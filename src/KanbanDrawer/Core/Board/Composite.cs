using System;

namespace KanbanDrawer.Core.Board
{
    /// <summary>
    /// The board before quantization: RGB per pixel (0..255 floats) plus, per pixel, a fixed palette index
    /// (frame, background colour, text) or -1 when the pixel is free (picture, stamps) and gets quantized.
    /// </summary>
    public sealed class Composite
    {
        public readonly int Width, Height;
        public readonly float[] Rgb;
        public readonly sbyte[] Fixed;

        public Composite(int width, int height)
        {
            Width = width; Height = height;
            Rgb = new float[width * height * 3];
            Fixed = new sbyte[width * height];
        }

        public Composite Clone()
        {
            var c = new Composite(Width, Height);
            Array.Copy(Rgb, c.Rgb, Rgb.Length);
            Array.Copy(Fixed, c.Fixed, Fixed.Length);
            return c;
        }

        public void SetFixed(int i, int paletteIndex)
        {
            var c = Palette.All[paletteIndex];
            Fixed[i] = (sbyte)paletteIndex;
            Rgb[i * 3] = c.R; Rgb[i * 3 + 1] = c.G; Rgb[i * 3 + 2] = c.B;
        }

        /// <summary>Source-over blend of a colour with alpha 0..1; the pixel becomes free.</summary>
        public void Blend(int i, double r, double g, double b, double a)
        {
            if (a <= 0) return;
            int p = i * 3;
            if (a >= 1)
            {
                Rgb[p] = (float)r; Rgb[p + 1] = (float)g; Rgb[p + 2] = (float)b;
            }
            else
            {
                Rgb[p] = (float)(r * a + Rgb[p] * (1 - a));
                Rgb[p + 1] = (float)(g * a + Rgb[p + 1] * (1 - a));
                Rgb[p + 2] = (float)(b * a + Rgb[p + 2] * (1 - a));
            }
            Fixed[i] = -1;
        }

        /// <summary>RGB view as 0xFFRRGGBB (for "original colours" previews).</summary>
        public Raster ToRaster()
        {
            var r = new Raster(Width, Height);
            for (int i = 0; i < r.Pixels.Length; i++)
            {
                int R = Clamp(Rgb[i * 3]), G = Clamp(Rgb[i * 3 + 1]), B = Clamp(Rgb[i * 3 + 2]);
                r.Pixels[i] = unchecked((int)0xFF000000) | R << 16 | G << 8 | B;
            }
            return r;
        }

        static int Clamp(float v) => v <= 0 ? 0 : v >= 255 ? 255 : (int)(v + 0.5f);
    }
}
