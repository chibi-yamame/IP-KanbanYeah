using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using KanbanDrawer.Core;
using KanbanDrawer.UI;
using KanbanDrawer.Win;

namespace KanbanDrawer
{
    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            // a picture dropped on the exe opens in the window; options mean command line mode
            bool picture = args.Length == 1 && !args[0].StartsWith("--") && File.Exists(args[0]);
            if (args.Length > 0 && !picture) return Cli.Run(args);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(picture ? args[0] : null));
            return 0;
        }
    }

    /// <summary>
    /// Command line mode (used by tools/e2e.sh). Exit codes: 0 drawn and verified, 1 drawn with wrong pixels left,
    /// 2 interrupted, 3 error (bad arguments, no editor found).
    ///   KanbanYeah.exe --draw pic.png [--fit contain|cover|stretch] [--dither none|fs|atkinson|bayer] [--strength 0-100]
    ///                    [--bg RRGGBB] [--delay ms] [--click-delay ms] [--fence n] [--retouch n] [--countdown s] [--input post|send]
    ///                    [--save-quantized out.png] [--log file]
    ///   KanbanYeah.exe --detect [--log file]
    ///   KanbanYeah.exe --quantize pic.png --save-quantized out.png [options]
    ///   KanbanYeah.exe --register-stamp pic.png [--remove-bg]                  (adds to the stamp library)
    ///   KanbanYeah.exe [--fit ...] --register-frame pic.png [--remove-bg]      (adds to the frame library)
    /// Settings and libraries live in %APPDATA%\KanbanDrawer, or in the folder named by KANBANDRAWER_DATA.
    /// </summary>
    internal static class Cli
    {
        static TextWriter logFile;

        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

        public static int Run(string[] args)
        {
            AttachConsole(-1);
            try { Console.OutputEncoding = new UTF8Encoding(false); } catch { }
            string draw = null, quantize = null, saveQ = null, logPath = null;
            bool detect = false;
            var fit = FitMode.Contain;
            var q = new QuantizeOptions();
            var set = new DrawSettings();
            int countdown = 2;
            var mode = InputMode.Post;
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i];
                    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException(a + " needs a value");
                    switch (a)
                    {
                        case "--draw": draw = Next(); break;
                        case "--quantize": quantize = Next(); break;
                        case "--detect": detect = true; break;
                        case "--compare": return Compare(Next(), Next());
                        case "--dotfont-dump": return DotFontDump(Next(), Next(), Next());
                        case "--register-stamp": return RegisterStamp(Next(), Array.IndexOf(args, "--remove-bg") >= 0);
                        case "--register-frame": return RegisterFrame(Next(), fit, Array.IndexOf(args, "--remove-bg") >= 0);
                        case "--remove-bg": break; // read by --register-stamp / --register-frame (either order)
                        case "--fit": fit = (FitMode)Enum.Parse(typeof(FitMode), Next(), true); break;
                        case "--dither": q.Dither = ParseDither(Next()); break;
                        case "--strength": q.Strength = int.Parse(Next()) / 100.0; break;
                        case "--bg": q.Background = int.Parse(Next(), NumberStyles.HexNumber); break;
                        case "--delay": set.StrokeDelayMs = int.Parse(Next()); break;
                        case "--click-delay": set.ClickDelayMs = int.Parse(Next()); break;
                        case "--fence": set.FenceEvery = int.Parse(Next()); break;
                        case "--retouch": set.MaxRetouch = int.Parse(Next()); break;
                        case "--countdown": countdown = int.Parse(Next()); break;
                        case "--input": mode = (InputMode)Enum.Parse(typeof(InputMode), Next(), true); break;
                        case "--save-quantized": saveQ = Next(); break;
                        case "--log": logPath = Next(); break;
                        case "--debug-shot": DrawRunner.DebugShotPath = Next(); break;
                        default: throw new ArgumentException("unknown option " + a);
                    }
                }
                if (logPath != null) logFile = new StreamWriter(logPath, false, new UTF8Encoding(false)) { AutoFlush = true };

                if (detect)
                {
                    var b = BoardFinder.Find(out string err);
                    Log(b == null ? err : $"origin={b.Origin.X},{b.Origin.Y} window=\"{b.WindowTitle}\"");
                    return b == null ? 3 : 0;
                }
                string src = draw ?? quantize ?? throw new ArgumentException("--draw, --quantize or --detect is required");
                IndexedImage target;
                using (var bmp = ImageLoader.Load(src))
                    target = Quantizer.Quantize(ImageLoader.Fit(bmp, fit), q);
                if (saveQ != null)
                    using (var outBmp = ImageLoader.ToBitmap(target.ToRaster()))
                        outBmp.Save(saveQ, System.Drawing.Imaging.ImageFormat.Png);
                var plan = StrokePlanner.PlanFull(target);
                Log($"plan: CLR={(plan.ClearColor >= 0 ? Palette.All[plan.ClearColor].Name : "-")} passes={plan.Passes.Count} strokes={plan.StrokeCount}");
                if (draw == null) return 0;

                var result = DrawRunner.Run(target, plan, set, mode, countdown, Log,
                    new Progress<DrawProgress>(p => { }));
                Log($"result: completed={result.Completed} wrong={result.WrongPixels} strokes={result.StrokesDrawn} " +
                    $"retouch={result.RetouchRounds} seconds={result.Elapsed.TotalSeconds:F1} interrupted={result.InterruptReason ?? "-"}");
                if (result.InterruptReason != null) return result.StrokesDrawn > 0 ? 2 : 3;
                return result.Completed ? 0 : 1;
            }
            catch (Exception e)
            {
                Log("error: " + e.Message);
                return 3;
            }
            finally
            {
                logFile?.Dispose();
            }
        }

        /// <summary>
        /// tools/fontcheck: the dot-font glyphs as "advance" + "x,y" pixel lines (pen at the origin on the baseline), one
        /// file per size / weight / character, to compare with the game's JRE.
        /// </summary>
        static int DotFontDump(string dir, string sizes, string charsFile)
        {
            Directory.CreateDirectory(dir);
            var r = sizes.Split('-');
            int from = int.Parse(r[0]), to = int.Parse(r[r.Length - 1]);
            string text = File.ReadAllText(charsFile, Encoding.UTF8).Trim();
            for (int size = from; size <= to; size++)
                foreach (bool bold in new[] { false, true })
                {
                    int i = 0;
                    for (int k = 0; k < text.Length; k += char.IsSurrogatePair(text, k) ? 2 : 1, i++)
                    {
                        int cp = char.ConvertToUtf32(text, k);
                        var g = DotFont.Render(cp, size, bold);
                        var sb = new StringBuilder();
                        sb.Append(g.Advance).Append(DotFont.IsAlphabetic(cp, bold) ? " A" : " J").Append('\n');
                        foreach (var p in g.Pixels) sb.Append(p.X).Append(',').Append(p.Y).Append('\n');
                        File.WriteAllText(Path.Combine(dir, $"{size}{(bold ? "b" : "")}_{i}.txt"), sb.ToString());
                    }
                }
            return 0;
        }

        /// <summary>Adds a picture to the stamp library (as the スタンプ tab does).</summary>
        static int RegisterStamp(string path, bool removeBackground)
        {
            Raster src;
            using (var bmp = ImageLoader.Load(path)) src = ImageLoader.ToRaster(bmp);
            var prepared = Core.Board.StampPrep.Prepare(src, removeBackground);
            if (!Core.Board.StampPrep.FitsBoard(prepared))
            {
                Log($"stamp: too large ({prepared.Width}x{prepared.Height}, at most 320x240)");
                return 1;
            }
            Log($"stamp: {new StampLibrary().Add(prepared)} ({prepared.Width}x{prepared.Height})");
            return 0;
        }

        /// <summary>
        /// Adds a picture to the frame library (as the 下地 tab does): placed on 320x240 in full colour, transparency kept
        /// (a --fit given before it applies; 減色 options are not used, frames are quantized with the draft).
        /// </summary>
        static int RegisterFrame(string path, FitMode fit, bool removeBackground)
        {
            Log("frame: " + new FrameLibrary().Register(path, fit, removeBackground));
            return 0;
        }

        /// <summary>Pixel-exact comparison of two images (RGB): 0 = same, 1 = different.</summary>
        static int Compare(string a, string b)
        {
            using (var ba = ImageLoader.Load(a))
            using (var bb = ImageLoader.Load(b))
            {
                var ra = ImageLoader.ToRaster(ba);
                var rb = ImageLoader.ToRaster(bb);
                if (ra.Width != rb.Width || ra.Height != rb.Height)
                {
                    Log($"compare: size differs {ra.Width}x{ra.Height} / {rb.Width}x{rb.Height}");
                    return 1;
                }
                int diff = 0;
                for (int i = 0; i < ra.Pixels.Length; i++)
                    if (((ra.Pixels[i] ^ rb.Pixels[i]) & 0xFFFFFF) != 0) diff++;
                Log($"compare: {diff} pixels differ");
                return diff == 0 ? 0 : 1;
            }
        }

        static DitherKind ParseDither(string s)
        {
            switch (s.ToLowerInvariant())
            {
                case "none": return DitherKind.None;
                case "fs": case "floyd": return DitherKind.FloydSteinberg;
                case "atkinson": return DitherKind.Atkinson;
                case "bayer": return DitherKind.Bayer4;
                default: throw new ArgumentException("unknown dither " + s);
            }
        }

        static void Log(string s)
        {
            string line = DateTime.Now.ToString("HH:mm:ss.fff ") + s;
            try { Console.WriteLine(line); } catch { }
            logFile?.WriteLine(line);
        }
    }
}
