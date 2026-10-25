using System;

namespace KanbanDrawer.Core.Board
{
    /// <summary>A registered picture placed on the board (own pixel copy, so deleting it from the library keeps the board).</summary>
    public sealed class StampObject : BoardObject
    {
        /// <summary>Library id it came from (informational).</summary>
        public string Id;
        /// <summary>ARGB pixels, shared between clones (never changed).</summary>
        public Raster Picture;

        public StampObject(string id, Raster picture)
        {
            Id = id;
            Picture = picture;
        }

        public override double LocalW => Picture.Width;
        public override double LocalH => Picture.Height;

        public override BoardObject Clone()
        {
            var o = new StampObject(Id, Picture) { shrunk = shrunk };
            CopyTransformTo(o);
            return o;
        }

        /// <summary>Opaque enough under the page point (transparent parts do not pick the stamp).</summary>
        public override bool Hit(double x, double y)
        {
            if (!InsideBox(x, y)) return false;
            PageToLocal(x, y, out double lx, out double ly);
            int sx = (int)Math.Floor(lx + Picture.Width / 2.0), sy = (int)Math.Floor(ly + Picture.Height / 2.0);
            if (sx < 0 || sy < 0 || sx >= Picture.Width || sy >= Picture.Height) return false;
            return ((Picture[sx, sy] >> 24) & 255) >= 32;
        }

        // shrunk copy of the picture for the current size (area average), shared by clones of this object
        Raster shrunk;

        public override void Render(Composite target, IRenderServices services)
        {
            PageBounds(target.Width, target.Height, out int l, out int t, out int r, out int b);
            if (r <= l || b <= t) return;
            // When shrinking, sample a copy shrunk by area averaging to about the page size (details average instead of
            // aliasing, and one bilinear sample per pixel stays cheap enough for live dragging).
            var src = Picture;
            if (Scale < 1)
            {
                int tw = Math.Max(1, (int)Math.Round(Picture.Width * Scale));
                int th = Math.Max(1, (int)Math.Round(Picture.Height * Scale));
                if (shrunk == null || shrunk.Width != tw || shrunk.Height != th) shrunk = StampPrep.Scale(Picture, tw, th);
                src = shrunk;
            }
            double fx = (double)src.Width / Picture.Width, fy = (double)src.Height / Picture.Height;
            double hw = Picture.Width / 2.0, hh = Picture.Height / 2.0;
            // inverse transform, stepped per pixel: local = R(-rot) * (page - centre) / scale
            double c = Math.Cos(Rot) / Scale, s = Math.Sin(Rot) / Scale;
            for (int y = t; y < b; y++)
            {
                double dy = y + 0.5 - Cy;
                for (int x = l; x < r; x++)
                {
                    double dx = x + 0.5 - Cx;
                    double lx = dx * c + dy * s, ly = -dx * s + dy * c;
                    double ar = 0, ag = 0, ab = 0, aa = 0;
                    Sample(src, (lx + hw) * fx - 0.5, (ly + hh) * fy - 0.5, ref ar, ref ag, ref ab, ref aa);
                    if (aa <= 0) continue;
                    // premultiplied: colour = sum(c*a) / sum(a)
                    target.Blend(y * target.Width + x, ar / aa, ag / aa, ab / aa, aa);
                }
            }
        }

        /// <summary>Bilinear sample (premultiplied accumulation); outside the picture counts as transparent.</summary>
        static void Sample(Raster Picture, double fx, double fy, ref double r, ref double g, ref double b, ref double a)
        {
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
            double tx = fx - x0, ty = fy - y0;
            for (int j = 0; j < 2; j++)
                for (int i = 0; i < 2; i++)
                {
                    double w = (i == 0 ? 1 - tx : tx) * (j == 0 ? 1 - ty : ty);
                    if (w <= 0) continue;
                    int px = x0 + i, py = y0 + j;
                    if (px < 0 || py < 0 || px >= Picture.Width || py >= Picture.Height) continue;
                    int c = Picture[px, py];
                    double ca = ((c >> 24) & 255) / 255.0 * w;
                    r += ((c >> 16) & 255) * ca;
                    g += ((c >> 8) & 255) * ca;
                    b += (c & 255) * ca;
                    a += ca;
                }
        }
    }
}
