using System;
using System.Collections.Generic;
using KanbanDrawer.Core;

namespace KanbanDrawer.Tests
{
    /// <summary>
    /// A simulated screen with the signboard editor, behaving like the program: buttons are bevelled toggles
    /// (ColorButton / BrushSizeButton), the 1px pen plots on press and draws a line from the press point on release
    /// (PenTool), CLR fills with the current colour. Events can be dropped to exercise the read-back pass.
    /// </summary>
    internal sealed class FakeEditor : IBoardIO
    {
        public readonly Raster Screen;
        public readonly Pt Origin;
        public int Color = 0x060606;
        public int Brush = 4;
        public int SelectedColor = 0;
        /// <summary>Drop every n-th button release on the canvas (0 = never).</summary>
        public int DropEveryNthRelease;
        public Func<string> Interrupt = () => null;
        public int Releases, Captures;
        public readonly List<string> Events = new List<string>();

        int cx, cy, pressX, pressY;
        bool pressed;

        public FakeEditor(int width = 420, int height = 400, int ox = 40, int oy = 30)
        {
            Screen = new Raster(width, height);
            for (int i = 0; i < Screen.Pixels.Length; i++) Screen.Pixels[i] = unchecked((int)0xFF1C3B3F);
            Origin = new Pt(ox, oy);
            for (int y = 0; y < BoardGeometry.Height; y++)
                for (int x = 0; x < BoardGeometry.Width; x++)
                    Screen[ox + x, oy + y] = unchecked((int)0xFFFFFFFF);
            PaintButtons();
        }

        public int[] Canvas()
        {
            var px = new int[BoardGeometry.Width * BoardGeometry.Height];
            for (int y = 0; y < BoardGeometry.Height; y++)
                for (int x = 0; x < BoardGeometry.Width; x++)
                    px[y * BoardGeometry.Width + x] = Screen[Origin.X + x, Origin.Y + y] & 0xFFFFFF;
            return px;
        }

        public void FillCanvas(Func<int, int, int> rgb)
        {
            for (int y = 0; y < BoardGeometry.Height; y++)
                for (int x = 0; x < BoardGeometry.Width; x++)
                    Screen[Origin.X + x, Origin.Y + y] = unchecked((int)0xFF000000) | rgb(x, y);
        }

        /// <summary>Another window covers the editor: captures show black.</summary>
        public bool Covered;
        /// <summary>The canvas on screen stops changing (the program draws, the display does not follow).</summary>
        public int[] FrozenCanvas;

        public Raster Capture(int x, int y, int w, int h)
        {
            Captures++;
            var r = new Raster(w, h);
            if (Covered) return r;
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    int sx = x + i, sy = y + j;
                    r[i, j] = sx >= 0 && sy >= 0 && sx < Screen.Width && sy < Screen.Height ? Screen[sx, sy] : 0;
                    if (FrozenCanvas != null && OnCanvas(sx, sy))
                        r[i, j] = unchecked((int)0xFF000000) | FrozenCanvas[(sy - Origin.Y) * BoardGeometry.Width + (sx - Origin.X)];
                }
            return r;
        }

        public void MoveTo(int x, int y) { cx = x; cy = y; }

        public void Down()
        {
            pressed = true;
            pressX = cx; pressY = cy;
            if (OnCanvas(cx, cy)) Plot(cx - Origin.X, cy - Origin.Y);
        }

        public void Up()
        {
            if (!pressed) return;
            pressed = false;
            if (OnCanvas(pressX, pressY))
            {
                Releases++;
                if (DropEveryNthRelease > 0 && Releases % DropEveryNthRelease == 0) return;
                Line(pressX - Origin.X, pressY - Origin.Y, cx - Origin.X, cy - Origin.Y);
                return;
            }
            Click(pressX - Origin.X, pressY - Origin.Y);
        }

        public void Sleep(int ms) { }

        public string CheckInterrupt() => Interrupt();

        bool OnCanvas(int x, int y) =>
            x >= Origin.X && y >= Origin.Y && x < Origin.X + BoardGeometry.Width && y < Origin.Y + BoardGeometry.Height;

        void Click(int rx, int ry)
        {
            foreach (var c in Palette.All)
            {
                var b = BoardGeometry.ColorButton(c);
                if (rx >= b.X && ry >= b.Y && rx < b.X + BoardGeometry.ColorButtonW && ry < b.Y + BoardGeometry.ColorButtonH)
                {
                    Color = c.Rgb; SelectedColor = c.Index; Events.Add("color " + c.Name);
                    PaintButtons();
                    return;
                }
            }
            if (In(rx, ry, BoardGeometry.Pen1Button)) { Brush = 1; Events.Add("pen1"); PaintButtons(); return; }
            if (In(rx, ry, BoardGeometry.ClearButton))
            {
                Events.Add("clear");
                FillCanvas((x, y) => Color);
            }
        }

        static bool In(int x, int y, Pt b) => x >= b.X && y >= b.Y && x < b.X + 32 && y < b.Y + 32;

        void Plot(int x, int y)
        {
            if (Brush != 1) throw new InvalidOperationException("drawing with a pen other than 1px");
            if (x >= 0 && y >= 0 && x < BoardGeometry.Width && y < BoardGeometry.Height)
                Screen[Origin.X + x, Origin.Y + y] = unchecked((int)0xFF000000) | Color;
        }

        void Line(int x0, int y0, int x1, int y1)
        {
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
            while (true)
            {
                Plot(x0, y0);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        void PaintButtons()
        {
            foreach (var c in Palette.All)
                Button(BoardGeometry.ColorButton(c), BoardGeometry.ColorButtonW, BoardGeometry.ColorButtonH, c.Rgb, c.Index == SelectedColor);
            Button(BoardGeometry.Pen1Button, 32, 32, 0xBCBCBC, Brush == 1);
            Button(BoardGeometry.Pen1Button.Offset(32, 0), 32, 32, 0xBCBCBC, Brush == 4);
            Button(BoardGeometry.ClearButton, 32, 32, 0xBCBCBC, false);
        }

        void Button(Pt rel, int w, int h, int face, bool down)
        {
            int ox = Origin.X + rel.X, oy = Origin.Y + rel.Y;
            int edge = down ? 3 : 2;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int v;
                    if (x >= edge && y >= edge && x < w - edge && y < h - edge) v = face;
                    else if (down) v = x < 3 || y < 3 ? 0x808080 : 0xFFFFFF;
                    else v = x < 2 || y < 2 ? 0xFFFFFF : 0x808080;
                    Screen[ox + x, oy + y] = unchecked((int)0xFF000000) | v;
                }
        }
    }
}
