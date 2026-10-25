using System;

namespace KanbanDrawer.Core.Board
{
    public enum Handle { None, Body, Rotate, ScaleTL, ScaleTR, ScaleBR, ScaleBL }

    /// <summary>
    /// Selection handles: the four corners scale (uniformly, around the centre), a knob above the top edge rotates,
    /// the body moves. Page pixels throughout (the editor shows the board 1:1). Modelled on the sibling project's
    /// ipx.android.board.Handles / Drag.
    /// </summary>
    public static class Handles
    {
        public const double RotateOffset = 14;
        public const double Tolerance = 5;

        /// <summary>Corners TL, TR, BR, BL, the rotate knob, then the top edge midpoint (for its stem): 12 numbers.</summary>
        public static double[] Points(BoardObject o)
        {
            var c = o.Corners();
            double mx = (c[0] + c[2]) / 2, my = (c[1] + c[3]) / 2;
            double ux = Math.Sin(o.Rot), uy = -Math.Cos(o.Rot); // object's "up" on the page
            double kx = mx + ux * RotateOffset, ky = my + uy * RotateOffset;
            // keep the knob reachable on the page: put it below the bottom edge when it would leave the board
            if (kx < 0 || ky < 0 || kx >= BoardDoc.W || ky >= BoardDoc.H)
            {
                double bx = (c[4] + c[6]) / 2, by = (c[5] + c[7]) / 2;
                kx = bx - ux * RotateOffset; ky = by - uy * RotateOffset;
                mx = bx; my = by;
            }
            return new[] { c[0], c[1], c[2], c[3], c[4], c[5], c[6], c[7], kx, ky, mx, my };
        }

        public static Handle Hit(BoardObject o, double x, double y)
        {
            if (o == null) return Handle.None;
            var p = Points(o);
            if (o.CanRotate && Dist(x, y, p[8], p[9]) <= Tolerance) return Handle.Rotate;
            int best = -1;
            double bd = Tolerance;
            for (int i = 0; i < 4 && o.CanScale; i++)
            {
                double d = Dist(x, y, p[i * 2], p[i * 2 + 1]);
                if (d <= bd) { bd = d; best = i; }
            }
            if (best >= 0) return Handle.ScaleTL + best;
            return o.InsideBox(x, y, 1) ? Handle.Body : Handle.None;
        }

        static double Dist(double x0, double y0, double x1, double y1) => Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
    }

    /// <summary>One drag of a handle: remembers the start and applies the pointer's movement to the object.</summary>
    public sealed class Drag
    {
        readonly BoardObject obj;
        readonly Handle handle;
        readonly double startX, startY, cx0, cy0, scale0, rot0, startDist, startAngle;

        public Drag(BoardObject obj, Handle handle, double x, double y)
        {
            this.obj = obj; this.handle = handle;
            startX = x; startY = y;
            cx0 = obj.Cx; cy0 = obj.Cy; scale0 = obj.Scale; rot0 = obj.Rot;
            startDist = Math.Max(1, Math.Sqrt((x - cx0) * (x - cx0) + (y - cy0) * (y - cy0)));
            startAngle = Math.Atan2(y - cy0, x - cx0);
        }

        public Handle Handle => handle;

        /// <param name="snap">rotate in 15 degree steps</param>
        public void Update(double x, double y, bool snap)
        {
            switch (handle)
            {
                case Handle.Body:
                    obj.Cx = cx0 + (x - startX);
                    obj.Cy = cy0 + (y - startY);
                    break;
                case Handle.Rotate:
                {
                    double a = rot0 + Math.Atan2(y - cy0, x - cx0) - startAngle;
                    if (snap) a = Math.Round(a / (Math.PI / 12)) * (Math.PI / 12);
                    obj.Rot = Normalize(a);
                    break;
                }
                case Handle.None:
                    break;
                default: // corners
                {
                    double d = Math.Sqrt((x - cx0) * (x - cx0) + (y - cy0) * (y - cy0));
                    obj.Scale = scale0 * d / startDist;
                    obj.ClampScale();
                    break;
                }
            }
        }

        public static double Normalize(double a)
        {
            while (a <= -Math.PI) a += 2 * Math.PI;
            while (a > Math.PI) a -= 2 * Math.PI;
            return a;
        }
    }
}
