using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using KanbanDrawer.Core;

namespace KanbanDrawer.Win
{
    public enum InputMode
    {
        /// <summary>
        /// Mouse messages posted straight to the game's windows, with the point inside each message. The real cursor
        /// does not move, order and positions are exact. Default.
        /// </summary>
        Post,
        /// <summary>
        /// Real mouse input (SendInput). Windows hands out WM_MOUSEMOVE with the cursor position at the time the program
        /// reads it, so when the program lags a drag lands on a later stroke's point: every stroke is synchronised
        /// (slow). Fallback only.
        /// </summary>
        Send,
    }

    /// <summary>
    /// Mouse input to the game plus the safety checks. Post mode: Esc, or a physical click on the game window.
    /// Send mode: Esc, any physical mouse use (a low-level hook sees events without LLMHF_INJECTED), or the game
    /// losing the foreground.
    /// </summary>
    internal sealed class InputDriver : IBoardIO, IDisposable
    {
        readonly IntPtr gameRoot;
        readonly InputMode mode;
        readonly MouseGuard guard;
        readonly Native.RECT vs;
        bool disposed;
        // Post mode: current point (screen) and the window that got the button press (gets everything until release)
        int curX, curY;
        IntPtr pressedWindow;

        public InputDriver(IntPtr gameRoot, InputMode mode)
        {
            this.gameRoot = gameRoot;
            this.mode = mode;
            vs = ScreenCapture.VirtualScreen();
            Native.timeBeginPeriod(1);
            Native.GetWindowRect(gameRoot, out var wr);
            guard = new MouseGuard(mode == InputMode.Send, wr);
        }

        public InputMode Mode => mode;

        /// <summary>
        /// Post mode: put screen coordinates into the messages. The JRE 8 AWT the game runs on reads posted mouse
        /// messages' lParam as screen coordinates (checked on the mock board, independent of the cursor);
        /// <see cref="DrawRunner"/> confirms it with a pen button click before drawing.
        /// </summary>
        public bool PostScreenCoords { get; set; } = true;

        public Raster Capture(int x, int y, int w, int h) => ScreenCapture.Capture(x, y, w, h);

        public void MoveTo(int x, int y)
        {
            if (mode == InputMode.Send)
            {
                Send(Native.MOUSEEVENTF_MOVE | Native.MOUSEEVENTF_ABSOLUTE | Native.MOUSEEVENTF_VIRTUALDESK, x, y);
                return;
            }
            curX = x; curY = y;
            // No moves while pressed: AWT's EventQueue replaces a pending MOUSE_DRAGGED with any later one (keeping
            // its place in the queue), so with a busy event thread a drag would carry a later stroke's point. The pen
            // draws press point -> release point anyway (PenTool.mouseUp). Other moves only when entering another
            // window (buttons may want to see the pointer first).
            if (pressedWindow != IntPtr.Zero) return;
            var target = TargetAt(x, y);
            if (target == lastMoveWindow) return;
            lastMoveWindow = target;
            Post(target, Native.WM_MOUSEMOVE, 0, x, y);
        }

        IntPtr lastMoveWindow;

        public void Down()
        {
            if (mode == InputMode.Send) { Send(Native.MOUSEEVENTF_LEFTDOWN, 0, 0); return; }
            pressedWindow = TargetAt(curX, curY);
            Post(pressedWindow, Native.WM_LBUTTONDOWN, Native.MK_LBUTTON, curX, curY);
        }

        public void Up()
        {
            if (mode == InputMode.Send) { Send(Native.MOUSEEVENTF_LEFTUP, 0, 0); return; }
            var target = pressedWindow != IntPtr.Zero ? pressedWindow : TargetAt(curX, curY);
            pressedWindow = IntPtr.Zero;
            Post(target, Native.WM_LBUTTONUP, 0, curX, curY);
        }

        public void Sleep(int ms) => Thread.Sleep(ms);

        public string CheckInterrupt()
        {
            if ((Native.GetAsyncKeyState(Native.VK_ESCAPE) & 0x8000) != 0) return "Esc キーで中断しました";
            if (guard.UserTouched)
                return mode == InputMode.Send ? "マウスが操作されたため中断しました" : "ゲームのウィンドウがクリックされたため中断しました";
            if (!Native.IsWindow(gameRoot)) return "ゲームのウィンドウが閉じられました";
            if (mode == InputMode.Send && Native.GetForegroundWindow() != gameRoot)
                return "ゲームのウィンドウが前面ではなくなったため中断しました";
            return null;
        }

        /// <summary>
        /// Send mode: moves the cursor and checks that it lands on the exact pixel. Post mode: checks that the point
        /// belongs to the game window.
        /// </summary>
        public bool VerifyMapping(int x, int y)
        {
            if (mode == InputMode.Post) return TargetAt(x, y) != IntPtr.Zero;
            MoveTo(x, y);
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 300)
            {
                Native.GetCursorPos(out var p);
                if (p.X == x && p.Y == y) return true;
                Thread.Sleep(5);
            }
            return false;
        }

