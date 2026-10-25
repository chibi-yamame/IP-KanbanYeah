using System;
using System.Threading;
using KanbanDrawer.Core;

namespace KanbanDrawer.Win
{
    /// <summary>Find the editor, bring it to the front, count down, check the coordinate mapping, draw.</summary>
    public static class DrawRunner
    {
        /// <summary>When set, a screen capture around the editor is saved here if drawing stops early.</summary>
        public static string DebugShotPath;

        /// <param name="plan">null = plan from the board as it is (resume / touch up)</param>
        /// <param name="log">status lines (any thread)</param>
        public static DrawResult Run(IndexedImage target, DrawPlan plan, DrawSettings settings, InputMode mode,
            int countdownSeconds, Action<string> log, IProgress<DrawProgress> progress, CancellationToken cancel = default)
        {
            settings = settings.Clone();
            var tracePath = Environment.GetEnvironmentVariable("KANBANDRAWER_TRACE");
            if (settings.Trace == null && !string.IsNullOrEmpty(tracePath))
            {
                var start = System.Diagnostics.Stopwatch.StartNew();
                settings.Trace = line =>
                {
                    try { System.IO.File.AppendAllText(tracePath, $"{start.ElapsedMilliseconds,8} {line}{Environment.NewLine}"); }
                    catch (System.IO.IOException) { }
                };
            }
            if (mode == InputMode.Send) settings.FenceEvery = 1; // see InputMode.Send
            var board = BoardFinder.Find(out string error);
            if (board == null) return Fail(error, log);
            log($"看板を検出: キャンバス左上 {board.Origin} / ウィンドウ「{board.WindowTitle}」");

            // Post mode works in the background, but the board must stay visible for reading it back.
            if (!InputDriver.BringToFront(board.Window) && mode == InputMode.Send)
                return Fail("ゲームのウィンドウを前面にできませんでした。ゲームをクリックしてから再度お試しください。", log);

            using (var io = new InputDriver(board.Window, mode))
            {
                for (int s = countdownSeconds; s > 0; s--)
                {
                    log(mode == InputMode.Send
                        ? $"{s} 秒後に描画を始めます。マウスに触らないでください（Esc で中止）"
                        : $"{s} 秒後に描画を始めます（Esc で中止）");
                    for (int i = 0; i < 10; i++)
                    {
                        Thread.Sleep(100);
                        if (cancel.IsCancellationRequested) return Fail("中止しました", log);
                        string why = io.CheckInterrupt();
                        if (why != null) return Fail(why, log);
                    }
                }
                // Same place still, and the cursor lands on exact pixels.
                var again = BoardFinder.Find(out error);
                if (again == null) return Fail(error, log);
                if (again.Origin.X != board.Origin.X || again.Origin.Y != board.Origin.Y)
                    return Fail("看板が移動しました。もう一度お試しください。", log);
                var o = board.Origin;
                if (!io.VerifyMapping(o.X + BoardGeometry.Width + 2, o.Y + 3))
                    return Fail(mode == InputMode.Send
                        ? "マウス座標が一致しません（画面の拡大率やリモートデスクトップを確認してください）"
                        : "ゲームのウィンドウ構成が想定と違います（入力方式を SendInput にしてお試しください）", log);

                if (mode == InputMode.Post)
                {
                    if (!CalibratePost(io, o))
                        return Fail("ゲームがマウス操作に反応しません（入力方式を SendInput にしてお試しください）", log);
                    log("入力方式: PostMessage（" + (io.PostScreenCoords ? "スクリーン座標" : "クライアント座標") + "）。描画中もマウスは自由に使えます");
                }

                var session = new DrawSession(new CancellableIO(io, cancel), o, target, settings, progress);
                var result = session.Run(plan);
                if (result.InterruptReason != null && DebugShotPath != null)
                {
                    // what the screen looked like when it stopped (diagnostics, CLI --debug-shot)
                    using (var bmp = ImageLoader.ToBitmap(ScreenCapture.Capture(o.X - 200, o.Y - 200, 760, 760)))
                        bmp.Save(DebugShotPath, System.Drawing.Imaging.ImageFormat.Png);
                }
                if (result.InterruptReason != null) log("中断: " + result.InterruptReason);
                else log(result.Completed
                    ? $"完了: {result.StrokesDrawn} ストローク / {result.Elapsed.TotalSeconds:F1} 秒"
                    : $"終了: 一致しない画素 {result.WrongPixels} / {result.StrokesDrawn} ストローク / {result.Elapsed.TotalSeconds:F1} 秒");
                return result;
            }
        }

        /// <summary>
        /// Finds which coordinates the game reads from posted messages: clicks a pen button that is not selected yet
        /// and looks whether it got pressed.
        /// </summary>
        static bool CalibratePost(InputDriver io, Pt o)
        {
            BoardGeometry.Extent(out int l, out int t, out int r, out int b);
            foreach (bool screen in new[] { true, false })
            {
                io.PostScreenCoords = screen;
                var shot = io.Capture(o.X + l, o.Y + t, r - l, b - t);
                var local = new Pt(-l, -t);
                bool pen1 = BoardLocator.IsPen1Selected(shot, local) == true;
                Pt button = pen1 ? BoardGeometry.Pen4Button : BoardGeometry.Pen1Button;
                Pt center = pen1 ? BoardGeometry.Pen4Center : BoardGeometry.Pen1Center;
                io.MoveTo(o.X + center.X, o.Y + center.Y);
                io.Down();
                io.Up();
                for (int i = 0; i < 20; i++)
                {
                    Thread.Sleep(25);
                    shot = io.Capture(o.X + l, o.Y + t, r - l, b - t);
                    if (BoardLocator.IsPressed(shot, local.Offset(button.X, button.Y)) == true) return true;
                }
            }
            return false;
        }

        static DrawResult Fail(string why, Action<string> log)
        {
            log(why);
            return new DrawResult { InterruptReason = why };
        }

        /// <summary>Adds the UI's 中止 button to the interrupt checks.</summary>
        sealed class CancellableIO : IBoardIO
        {
            readonly IBoardIO inner;
            readonly CancellationToken cancel;
            public CancellableIO(IBoardIO inner, CancellationToken cancel) { this.inner = inner; this.cancel = cancel; }
            public Raster Capture(int x, int y, int w, int h) => inner.Capture(x, y, w, h);
            public void MoveTo(int x, int y) => inner.MoveTo(x, y);
            public void Down() => inner.Down();
            public void Up() => inner.Up();
            public void Sleep(int ms) => inner.Sleep(ms);
            public string CheckInterrupt() => cancel.IsCancellationRequested ? "中止しました" : inner.CheckInterrupt();
        }
    }
}
