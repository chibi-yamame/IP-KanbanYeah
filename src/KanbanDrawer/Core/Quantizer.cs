using System;
using System.Collections.Generic;
using KanbanDrawer.Core.Board;

namespace KanbanDrawer.Core
{
    public enum DitherKind { None, FloydSteinberg, Atkinson, Bayer4 }

    public sealed class QuantizeOptions
    {
        public DitherKind Dither = DitherKind.FloydSteinberg;
        /// <summary>0..1, scales the diffused error / ordered threshold.</summary>
        public double Strength = 1.0;
        /// <summary>Which palette colours may be used (length Palette.Count). At least one must be true.</summary>
        public bool[] Enabled = { true, true, true, true, true, true, true, true, true, true };
        /// <summary>Colour transparent pixels are composited over (0xRRGGBB) by <see cref="Quantizer.Quantize(Raster, QuantizeOptions)"/>.</summary>
        public int Background = 0xFFFFFF;
        /// <summary>-100..100</summary>
        public int Brightness;
        /// <summary>-100..100</summary>
        public int Contrast;
        /// <summary>0..200 (%), 100 = unchanged.</summary>
        public int Saturation = 100;

        public QuantizeOptions Clone()
        {
            var o = (QuantizeOptions)MemberwiseClone();
            o.Enabled = (bool[])Enabled.Clone();
            return o;
        }
    }

    /// <summary>
    /// Maps a picture to the signboard palette (perceptual nearest colour + optional dithering). With a
    /// <see cref="Composite"/>, fixed pixels (background colour, text) keep their index and neither give nor
    /// take dithering error; only free pixels (frame, pictures, stamps) are adjusted and quantized.
    /// </summary>
    public static class Quantizer
    {
        static readonly int[,] Bayer4 =
        {
            { 0, 8, 2, 10 },
            { 12, 4, 14, 6 },
            { 3, 11, 1, 9 },
            { 15, 7, 13, 5 },
        };

        /// <summary>A plain picture (every pixel free), transparency over <see cref="QuantizeOptions.Background"/>.</summary>
        public static IndexedImage Quantize(Raster src, QuantizeOptions opt)
        {
            var c = new Composite(src.Width, src.Height);
            int br = opt.Background >> 16 & 255, bg = opt.Background >> 8 & 255, bb = opt.Background & 255;
            for (int i = 0; i < src.Pixels.Length; i++)
            {
                int p = src.Pixels[i];
                double a = ((p >> 24) & 255) / 255.0;
                c.Rgb[i * 3] = (float)(((p >> 16) & 255) * a + br * (1 - a));
                c.Rgb[i * 3 + 1] = (float)(((p >> 8) & 255) * a + bg * (1 - a));
                c.Rgb[i * 3 + 2] = (float)((p & 255) * a + bb * (1 - a));
                c.Fixed[i] = -1;
            }
            return Quantize(c, opt);
        }

