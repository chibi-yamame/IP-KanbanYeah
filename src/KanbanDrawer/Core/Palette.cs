using System;
using System.Collections.Generic;

namespace KanbanDrawer.Core
{
    /// <summary>One of the signboard editor's colour buttons.</summary>
    public sealed class PaletteColor
    {
        public int Index { get; }
        public string Name { get; }
        /// <summary>0x00RRGGBB, exactly as the program sets it (EditCanvas.pixels value).</summary>
        public int Rgb { get; }
        /// <summary>Button column (0-4) and row (0-1) in the tool panel.</summary>
        public int Col { get; }
        public int Row { get; }

        internal PaletteColor(int index, string name, int rgb, int col, int row)
        {
            Index = index; Name = name; Rgb = rgb; Col = col; Row = row;
        }

        public int R => (Rgb >> 16) & 255;
        public int G => (Rgb >> 8) & 255;
        public int B => Rgb & 255;
        public override string ToString() => $"{Name} #{Rgb:X6}";
    }

    /// <summary>
    /// The 10 colours of the signboard editor.
    /// Source: LotSignBoardView.createEditToolPanel (the original client, ipv3m4.jar), color(argb, x, y) with x = 27*col, y = 16*row.
    /// </summary>
    public static class Palette
    {
        public const int Count = 10;

        public static readonly IReadOnlyList<PaletteColor> All = new[]
        {
            new PaletteColor(0, "黒", 0x060606, 0, 0),
            new PaletteColor(1, "灰", 0x939393, 1, 0),
            new PaletteColor(2, "白", 0xFFFFFF, 2, 0),
            new PaletteColor(3, "桃", 0xFFB595, 3, 0),
            new PaletteColor(4, "赤", 0xFF0500, 4, 0),
            new PaletteColor(5, "水", 0x3ACDFF, 0, 1),
            new PaletteColor(6, "青", 0x0205CA, 1, 1),
            new PaletteColor(7, "黄", 0xFFDD00, 2, 1),
            new PaletteColor(8, "緑", 0x00BB09, 3, 1),
            new PaletteColor(9, "茶", 0xC26B15, 4, 1),
        };

        public const int White = 2;

        /// <summary>Palette index of an exact 0xRRGGBB value, or -1.</summary>
        public static int IndexOf(int rgb)
        {
            rgb &= 0xFFFFFF;
            for (int i = 0; i < Count; i++)
                if (All[i].Rgb == rgb) return i;
            return -1;
        }
    }

    /// <summary>sRGB to CIELAB (D65), used for perceptual nearest-colour search.</summary>
    public static class ColorSpace
    {
        static readonly double[] Lin = BuildLin();

        static double[] BuildLin()
        {
            var t = new double[256];
            for (int i = 0; i < 256; i++)
            {
                double c = i / 255.0;
                t[i] = c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return t;
        }

        static double F(double t) => t > 216.0 / 24389 ? Math.Pow(t, 1.0 / 3) : (24389.0 / 27 * t + 16) / 116;

        public static void ToLab(double r, double g, double b, out double L, out double A, out double B)
        {
            double rl = LinOf(r), gl = LinOf(g), bl = LinOf(b);
            double x = (0.4124564 * rl + 0.3575761 * gl + 0.1804375 * bl) / 0.95047;
            double y = 0.2126729 * rl + 0.7151522 * gl + 0.0721750 * bl;
            double z = (0.0193339 * rl + 0.1191920 * gl + 0.9503041 * bl) / 1.08883;
            double fx = F(x), fy = F(y), fz = F(z);
            L = 116 * fy - 16;
            A = 500 * (fx - fy);
            B = 200 * (fy - fz);
        }

        static double LinOf(double v)
        {
            if (v <= 0) return 0;
            if (v >= 255) return 1;
            int i = (int)v;
            double f = v - i;
            return i >= 255 ? Lin[255] : Lin[i] + (Lin[i + 1] - Lin[i]) * f;
        }
    }
}
