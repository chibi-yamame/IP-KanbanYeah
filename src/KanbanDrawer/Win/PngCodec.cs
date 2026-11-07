using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using KanbanDrawer.Core;
using KanbanDrawer.Core.Board;

namespace KanbanDrawer.Win
{
    /// <summary>
    /// PNG for .kanban files (GDI+): 32-bit ARGB, alpha kept. The RGB of fully transparent pixels is not guaranteed
    /// (it does not affect the composite).
    /// </summary>
    public sealed class PngCodec : IImageCodec
    {
        public byte[] EncodePng(Raster image)
        {
            using (var bmp = ImageLoader.ToBitmap(image))
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        public Raster DecodePng(byte[] png)
        {
            // GDI+ keeps reading the stream while the bitmap lives: copy to 32bppArgb before the stream is closed
            using (var ms = new MemoryStream(png))
            using (var src = new Bitmap(ms))
            using (var copy = src.Clone(new Rectangle(0, 0, src.Width, src.Height), PixelFormat.Format32bppArgb))
                return ImageLoader.ToRaster(copy);
        }
    }
}
