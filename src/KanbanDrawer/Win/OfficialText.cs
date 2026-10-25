using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace KanbanDrawer.Win
{
    /// <summary>A rendered text block: 1 = ink, width x height (widest line + 2 x lines * line height).</summary>
    public sealed class TextBitmap
    {
        public readonly int Width, Height;
        public readonly byte[] Bits;
        public TextBitmap(int w, int h, byte[] bits) { Width = w; Height = h; Bits = bits; }
    }

    /// <summary>
    /// Text exactly as the program's 文字 tool draws it (TextTool: Java "SansSerif" 12, plain, no anti-aliasing), 10-22 px.
    /// Drawn by the program's own renderer: KanbanText (Java/KanbanText.java, embedded jar) in one long-lived helper process
    /// started with the Java that opens .jar files (<see cref="JavaHelper.FindJava"/> = the one InterPot3's shortcut uses).
    /// Without such a Java the text is approximated with GDI (<see cref="Approximate"/>: Japanese identical, 英数字 a few
    /// pixels off). The program's tool has no bold, so neither has this mode.
    /// </summary>
    public static class OfficialText
    {
        // 10-22: where MS Gothic's bitmap glyphs make even the GDI approximation of Japanese identical; the program's own
        // default is 12.
        public const int MinSize = 10, MaxSize = DotFont.MaxSize, DefaultSize = 12;

        static readonly Dictionary<(int, string), TextBitmap> Cache = new Dictionary<(int, string), TextBitmap>();
        static readonly object Gate = new object();
        static Process helper;
        static bool failed;

        /// <summary>True while text comes from the program's renderer (Java); false = GDI approximation.</summary>
        public static bool Exact { get { lock (Gate) return helper != null && !failed; } }

        /// <summary>Raised (on any thread) when it turns out whether Java renders the text.</summary>
        public static event Action ExactChanged;

        /// <summary>Starts the helper in the background, so the first text does not wait for the JVM.</summary>
        public static void Warmup() => Task.Run(() => { lock (Gate) EnsureHelper(); });

        public static TextBitmap Render(string text, int size)
        {
            size = Math.Max(MinSize, Math.Min(MaxSize, size));
            text = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            lock (Gate)
            {
                if (Cache.TryGetValue((size, text), out var hit)) return hit;
                var bmp = (EnsureHelper() ? AskJava(text, size) : null) ?? Approximate(text, size);
                if (Cache.Count > 500) Cache.Clear();
                Cache[(size, text)] = bmp;
                return bmp;
            }
        }

        static bool EnsureHelper()
        {
            if (failed) return false;
            if (helper != null && !helper.HasExited) return true;
            try
            {
                string java = JavaHelper.FindJava();
                if (java == null) return Fail();
                string jar = JavaHelper.Extract("KanbanDrawer.kanban-text.jar", "kanban-text.jar");
                helper = JavaHelper.Start(java, $"-Djava.awt.headless=true -Dfile.encoding=UTF-8 -cp \"{jar}\" KanbanText");
                Cache.Clear(); // approximations made before the helper was there
                Notify();
                return true;
            }
            catch (Exception)
            {
                return Fail();
            }
        }

        static bool Fail()
        {
            bool was = failed;
            failed = true;
            try { if (helper != null && !helper.HasExited) helper.Kill(); } catch (Exception) { }
            helper = null;
            if (!was) Notify();
            return false;
        }

        static void Notify() => Task.Run(() => ExactChanged?.Invoke());

        static TextBitmap AskJava(string text, int size)
        {
            try
            {
                var req = Encoding.ASCII.GetBytes($"{size} 0 {Convert.ToBase64String(Encoding.UTF8.GetBytes(text))}\n");
                var stdin = helper.StandardInput.BaseStream;
                stdin.Write(req, 0, req.Length);
                stdin.Flush();
                var read = helper.StandardOutput.ReadLineAsync();
                if (!read.Wait(TimeSpan.FromSeconds(10))) { Fail(); return null; }
                var p = (read.Result ?? "").Split(' ');
                if (p.Length != 3) { Fail(); return null; }
                int w = int.Parse(p[0]), h = int.Parse(p[1]);
                var bits = Convert.FromBase64String(p[2]);
                return bits.Length == w * h ? new TextBitmap(w, h, bits) : null;
            }
            catch (Exception)
            {
                Fail();
                return null;
            }
        }

        /// <summary>
        /// GDI approximation with Java's line metrics and advances: MS Gothic's bitmap glyphs (identical to the program at
        /// 10-22 px), Arial for 英数字 (GDI rasterizes it a little differently from Java).
        /// </summary>
        internal static TextBitmap Approximate(string text, int size)
        {
            DotFont.Metrics(size, false, out int asc, out _, out int lineH);
            var lines = text.Split('\n');
            var placed = new List<(int X, int Y)>();
            int width = 2;
            for (int k = 0; k < lines.Length; k++)
            {
                int pen = 1, baseline = k * lineH + asc;
                string s = lines[k];
                for (int i = 0; i < s.Length; i += char.IsSurrogatePair(s, i) ? 2 : 1)
                {
                    int cp = char.ConvertToUtf32(s, i);
                    var g = DotFont.Render(cp, size, false);
                    foreach (var px in g.Pixels) placed.Add((pen + px.X, baseline + px.Y));
                    pen += DotFont.JavaAdvance(cp, size, false, g.Advance);
                }
                width = Math.Max(width, pen + 1);
            }
            int height = Math.Max(1, lines.Length * lineH);
            var bits = new byte[width * height];
            foreach (var (x, y) in placed)
                if (x >= 0 && y >= 0 && x < width && y < height) bits[y * width + x] = 1;
            return new TextBitmap(width, height, bits);
        }

        /// <summary>Lets the helper end (closing its input ends it; the job object ends it anyway when we exit).</summary>
        public static void Shutdown()
        {
            lock (Gate)
            {
                try { helper?.StandardInput.Close(); } catch (Exception) { }
                helper = null;
            }
        }
    }
}
