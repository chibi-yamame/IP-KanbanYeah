using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using KanbanDrawer.Core.Board;

namespace KanbanDrawer.Win
{
    /// <summary>
    /// Text as GDI+ glyph outlines (GraphicsPath.AddString), filled without anti-aliasing so every pixel is either
    /// the letter colour, the outline colour or untouched. Fonts are whatever is installed.
    /// </summary>
    public sealed class GdiTextRasterizer : ITextRasterizer, IRenderServices
    {
        public ITextRasterizer Text => this;

        /// <summary>Installed font families (names as GDI+ reports them, Japanese names where the font has them).</summary>
        public static string[] InstalledFonts()
        {
            using (var fonts = new InstalledFontCollection())
                return fonts.Families.Select(f => f.Name).ToArray();
        }

        public static string DefaultFont()
        {
            var fonts = InstalledFonts();
            foreach (var name in new[] { "Yu Gothic UI", "游ゴシック", "Meiryo UI", "メイリオ", "MS UI Gothic", "MS ゴシック" })
                if (fonts.Contains(name)) return name;
            return FontFamily.GenericSansSerif.Name;
        }

        public void Measure(TextObject t, out double width, out double height)
        {
            if (t.Official)
            {
                var bmp = OfficialText.Render(t.Text, (int)Math.Round(t.Size));
                int ow = t.Outline >= 0 ? t.OutlineWidth : 0;
                width = bmp.Width + 2 * ow;
                height = bmp.Height + 2 * ow;
                return;
            }
            using (var path = LocalPath(t, out RectangleF b))
            {
                double pad = t.Outline >= 0 ? t.OutlineWidth : 0;
                width = b.Width + 2 * pad + 1;
                height = b.Height + 2 * pad + 1;
            }
        }

        public void Rasterize(TextObject t, byte[] mask, int pageW, int pageH)
        {
            if (t.Official) { RasterizeOfficial(t, mask, pageW, pageH); return; }
            using (var path = LocalPath(t, out RectangleF b))
            using (var m = new Matrix())
            {
                // local box centred on (0,0) → scale → rotate → place
                m.Translate(-(b.X + b.Width / 2), -(b.Y + b.Height / 2), MatrixOrder.Append);
                m.Scale((float)t.Scale, (float)t.Scale, MatrixOrder.Append);
                m.Rotate((float)(t.Rot * 180 / Math.PI), MatrixOrder.Append);
                m.Translate((float)t.Cx, (float)t.Cy, MatrixOrder.Append);
                path.Transform(m);
                using (var bmp = new Bitmap(pageW, pageH, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.None;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.Clear(Color.Black);
                        if (t.Outline >= 0 && t.OutlineWidth > 0)
                        {
                            using (var wide = (GraphicsPath)path.Clone())
                            using (var pen = new Pen(Color.White, (float)Math.Max(1, 2 * t.OutlineWidth * t.Scale)) { LineJoin = LineJoin.Round })
                            {
                                wide.Widen(pen);
                                g.FillPath(Brushes.Blue, wide);
                                g.FillPath(Brushes.Blue, path); // widening leaves the inside of letters open
                            }
                        }
                        g.FillPath(Brushes.Red, path);
                    }
                    var data = bmp.LockBits(new Rectangle(0, 0, pageW, pageH), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        var row = new int[pageW];
                        for (int y = 0; y < pageH; y++)
                        {
                            Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, pageW);
                            for (int x = 0; x < pageW; x++)
                            {
                                int c = row[x] & 0xFFFFFF;
                                if (c == 0xFF0000) mask[y * pageW + x] = 1;
                                else if (c == 0x0000FF) mask[y * pageW + x] = 2;
                            }
                        }
                    }
                    finally { bmp.UnlockBits(data); }
                }
            }
        }

        /// <summary>
        /// 公式と同じ: the program's text bitmap on whole pixels (box centred on the object), outline = the letters grown by
        /// OutlineWidth pixels in every direction (square).
        /// </summary>
        static void RasterizeOfficial(TextObject t, byte[] mask, int pageW, int pageH)
        {
            var bmp = OfficialText.Render(t.Text, (int)Math.Round(t.Size));
            int ow = t.Outline >= 0 ? t.OutlineWidth : 0;
            int left = (int)Math.Round(t.Cx - t.LocalW / 2) + ow, top = (int)Math.Round(t.Cy - t.LocalH / 2) + ow;
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                {
                    if (bmp.Bits[y * bmp.Width + x] == 0) continue;
                    int px = left + x, py = top + y;
                    for (int dy = -ow; dy <= ow; dy++)
                        for (int dx = -ow; dx <= ow; dx++)
                        {
                            int qx = px + dx, qy = py + dy;
                            if (qx < 0 || qy < 0 || qx >= pageW || qy >= pageH) continue;
                            int i = qy * pageW + qx;
                            if (dx == 0 && dy == 0) mask[i] = 1;
                            else if (mask[i] == 0) mask[i] = 2;
                        }
                }
        }

        /// <summary>The text's outline path at Scale 1 (unplaced) and its bounds.</summary>
        static GraphicsPath LocalPath(TextObject t, out RectangleF bounds)
        {
            var path = new GraphicsPath();
            var family = Family(t.Font);
            var style = t.Bold && family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
            if (!family.IsStyleAvailable(style)) style = family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
            using (var fmt = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                fmt.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                path.AddString(string.IsNullOrEmpty(t.Text) ? " " : t.Text, family, (int)style, (float)t.Size, PointF.Empty, fmt);
            }
            bounds = path.GetBounds();
            if (bounds.Width <= 0 || bounds.Height <= 0) bounds = new RectangleF(0, 0, (float)t.Size / 2, (float)t.Size);
            return path;
        }

        static FontFamily Family(string name)
        {
            try { return string.IsNullOrEmpty(name) ? FontFamily.GenericSansSerif : new FontFamily(name); }
            catch (ArgumentException) { return FontFamily.GenericSansSerif; }
        }
    }
}
