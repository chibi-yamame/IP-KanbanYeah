namespace KanbanDrawer.Core
{
    public readonly struct Pt
    {
        public readonly int X, Y;
        public Pt(int x, int y) { X = x; Y = y; }
        public Pt Offset(int dx, int dy) => new Pt(X + dx, Y + dy);
        public override string ToString() => $"({X},{Y})";
    }

    /// <summary>
    /// Positions of the editor's controls relative to the canvas' top-left pixel. Everything sits in the edit panel of
    /// LotSignBoardView (the original client, ipv3m4.jar): CANVAS_BOUNDS = (26,72,320,240); command panel VIEW_LIST_COMMANDS_BOUNDS = (0,354);
    /// colour buttons at (5+27*col, 8+16*row) size 27x16; pen buttons 1/4/8 px at (145|177|209, 8) size 32x32;
    /// text (241,8); CLR at (273,8); 保存 (310,8); キャンセル (310,24). Checked against a screenshot: canvas at (38,112),
    /// black button at (17,402).
    /// </summary>
    public static class BoardGeometry
    {
        public const int Width = 320;
        public const int Height = 240;

        const int PanelDx = -26, PanelDy = 354 - 72;

        public const int ColorButtonW = 27, ColorButtonH = 16;

        /// <summary>Top-left of a colour button, relative to the canvas origin.</summary>
        public static Pt ColorButton(int col, int row) => new Pt(PanelDx + 5 + 27 * col, PanelDy + 8 + 16 * row);

        public static Pt ColorButton(PaletteColor c) => ColorButton(c.Col, c.Row);

        public static Pt ColorButtonCenter(PaletteColor c) => ColorButton(c).Offset(13, 8);

        public const int ToolButtonSize = 32;

        public static Pt Pen1Button => new Pt(PanelDx + 145, PanelDy + 8);
        public static Pt Pen1Center => Pen1Button.Offset(16, 16);
        public static Pt Pen4Button => new Pt(PanelDx + 177, PanelDy + 8);
        public static Pt Pen4Center => Pen4Button.Offset(16, 16);
        public static Pt ClearButton => new Pt(PanelDx + 273, PanelDy + 8);
        public static Pt ClearCenter => ClearButton.Offset(16, 16);

        /// <summary>Bounding box (relative) of everything the tool reads or clicks: canvas + tool panel up to CLR.</summary>
        public static void Extent(out int left, out int top, out int right, out int bottom)
        {
            left = PanelDx;
            top = 0;
            right = Width;
            bottom = PanelDy + 8 + ToolButtonSize;
        }
    }
}
