using System;
using System.Collections.Generic;

namespace KanbanDrawer.Core
{
    /// <summary>
    /// Finds the signboard editor in a screen capture by its 10 colour buttons (independent of what the canvas shows).
    /// A ColorButton (ColorButton.paintComponent in the original client) fills its colour at (2,2,w-4,h-4) when up and (3,3,w-6,h-6)
    /// when pressed, with white / gray bevels: points inside (3..23, 3..12) always show the colour, (1,1) is white when
    /// up and gray (128) when pressed. The pen buttons use the same bevels.
    /// </summary>
    public static class BoardLocator
    {
        static readonly Pt[] Samples = { new Pt(4, 4), new Pt(13, 8), new Pt(22, 11), new Pt(4, 11), new Pt(22, 4) };

        /// <summary>
        /// Bevel pixels (white or gray in both states). One pixel further in is the colour on an up button, so a
        /// position off by one fails on the up buttons (at most one button is down).
        /// </summary>
        static readonly Pt[] Bevels = { new Pt(1, 1), new Pt(1, 8), new Pt(25, 8), new Pt(13, 1), new Pt(13, 14), new Pt(25, 14) };

        /// <summary>Canvas origins (in raster coordinates) of every editor found.</summary>
        public static List<Pt> Find(Raster screen, int tolerance = 0)
        {
            var found = new List<Pt>();
            var black = Palette.All[0];
            Pt b0 = BoardGeometry.ColorButton(black).Offset(Samples[1].X, Samples[1].Y);
            int w = screen.Width, h = screen.Height;
            var px = screen.Pixels;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    if (!Near(px[row + x], black.Rgb, tolerance)) continue;
                    var origin = new Pt(x - b0.X, y - b0.Y);
                    if (Matches(screen, origin, tolerance)) found.Add(origin);
                }
            }
            return found;
        }

        /// <summary>True when all colour buttons are where an editor with this canvas origin would have them.</summary>
        public static bool Matches(Raster screen, Pt origin, int tolerance = 0)
        {
            BoardGeometry.Extent(out int l, out int t, out int r, out int b);
            if (origin.X + l < 0 || origin.Y + t < 0 || origin.X + r > screen.Width || origin.Y + b > screen.Height)
                return false;
            foreach (var c in Palette.All)
            {
                var bt = BoardGeometry.ColorButton(c);
                foreach (var s in Samples)
                    if (!Near(screen[origin.X + bt.X + s.X, origin.Y + bt.Y + s.Y], c.Rgb, tolerance)) return false;
                foreach (var s in Bevels)
                {
                    int v = screen[origin.X + bt.X + s.X, origin.Y + bt.Y + s.Y];
                    if (!Near(v, 0xFFFFFF, tolerance) && !Near(v, 0x808080, tolerance)) return false;
                }
            }
            return true;
        }

        /// <summary>Pressed state of a bevelled button at <paramref name="buttonTopLeft"/> (absolute): true / false / null (unknown).</summary>
        public static bool? IsPressed(Raster screen, Pt buttonTopLeft)
        {
            int x = buttonTopLeft.X + 1, y = buttonTopLeft.Y + 1;
            if (x < 0 || y < 0 || x >= screen.Width || y >= screen.Height) return null;
            int v = screen[x, y] & 0xFFFFFF;
            if (v == 0xFFFFFF) return false;
            if (v == 0x808080) return true;
            return null;
        }

        public static bool? IsColorSelected(Raster screen, Pt origin, PaletteColor c)
        {
            var bt = BoardGeometry.ColorButton(c);
            return IsPressed(screen, origin.Offset(bt.X, bt.Y));
        }

        public static bool? IsPen1Selected(Raster screen, Pt origin)
        {
            var bt = BoardGeometry.Pen1Button;
            return IsPressed(screen, origin.Offset(bt.X, bt.Y));
        }

        static bool Near(int argb, int rgb, int tol)
        {
            if (tol == 0) return (argb & 0xFFFFFF) == rgb;
            return Math.Abs(((argb >> 16) & 255) - ((rgb >> 16) & 255)) <= tol
                && Math.Abs(((argb >> 8) & 255) - ((rgb >> 8) & 255)) <= tol
                && Math.Abs((argb & 255) - (rgb & 255)) <= tol;
        }
    }
}
