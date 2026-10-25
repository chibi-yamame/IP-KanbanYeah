using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using KanbanDrawer.Core;

namespace KanbanDrawer.Win
{
    /// <summary>GDI+ side of picture handling: decode, fit to the board (high quality resampling), convert.</summary>
    public static class ImageLoader
    {
        /// <summary>Loads a file into memory (the file is not kept locked).</summary>
        public static Bitmap Load(string path)
        {
            using (var img = Image.FromFile(path))
                return new Bitmap(img);
        }

        /// <summary>The picture placed on a 320x240 board; uncovered parts are transparent.</summary>
        public static Raster Fit(Bitmap src, FitMode mode)
        {
            var r = ImageFit.Compute(src.Width, src.Height, BoardGeometry.Width, BoardGeometry.Height, mode);
            using (var dst = new Bitmap(BoardGeometry.Width, BoardGeometry.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(dst))
                using (var attr = new ImageAttributes())
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingMode = CompositingMode.SourceCopy;
                    attr.SetWrapMode(WrapMode.TileFlipXY); // no dark fringe at the edges
                    g.DrawImage(src, new Rectangle(r.DstX, r.DstY, r.DstW, r.DstH),
                        (float)r.SrcX, (float)r.SrcY, (float)r.SrcW, (float)r.SrcH, GraphicsUnit.Pixel, attr);
                }
                return ToRaster(dst);
            }
        }

        public static Raster ToRaster(Bitmap bmp)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var px = new int[bmp.Width * bmp.Height];
                for (int y = 0; y < bmp.Height; y++)
                    Marshal.Copy(data.Scan0 + y * data.Stride, px, y * bmp.Width, bmp.Width);
                return new Raster(bmp.Width, bmp.Height, px);
            }
            finally { bmp.UnlockBits(data); }
        }

        public static Bitmap ToBitmap(Raster r)
        {
            var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, r.Width, r.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < r.Height; y++)
                    Marshal.Copy(r.Pixels, y * r.Width, data.Scan0 + y * data.Stride, r.Width);
            }
            finally { bmp.UnlockBits(data); }
            return bmp;
        }
    }
}
