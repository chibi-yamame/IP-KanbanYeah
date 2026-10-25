using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace KanbanDrawer.Win
{
    /// <summary>
    /// Text exactly like the program's 文字 tool: Java's logical font "SansSerif" on Japanese Windows (JRE fontconfig:
    /// sequence.sansserif.windows-31j = alphabetic, japanese → Arial, then MS Gothic; exclusion.alphabetic ranges go to
    /// MS Gothic), no anti-aliasing. MS Gothic carries hand-made bitmap glyphs for 7-22 px, which both Java and GDI use
    /// for unrotated, non-anti-aliased text at those sizes. Glyphs come from GDI TextOut (NONANTIALIASED_QUALITY); line
    /// metrics are Java's (measured with the game's JRE, tools/fontcheck).
    /// </summary>
    public static class DotFont
    {
        public const int MinSize = 7, MaxSize = 22;

        /// <summary>A glyph: advance and the pixels set, relative to the pen at the baseline.</summary>
        public sealed class Glyph
        {
            public int Advance;
            public readonly List<(int X, int Y)> Pixels = new List<(int, int)>();
        }

        /// <summary>Java's FontMetrics of SansSerif (ascent, descent, height) per size, plain / bold.</summary>
        public static void Metrics(int size, bool bold, out int ascent, out int descent, out int height)
        {
            var m = JavaMetrics.TryGetValue((size, bold), out var v) ? v : (size + size / 12 + 1, size / 4 + 1, size + size / 3);
            ascent = m.Item1; descent = m.Item2; height = m.Item3;
        }

        // measured with the game's JRE 1.8.0_503 (tools/fontcheck/src/JavaText.java), the same for plain and bold:
        // (ascent, descent, height)
        static readonly Dictionary<(int, bool), (int, int, int)> JavaMetrics = BuildMetrics();

        static Dictionary<(int, bool), (int, int, int)> BuildMetrics()
        {
            var t = new (int, int, int)[]
            {
                (7, 2, 9), (8, 2, 10), (9, 2, 12), (11, 3, 14), (12, 3, 15), (13, 3, 16), (14, 3, 18), (15, 4, 19),
                (16, 4, 20), (17, 4, 21), (18, 4, 23), (19, 4, 24), (20, 5, 25), (21, 5, 26), (22, 5, 28), (23, 5, 29),
            };
            var d = new Dictionary<(int, bool), (int, int, int)>();
            for (int i = 0; i < t.Length; i++)
            {
                d[(MinSize + i, false)] = t[i];
                d[(MinSize + i, true)] = t[i];
            }
            return d;
        }

        /// <summary>
        /// Java's advance: for MS Gothic full-width glyphs it is the size (+1 bold) where GDI reports one more at odd sizes;
        /// otherwise GDI's (tools/fontcheck: Arial advances agreed at every size).
        /// </summary>
        public static int JavaAdvance(int cp, int size, bool bold, int gdiAdvance)
        {
            if (IsAlphabetic(cp, bold) || gdiAdvance < size) return gdiAdvance;
            return size + (bold ? 1 : 0);
        }

        /// <summary>Whether Java draws this character with the alphabetic font (Arial) rather than MS Gothic.</summary>
        public static bool IsAlphabetic(int cp, bool bold)
        {
            // exclusion.alphabetic=0700-1cff,1d80-1e9f,1f00-2017,2020-20ab,20ad-f8ff
            if ((cp >= 0x0700 && cp <= 0x1cff) || (cp >= 0x1d80 && cp <= 0x1e9f) || (cp >= 0x1f00 && cp <= 0x2017)
                || (cp >= 0x2020 && cp <= 0x20ab) || (cp >= 0x20ad && cp <= 0xf8ff)) return false;
            return HasGlyph(Face(true, bold), cp);
        }

        static string Face(bool alphabetic, bool bold) => alphabetic ? "Arial" : "ＭＳ ゴシック";

        static readonly Dictionary<(int, bool, int), Glyph> Cache = new Dictionary<(int, bool, int), Glyph>();

        public static Glyph Render(int cp, int size, bool bold)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue((size, bold, cp), out var g)) return g;
                g = RenderUncached(cp, size, bold);
                Cache[(size, bold, cp)] = g;
                return g;
            }
        }

        static Glyph RenderUncached(int cp, int size, bool bold)
        {
            bool alpha = IsAlphabetic(cp, bold);
            string s = char.ConvertFromUtf32(cp);
            const int W = 96, H = 96, Ox = 16, Oy = 64; // pen at (Ox, Oy) on the baseline
            var glyph = new Glyph();
            IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
            var bmi = new BITMAPINFOHEADER { biSize = 40, biWidth = W, biHeight = -H, biPlanes = 1, biBitCount = 32 };
            IntPtr dib = CreateDIBSection(dc, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
            IntPtr font = CreateFont(-size, 0, 0, 0, bold ? 700 : 400, 0, 0, 0, alpha ? 0u : 128u /* SHIFTJIS */, 0, 0,
                3 /* NONANTIALIASED_QUALITY */, 0, Face(alpha, bold));
            try
            {
                IntPtr oldBmp = SelectObject(dc, dib);
                IntPtr oldFont = SelectObject(dc, font);
                var px = new int[W * H];
                for (int i = 0; i < px.Length; i++) px[i] = 0xFFFFFF;
                Marshal.Copy(px, 0, bits, px.Length);
                SetBkMode(dc, 1);           // TRANSPARENT
                SetTextColor(dc, 0);
                SetTextAlign(dc, 24);       // TA_BASELINE | TA_LEFT
                TextOutW(dc, Ox, Oy, s, s.Length);
                GetTextExtentPoint32W(dc, s, s.Length, out SIZE ext);
                glyph.Advance = ext.cx;
                GdiFlush();
                Marshal.Copy(bits, px, 0, px.Length);
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                        if ((px[y * W + x] & 0xFFFFFF) == 0) glyph.Pixels.Add((x - Ox, y - Oy));
                SelectObject(dc, oldFont);
                SelectObject(dc, oldBmp);
            }
            finally
            {
                DeleteObject(font);
                DeleteObject(dib);
                DeleteDC(dc);
            }
            return glyph;
        }

        static readonly Dictionary<(string, int), bool> GlyphCache = new Dictionary<(string, int), bool>();

        static bool HasGlyph(string face, int cp)
        {
            lock (GlyphCache)
            {
                if (GlyphCache.TryGetValue((face, cp), out bool has)) return has;
                IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
                IntPtr font = CreateFont(-12, 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 3, 0, face);
                try
                {
                    IntPtr old = SelectObject(dc, font);
                    string s = char.ConvertFromUtf32(cp);
                    var idx = new ushort[s.Length];
                    GetGlyphIndicesW(dc, s, s.Length, idx, 1 /* GGI_MARK_NONEXISTING_GLYPHS */);
                    has = s.Length == 1 && idx[0] != 0xFFFF;
                    SelectObject(dc, old);
                }
                finally
                {
                    DeleteObject(font);
                    DeleteDC(dc);
                }
                GlyphCache[(face, cp)] = has;
                return has;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SIZE { public int cx, cy; }

        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateFont(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline,
            uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);
        [DllImport("gdi32.dll")] static extern int SetBkMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] static extern uint SetTextColor(IntPtr dc, int color);
        [DllImport("gdi32.dll")] static extern uint SetTextAlign(IntPtr dc, uint align);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool TextOutW(IntPtr dc, int x, int y, string s, int n);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool GetTextExtentPoint32W(IntPtr dc, string s, int n, out SIZE size);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern uint GetGlyphIndicesW(IntPtr dc, string s, int n, ushort[] idx, uint flags);
        [DllImport("gdi32.dll")] static extern bool GdiFlush();
    }
}
