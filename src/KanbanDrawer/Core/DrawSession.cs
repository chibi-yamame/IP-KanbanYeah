using System;
using System.Diagnostics;

namespace KanbanDrawer.Core
{
    /// <summary>What the session needs from the machine (screen coordinates are absolute physical pixels).</summary>
    public interface IBoardIO
    {
        Raster Capture(int x, int y, int w, int h);
        void MoveTo(int x, int y);
        void Down();
        void Up();
        void Sleep(int ms);
        /// <summary>Null while drawing may go on, else why it must stop (Esc, the user moved the mouse, the game lost focus).</summary>
        string CheckInterrupt();
    }

    public sealed class DrawSettings
    {
        /// <summary>Pause after each stroke (ms). 0 = only the fences throttle (the program needs ~2 ms per stroke).</summary>
        public int StrokeDelayMs = 0;
        /// <summary>Pause after a button press / release and after clicks on buttons (ms).</summary>
        public int ClickDelayMs = 40;
        /// <summary>Wait for the program to show the last stroke every this many strokes.</summary>
        public int FenceEvery = 40;
        public int FenceTimeoutMs = 3000;
        /// <summary>Stop after this many fence timeouts in a row: the game's screen is not showing what it draws.</summary>
        public int MaxFenceTimeouts = 3;
        /// <summary>How long a clicked colour / pen button may take to show as pressed.</summary>
        public int PressTimeoutMs = 1000;
        /// <summary>Diagnostics: one line per step (pass, fence wait, button check, read-back), or null.</summary>
        public Action<string> Trace;
        /// <summary>Re-draw passes after reading the board back.</summary>
        public int MaxRetouch = 3;

        public DrawSettings Clone() => (DrawSettings)MemberwiseClone();
    }

    public enum DrawPhase { Prepare, Clear, Strokes, Verify, Retouch, Done }

    public readonly struct DrawProgress
    {
        public readonly DrawPhase Phase;
        public readonly int Done, Total;
        public readonly string Message;

        public DrawProgress(DrawPhase phase, int done, int total, string message)
        {
            Phase = phase; Done = done; Total = total; Message = message;
        }
    }

    public sealed class DrawResult
    {
        public bool Completed;
        public string InterruptReason;
        public int WrongPixels = -1;
        public int StrokesDrawn;
        public int RetouchRounds;
        public TimeSpan Elapsed;
    }

    public sealed class DrawInterruptedException : Exception
    {
        public DrawInterruptedException(string reason) : base(reason) { }
    }

    /// <summary>
    /// Drives the editor: pen 1px, CLR, colour passes, then read back and re-draw what is wrong. Every stroke is
    /// press at one end, move to the other end, release; the program draws the line on release even if moves are
    /// coalesced away. Stops at the first interrupt and always releases the button.
    /// </summary>
    public sealed class DrawSession
    {
        readonly IBoardIO io;
        readonly Pt origin;
        readonly IndexedImage target;
        readonly DrawSettings set;
        readonly IProgress<DrawProgress> progress;
        bool down;
        int strokesDrawn;
        int fenceTimeouts;

        public DrawSession(IBoardIO io, Pt canvasOrigin, IndexedImage target, DrawSettings settings, IProgress<DrawProgress> progress = null)
        {
            if (target.Width != BoardGeometry.Width || target.Height != BoardGeometry.Height)
                throw new ArgumentException("target must be 320x240");
            this.io = io; origin = canvasOrigin; this.target = target; set = settings; this.progress = progress;
        }

