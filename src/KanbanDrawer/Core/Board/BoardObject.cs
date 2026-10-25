using System;

namespace KanbanDrawer.Core.Board
{
    /// <summary>What objects may need while rendering (implemented by the Windows side, faked in tests).</summary>
    public interface IRenderServices
    {
        ITextRasterizer Text { get; }
    }

    /// <summary>
    /// Something placed on the board (stamp, text). Geometry is local around (0,0), size LocalW x LocalH at Scale 1;
    /// page = (Cx,Cy) + R(Rot) * Scale * local, Rot in radians clockwise (y points down). Same model as the sibling
    /// project's ipx.android.board.BoardObject, with uniform scale only.
    /// </summary>
    public abstract class BoardObject
    {
        public double Cx, Cy;
        public double Scale = 1;
        public double Rot;

        /// <summary>False: no rotate knob (the object stays upright).</summary>
        public virtual bool CanRotate => true;
        /// <summary>False: no corner handles (the size is set otherwise).</summary>
        public virtual bool CanScale => true;

        public abstract double LocalW { get; }
        public abstract double LocalH { get; }

        /// <summary>Draws the object into the composite (page 320x240).</summary>
        public abstract void Render(Composite target, IRenderServices services);

        public abstract BoardObject Clone();

        public const double MinSize = 4, MaxSize = 2000;

        public void PageToLocal(double x, double y, out double lx, out double ly)
        {
            double dx = x - Cx, dy = y - Cy;
            double c = Math.Cos(Rot), s = Math.Sin(Rot);
            lx = (dx * c + dy * s) / Scale;
            ly = (-dx * s + dy * c) / Scale;
        }

        public void LocalToPage(double lx, double ly, out double x, out double y)
        {
            double c = Math.Cos(Rot), s = Math.Sin(Rot);
            x = Cx + (lx * c - ly * s) * Scale;
            y = Cy + (lx * s + ly * c) * Scale;
        }

        /// <summary>Corners TL, TR, BR, BL on the page (x0,y0,...,x3,y3).</summary>
        public double[] Corners()
        {
            double hw = LocalW / 2, hh = LocalH / 2;
            var r = new double[8];
            LocalToPage(-hw, -hh, out r[0], out r[1]);
            LocalToPage(hw, -hh, out r[2], out r[3]);
            LocalToPage(hw, hh, out r[4], out r[5]);
            LocalToPage(-hw, hh, out r[6], out r[7]);
            return r;
        }

        /// <summary>Page bounding box of the transformed object, clipped to the page: l, t, r, b (exclusive).</summary>
        public void PageBounds(int pageW, int pageH, out int l, out int t, out int r, out int b)
        {
            var c = Corners();
            double minX = Math.Min(Math.Min(c[0], c[2]), Math.Min(c[4], c[6]));
            double maxX = Math.Max(Math.Max(c[0], c[2]), Math.Max(c[4], c[6]));
            double minY = Math.Min(Math.Min(c[1], c[3]), Math.Min(c[5], c[7]));
            double maxY = Math.Max(Math.Max(c[1], c[3]), Math.Max(c[5], c[7]));
            l = Math.Max(0, (int)Math.Floor(minX) - 1);
            t = Math.Max(0, (int)Math.Floor(minY) - 1);
            r = Math.Min(pageW, (int)Math.Ceiling(maxX) + 2);
            b = Math.Min(pageH, (int)Math.Ceiling(maxY) + 2);
        }

        /// <summary>Inside the transformed box (with a margin in page pixels).</summary>
        public bool InsideBox(double x, double y, double margin = 0)
        {
            PageToLocal(x, y, out double lx, out double ly);
            double m = margin / Scale;
            return Math.Abs(lx) <= LocalW / 2 + m && Math.Abs(ly) <= LocalH / 2 + m;
        }

        /// <summary>Whether a click at the page point picks this object (default: its box).</summary>
        public virtual bool Hit(double x, double y) => InsideBox(x, y);

        /// <summary>Larger side on the page, used to clamp scaling.</summary>
        public double PageSize => Math.Max(LocalW, LocalH) * Scale;

        public void ClampScale()
        {
            double size = Math.Max(LocalW, LocalH);
            if (size <= 0) return;
            Scale = Math.Max(MinSize / size, Math.Min(MaxSize / size, Scale));
        }

        protected void CopyTransformTo(BoardObject o)
        {
            o.Cx = Cx; o.Cy = Cy; o.Scale = Scale; o.Rot = Rot;
        }
    }
}
