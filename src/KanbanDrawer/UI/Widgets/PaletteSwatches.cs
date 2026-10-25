using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using KanbanDrawer.Core;

namespace KanbanDrawer.UI.Widgets
{
    /// <summary>Pick one of the 10 palette colours (optionally "なし" first). Selected = -1 means なし.</summary>
    internal sealed class PaletteSwatches : Control
    {
        const int Cell = 22, Gap = 4;
        readonly bool allowNone;
        int selected;

        public event EventHandler SelectedChanged;

        public PaletteSwatches(bool allowNone, int selected)
        {
            this.allowNone = allowNone;
            this.selected = selected;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Size = new Size(Count * (Cell + Gap), Cell + 4);
        }

        int Count => Palette.Count + (allowNone ? 1 : 0);

        public int Selected
        {
            get => selected;
            set
            {
                if (value == selected) return;
                selected = value;
                Invalidate();
                SelectedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void SetSilently(int v) { selected = v; Invalidate(); }

        int IndexOfCell(int cell) => allowNone ? cell - 1 : cell;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int cell = e.X / (Cell + Gap);
            if (cell < 0 || cell >= Count || e.X % (Cell + Gap) >= Cell) return;
            Selected = IndexOfCell(cell);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            for (int cell = 0; cell < Count; cell++)
            {
                int idx = IndexOfCell(cell);
                var r = new Rectangle(cell * (Cell + Gap) + 2, 2, Cell - 2, Cell - 2);
                if (idx < 0)
                {
                    g.FillRectangle(Brushes.White, r);
                    using (var p = new Pen(Color.Red, 2)) g.DrawLine(p, r.Left + 2, r.Bottom - 2, r.Right - 2, r.Top + 2);
                }
                else
                {
                    using (var b = new SolidBrush(Color.FromArgb(unchecked((int)0xFF000000) | Palette.All[idx].Rgb)))
                        g.FillRectangle(b, r);
                }
                g.DrawRectangle(Pens.Gray, r);
                if (idx == selected)
                {
                    using (var p = new Pen(Color.FromArgb(0, 120, 215), 2))
                        g.DrawRectangle(p, r.X - 2, r.Y - 2, r.Width + 3, r.Height + 3);
                }
            }
        }
    }
}