        /// <summary>Draws <paramref name="plan"/> (null = plan from what the board shows now, keeping correct pixels).</summary>
        public DrawResult Run(DrawPlan plan)
        {
            var sw = Stopwatch.StartNew();
            var result = new DrawResult();
            try
            {
                Report(DrawPhase.Prepare, 0, 0, "準備");
                EnsureEditor();
                SelectPen1();
                if (plan == null)
                    plan = StrokePlanner.PlanFrom(target, CaptureCanvas().Pixels, true);
                Execute(plan, DrawPhase.Strokes);
                for (int round = 0; ; round++)
                {
                    Report(DrawPhase.Verify, 0, 0, "確認中");
                    var board = Settle();
                    result.WrongPixels = StrokePlanner.CountWrong(target, board.Pixels);
                    set.Trace?.Invoke($"read back: {result.WrongPixels} wrong pixels");
                    if (result.WrongPixels == 0 || round >= set.MaxRetouch) break;
                    result.RetouchRounds = round + 1;
                    EnsureEditor();
                    Execute(StrokePlanner.PlanFrom(target, board.Pixels, false), DrawPhase.Retouch);
                }
                result.Completed = result.WrongPixels == 0;
                Report(DrawPhase.Done, 1, 1, result.Completed ? "完了" : $"完了（{result.WrongPixels} 画素が一致しません）");
            }
            catch (DrawInterruptedException e)
            {
                result.InterruptReason = e.Message;
            }
            finally
            {
                if (down) { io.Up(); down = false; }
                result.StrokesDrawn = strokesDrawn;
                result.Elapsed = sw.Elapsed;
            }
            return result;
        }

        void Execute(DrawPlan plan, DrawPhase phase)
        {
            int total = plan.StrokeCount, done = 0;
            if (plan.ClearColor >= 0)
            {
                Report(DrawPhase.Clear, 0, total, "全面を塗りつぶし: " + Palette.All[plan.ClearColor].Name);
                SelectColor(Palette.All[plan.ClearColor]);
                Click(BoardGeometry.ClearCenter);
                WaitPixel(0, 0, Palette.All[plan.ClearColor].Rgb);
            }
            foreach (var pass in plan.Passes)
            {
                var c = Palette.All[pass.Color];
                set.Trace?.Invoke($"pass {c.Name}: {pass.Strokes.Count} strokes ({(pass.Vertical ? "vertical" : "horizontal")})");
                SelectColor(c);
                for (int i = 0; i < pass.Strokes.Count; i++)
                {
                    var s = pass.Strokes[i];
                    Stroke(s);
                    done++;
                    bool last = i == pass.Strokes.Count - 1;
                    if (last || (i + 1) % set.FenceEvery == 0)
                    {
                        WaitPixel(s.X1, s.Y1, c.Rgb);
                        Report(phase, done, total, $"{c.Name} {i + 1}/{pass.Strokes.Count}");
                    }
                }
            }
        }

        void Stroke(Stroke s)
        {
            Check();
            io.MoveTo(origin.X + s.X0, origin.Y + s.Y0);
            io.Down();
            down = true;
            if (s.X1 != s.X0 || s.Y1 != s.Y0)
                io.MoveTo(origin.X + s.X1, origin.Y + s.Y1);
            io.Up();
            down = false;
            strokesDrawn++;
            if (set.StrokeDelayMs > 0) io.Sleep(set.StrokeDelayMs);
        }

        void Click(Pt rel)
        {
            Check();
            io.MoveTo(origin.X + rel.X, origin.Y + rel.Y);
            io.Sleep(set.ClickDelayMs / 2);
            io.Down();
            down = true;
            io.Sleep(set.ClickDelayMs / 2);
            io.Up();
            down = false;
            io.Sleep(set.ClickDelayMs);
        }