        public static IndexedImage Quantize(Composite src, QuantizeOptions opt)
        {
            int w = src.Width, h = src.Height, n = w * h;
            var enabled = opt.Enabled;
            if (enabled == null || enabled.Length != Palette.Count || Array.IndexOf(enabled, true) < 0)
                throw new ArgumentException("at least one palette colour must be enabled");
            var lut = Lut.For(enabled);
            var fix = src.Fixed;
            var rgb = (float[])src.Rgb.Clone();
            Adjust(rgb, fix, opt);

            var dst = new IndexedImage(w, h);
            for (int i = 0; i < n; i++)
                if (fix[i] >= 0) dst.Index[i] = (byte)fix[i];

            double s = Math.Max(0, Math.Min(1, opt.Strength));
            switch (opt.Dither)
            {
                case DitherKind.FloydSteinberg:
                case DitherKind.Atkinson:
                    Diffuse(rgb, fix, w, h, dst, lut, s, opt.Dither == DitherKind.Atkinson);
                    break;
                case DitherKind.Bayer4:
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int i = y * w + x;
                            if (fix[i] >= 0) continue;
                            double t = ((Bayer4[y & 3, x & 3] + 0.5) / 16.0 - 0.5) * 96 * s;
                            dst.Index[i] = lut.Nearest(rgb[i * 3] + t, rgb[i * 3 + 1] + t, rgb[i * 3 + 2] + t);
                        }
                    break;
                default:
                    for (int i = 0; i < n; i++)
                        if (fix[i] < 0) dst.Index[i] = lut.Nearest(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]);
                    break;
            }
            return dst;
        }

        /// <summary>Brightness / contrast / saturation on the free pixels.</summary>
        static void Adjust(float[] rgb, sbyte[] fix, QuantizeOptions opt)
        {
            double bright = opt.Brightness * 2.55;
            double cf = opt.Contrast >= 0 ? 1 + opt.Contrast / 100.0 * 2 : 1 + opt.Contrast / 100.0;
            double sat = opt.Saturation / 100.0;
            if (bright == 0 && cf == 1 && sat == 1) return;
            for (int i = 0; i < fix.Length; i++)
            {
                if (fix[i] >= 0) continue;
                int p = i * 3;
                double r = rgb[p], g = rgb[p + 1], b = rgb[p + 2];
                if (sat != 1)
                {
                    double l = 0.299 * r + 0.587 * g + 0.114 * b;
                    r = l + (r - l) * sat; g = l + (g - l) * sat; b = l + (b - l) * sat;
                }
                if (cf != 1) { r = (r - 128) * cf + 128; g = (g - 128) * cf + 128; b = (b - 128) * cf + 128; }
                r += bright; g += bright; b += bright;
                rgb[p] = (float)Clamp(r);
                rgb[p + 1] = (float)Clamp(g);
                rgb[p + 2] = (float)Clamp(b);
            }
        }

        static double Clamp(double v) => v < 0 ? 0 : v > 255 ? 255 : v;

        static void Diffuse(float[] rgb, sbyte[] fix, int w, int h, IndexedImage dst, Lut lut, double s, bool atkinson)
        {
            for (int y = 0; y < h; y++)
            {
                bool ltr = (y & 1) == 0; // serpentine
                for (int k = 0; k < w; k++)
                {
                    int x = ltr ? k : w - 1 - k;
                    int i = y * w + x;
                    if (fix[i] >= 0) continue;
                    int dx = ltr ? 1 : -1;
                    int p = i * 3;
                    double r = Clamp(rgb[p]), g = Clamp(rgb[p + 1]), b = Clamp(rgb[p + 2]);
                    int idx = lut.Nearest(r, g, b);
                    dst.Index[i] = (byte)idx;
                    var c = Palette.All[idx];
                    double er = (r - c.R) * s, eg = (g - c.G) * s, eb = (b - c.B) * s;
                    if (atkinson)
                    {
                        er /= 8; eg /= 8; eb /= 8;
                        Add(rgb, fix, w, h, x + dx, y, er, eg, eb, 1);
                        Add(rgb, fix, w, h, x + 2 * dx, y, er, eg, eb, 1);
                        Add(rgb, fix, w, h, x - dx, y + 1, er, eg, eb, 1);
                        Add(rgb, fix, w, h, x, y + 1, er, eg, eb, 1);
                        Add(rgb, fix, w, h, x + dx, y + 1, er, eg, eb, 1);
                        Add(rgb, fix, w, h, x, y + 2, er, eg, eb, 1);
                    }
                    else
                    {
                        Add(rgb, fix, w, h, x + dx, y, er, eg, eb, 7 / 16.0);
                        Add(rgb, fix, w, h, x - dx, y + 1, er, eg, eb, 3 / 16.0);
                        Add(rgb, fix, w, h, x, y + 1, er, eg, eb, 5 / 16.0);
                        Add(rgb, fix, w, h, x + dx, y + 1, er, eg, eb, 1 / 16.0);
                    }
                }
            }
        }

        static void Add(float[] rgb, sbyte[] fix, int w, int h, int x, int y, double er, double eg, double eb, double f)
        {
            if (x < 0 || x >= w || y >= h) return;
            int i = y * w + x;
            if (fix[i] >= 0) return; // fixed pixels take no error (what they would get is dropped)
            int p = i * 3;
            rgb[p] += (float)(er * f);
            rgb[p + 1] += (float)(eg * f);
            rgb[p + 2] += (float)(eb * f);
        }

        /// <summary>Exact perceptual nearest colour (CIELAB ΔE76) among the enabled colours.</summary>
        internal static int NearestExact(double r, double g, double b, bool[] enabled)
        {
            ColorSpace.ToLab(Clamp(r), Clamp(g), Clamp(b), out double L, out double A, out double B);
            int best = -1;
            double bd = double.MaxValue;
            for (int i = 0; i < Palette.Count; i++)
            {
                if (!enabled[i]) continue;
                var lab = PaletteLab[i];
                double dl = L - lab[0], da = A - lab[1], db = B - lab[2];
                double d = dl * dl + da * da + db * db;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        static readonly double[][] PaletteLab = BuildPaletteLab();

        static double[][] BuildPaletteLab()
        {
            var t = new double[Palette.Count][];
            for (int i = 0; i < Palette.Count; i++)
            {
                var c = Palette.All[i];
                ColorSpace.ToLab(c.R, c.G, c.B, out double l, out double a, out double b);
                t[i] = new[] { l, a, b };
            }
            return t;
        }

        /// <summary>
        /// Nearest colour per 64-level RGB cell (262,144 cells, filled lazily), one table per set of enabled colours.
        /// Exact palette colours are matched exactly before the table.
        /// </summary>
        sealed class Lut
        {
            static readonly Dictionary<int, Lut> Cache = new Dictionary<int, Lut>();
            readonly bool[] enabled;
            readonly byte[] cells = new byte[64 * 64 * 64];
            readonly int[] exact = new int[Palette.Count];

            Lut(bool[] enabled)
            {
                this.enabled = (bool[])enabled.Clone();
                for (int i = 0; i < cells.Length; i++) cells[i] = 255;
                for (int i = 0; i < Palette.Count; i++) exact[i] = enabled[i] ? Palette.All[i].Rgb : -1;
            }

            public static Lut For(bool[] enabled)
            {
                int key = 0;
                for (int i = 0; i < enabled.Length; i++) if (enabled[i]) key |= 1 << i;
                lock (Cache)
                {
                    if (!Cache.TryGetValue(key, out var lut)) Cache[key] = lut = new Lut(enabled);
                    return lut;
                }
            }

            public byte Nearest(double r, double g, double b)
            {
                int R = (int)(r < 0 ? 0 : r > 255 ? 255 : r + 0.5);
                int G = (int)(g < 0 ? 0 : g > 255 ? 255 : g + 0.5);
                int B = (int)(b < 0 ? 0 : b > 255 ? 255 : b + 0.5);
                int rgb = R << 16 | G << 8 | B;
                for (int i = 0; i < exact.Length; i++)
                    if (exact[i] == rgb) return (byte)i;
                int cell = (R >> 2) << 12 | (G >> 2) << 6 | (B >> 2);
                byte v = cells[cell];
                if (v == 255)
                {
                    // benign race: concurrent writers store the same value
                    v = (byte)NearestExact((R >> 2) * 4 + 1.5, (G >> 2) * 4 + 1.5, (B >> 2) * 4 + 1.5, enabled);
                    cells[cell] = v;
                }
                return v;
            }
        }
    }
}