        /// <summary>The deepest child window of the game under a screen point, ignoring other top-level windows.</summary>
        IntPtr TargetAt(int x, int y)
        {
            IntPtr cur = gameRoot;
            while (true)
            {
                var p = new Native.POINT { X = x, Y = y };
                Native.ScreenToClient(cur, ref p);
                var child = Native.ChildWindowFromPointEx(cur, p,
                    Native.CWP_SKIPINVISIBLE | Native.CWP_SKIPDISABLED | Native.CWP_SKIPTRANSPARENT);
                if (child == IntPtr.Zero || child == cur) return cur;
                cur = child;
            }
        }

        void Post(IntPtr hwnd, uint msg, int keys, int screenX, int screenY)
        {
            var p = new Native.POINT { X = screenX, Y = screenY };
            if (!PostScreenCoords) Native.ScreenToClient(hwnd, ref p);
            var l = (IntPtr)((p.Y << 16) | (p.X & 0xFFFF));
            if (!Native.PostMessage(hwnd, msg, (IntPtr)keys, l))
                throw new DrawInterruptedException("ゲームにマウス操作を送れませんでした（ゲームが閉じられた可能性）");
        }

        void Send(uint flags, int x, int y)
        {
            var input = new Native.INPUT { type = Native.INPUT_MOUSE };
            input.u.mi.dwFlags = flags;
            if ((flags & Native.MOUSEEVENTF_ABSOLUTE) != 0)
            {
                input.u.mi.dx = Normalize(x - vs.Left, vs.Right - vs.Left);
                input.u.mi.dy = Normalize(y - vs.Top, vs.Bottom - vs.Top);
            }
            if (Native.SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Native.INPUT))) != 1)
                throw new DrawInterruptedException("マウス入力を送れませんでした（権限の高いウィンドウが前面にある可能性）");
        }

        /// <summary>
        /// Windows maps an absolute coordinate n (0..65535) to pixel floor(n * size / 65536); aim at the middle of the
        /// interval that maps to <paramref name="v"/>.
        /// </summary>
        internal static int Normalize(int v, int size) => (int)(((2L * v + 1) * 65536) / (2L * size));

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            guard.Dispose();
            Native.timeEndPeriod(1);
        }

        /// <summary>Brings the game to the front (the Alt tap lets a background process take the foreground).</summary>
        public static bool BringToFront(IntPtr root)
        {
            if (Native.IsIconic(root)) Native.ShowWindow(root, Native.SW_RESTORE);
            if (Native.GetForegroundWindow() == root) return true;
            var alt = new Native.INPUT[2];
            alt[0].type = alt[1].type = Native.INPUT_KEYBOARD;
            alt[0].u.ki.wVk = alt[1].u.ki.wVk = Native.VK_MENU;
            alt[1].u.ki.dwFlags = Native.KEYEVENTF_KEYUP;
            Native.SendInput(2, alt, Marshal.SizeOf(typeof(Native.INPUT)));
            Native.SetForegroundWindow(root);
            for (int i = 0; i < 50 && Native.GetForegroundWindow() != root; i++) Thread.Sleep(10);
            return Native.GetForegroundWindow() == root;
        }

        /// <summary>Top-level window under a screen point.</summary>
        public static IntPtr RootAt(int x, int y)
        {
            var h = Native.WindowFromPoint(new Native.POINT { X = x, Y = y });
            return h == IntPtr.Zero ? IntPtr.Zero : Native.GetAncestor(h, Native.GA_ROOT);
        }

        /// <summary>Low-level mouse hook on its own thread (needs a message loop).</summary>
        sealed class MouseGuard : IDisposable
        {
            readonly Thread thread;
            readonly ManualResetEventSlim ready = new ManualResetEventSlim();
            readonly bool anyUse;
            readonly Native.RECT game;
            Native.LowLevelProc proc;
            uint threadId;
            volatile bool touched;
            int physicalMoves;

            public bool UserTouched => touched;

            /// <param name="anyUse">true: any physical move / click counts; false: only clicks inside <paramref name="game"/></param>
            public MouseGuard(bool anyUse, Native.RECT game)
            {
                this.anyUse = anyUse;
                this.game = game;
                thread = new Thread(Loop) { IsBackground = true, Name = "MouseGuard" };
                thread.Start();
                ready.Wait(2000);
            }

            void Loop()
            {
                threadId = Native.GetCurrentThreadId();
                proc = Hook;
                IntPtr hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, proc, Native.GetModuleHandle(null), 0);
                ready.Set();
                while (Native.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0) { }
                if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
            }

            IntPtr Hook(int code, IntPtr w, IntPtr l)
            {
                if (code >= 0)
                {
                    var info = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(l);
                    if ((info.flags & Native.LLMHF_INJECTED) == 0)
                    {
                        int m = w.ToInt32();
                        bool click = m == Native.WM_LBUTTONDOWN || m == Native.WM_RBUTTONDOWN || m == Native.WM_MBUTTONDOWN;
                        if (anyUse)
                        {
                            if (click) touched = true;
                            else if (m == Native.WM_MOUSEMOVE && ++physicalMoves >= 3)
                                touched = true; // a stray event or two (sensor jitter) is tolerated
                        }
                        else if (click && info.pt.X >= game.Left && info.pt.X < game.Right
                                 && info.pt.Y >= game.Top && info.pt.Y < game.Bottom)
                            touched = true;
                    }
                }
                return Native.CallNextHookEx(IntPtr.Zero, code, w, l);
            }

            public void Dispose()
            {
                if (threadId != 0) Native.PostThreadMessage(threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            }
        }
    }
}