        void SelectPen1()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                Click(BoardGeometry.Pen1Center);
                if (WaitPressed((shot, local) => BoardLocator.IsPen1Selected(shot, local))) return;
            }
            EnsureEditor();
            throw new DrawInterruptedException("1px ペンを選択できませんでした");
        }

        void SelectColor(PaletteColor c)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                Click(BoardGeometry.ColorButtonCenter(c));
                if (WaitPressed((shot, local) => BoardLocator.IsColorSelected(shot, local, c))) return;
            }
            EnsureEditor();
            throw new DrawInterruptedException($"色「{c.Name}」を選択できませんでした");
        }

        /// <summary>
        /// Polls the button's pressed look for up to <see cref="DrawSettings.PressTimeoutMs"/>: the program repaints its
        /// (lightweight) buttons later than the click, judging right away gave false failures in the real game.
        /// </summary>
        bool WaitPressed(Func<Raster, Pt, bool?> pressed)
        {
            var sw = Stopwatch.StartNew();
            while (true)
            {
                var shot = CaptureTools(out var local);
                if (pressed(shot, local) == true)
                {
                    set.Trace?.Invoke($"button pressed after {sw.ElapsedMilliseconds} ms");
                    return true;
                }
                if (sw.ElapsedMilliseconds >= set.PressTimeoutMs)
                {
                    set.Trace?.Invoke("button not shown pressed");
                    return false;
                }
                Check();
                io.Sleep(20);
            }
        }

        /// <summary>Waits until a canvas pixel shows <paramref name="rgb"/>: the program has caught up with the input.</summary>
        void WaitPixel(int x, int y, int rgb)
        {
            var sw = Stopwatch.StartNew();
            while (true)
            {
                var r = io.Capture(origin.X + x, origin.Y + y, 1, 1);
                if ((r.Pixels[0] & 0xFFFFFF) == rgb)
                {
                    fenceTimeouts = 0;
                    if (sw.ElapsedMilliseconds > 200) set.Trace?.Invoke($"fence ({x},{y}) slow: {sw.ElapsedMilliseconds} ms");
                    return;
                }
                if (sw.ElapsedMilliseconds > set.FenceTimeoutMs)
                {
                    set.Trace?.Invoke($"fence ({x},{y}) timed out: wanted #{rgb:X6}, screen shows #{r.Pixels[0] & 0xFFFFFF:X6}");
                    // One is not fatal (the read-back pass re-draws what is missing), but the editor must still be there,
                    // and several in a row mean the screen does not follow the drawing (seen with Java2D's Direct3D
                    // pipeline after the display was off: images stop being painted while buttons still are).
                    EnsureEditor();
                    if (++fenceTimeouts >= set.MaxFenceTimeouts)
                        throw new DrawInterruptedException("ゲームの画面が描画に追いついていません（看板の表示が更新されない）。" +
                            "ゲームを最小化して元に戻すか、ゲームを再起動してから「続きから / 手直し」を押してください");
                    return;
                }
                Check();
                io.Sleep(5);
            }
        }

        /// <summary>Captures the canvas until two captures agree (the program has painted everything).</summary>
        Raster Settle()
        {
            var a = CaptureCanvas();
            for (int i = 0; i < 40; i++)
            {
                io.Sleep(100);
                Check();
                var b = CaptureCanvas();
                if (Same(a, b)) return b;
                a = b;
            }
            return a;
        }

        static bool Same(Raster a, Raster b)
        {
            for (int i = 0; i < a.Pixels.Length; i++)
                if (((a.Pixels[i] ^ b.Pixels[i]) & 0xFFFFFF) != 0) return false;
            return true;
        }

        void EnsureEditor()
        {
            var shot = CaptureTools(out var local);
            if (!BoardLocator.Matches(shot, local))
                throw new DrawInterruptedException("看板エディタが見つかりません（移動した・隠れた・閉じた）");
        }

        Raster CaptureCanvas() => io.Capture(origin.X, origin.Y, BoardGeometry.Width, BoardGeometry.Height);

        /// <summary>Captures canvas + tool panel; <paramref name="local"/> is the canvas origin inside the capture.</summary>
        Raster CaptureTools(out Pt local)
        {
            BoardGeometry.Extent(out int l, out int t, out int r, out int b);
            local = new Pt(-l, -t);
            return io.Capture(origin.X + l, origin.Y + t, r - l, b - t);
        }

        void Check()
        {
            string why = io.CheckInterrupt();
            if (why != null) throw new DrawInterruptedException(why);
        }

        void Report(DrawPhase phase, int done, int total, string msg) =>
            progress?.Report(new DrawProgress(phase, done, total, msg));
    }
}
