using System;

namespace KanbanDrawer.Core
{
    /// <summary>32-bit ARGB pixels (0xAARRGGBB), row major.</summary>
    public sealed class Raster
    {
        public int Width { get; }
        public int Height { get; }
        public int[] Pixels { get; }

        public Raster(int width, int height) : this(width, height, new int[width * height]) { }

        public Raster(int width, int height, int[] pixels)
        {
            if (pixels.Length != width * height) throw new ArgumentException("size mismatch");
            Width = width; Height = height; Pixels = pixels;
        }

        public int this[int x, int y]
        {
            get => Pixels[y * Width + x];
            set => Pixels[y * Width + x] = value;
        }

        public Raster Clone() => new Raster(Width, Height, (int[])Pixels.Clone());

        /// <summary>Copies a sub rectangle (must lie inside).</summary>
        public Raster Crop(int x, int y, int w, int h)
        {
            var r = new Raster(w, h);
            for (int j = 0; j < h; j++)
                Array.Copy(Pixels, (y + j) * Width + x, r.Pixels, j * w, w);
            return r;
        }
    }

    /// <summary>Palette indices (0..Palette.Count-1), row major.</summary>
    public sealed class IndexedImage
    {
        public int Width { get; }
        public int Height { get; }
        public byte[] Index { get; }

        public IndexedImage(int width, int height) : this(width, height, new byte[width * height]) { }

        public IndexedImage(int width, int height, byte[] index)
        {
            if (index.Length != width * height) throw new ArgumentException("size mismatch");
            Width = width; Height = height; Index = index;
        }

        public byte this[int x, int y]
        {
            get => Index[y * Width + x];
            set => Index[y * Width + x] = value;
        }

        public Raster ToRaster()
        {
            var r = new Raster(Width, Height);
            for (int i = 0; i < Index.Length; i++)
                r.Pixels[i] = unchecked((int)0xFF000000) | Palette.All[Index[i]].Rgb;
            return r;
        }

        /// <summary>Pixel count per palette index.</summary>
        public int[] Histogram()
        {
            var h = new int[Palette.Count];
            foreach (var b in Index) h[b]++;
            return h;
        }
    }
}
