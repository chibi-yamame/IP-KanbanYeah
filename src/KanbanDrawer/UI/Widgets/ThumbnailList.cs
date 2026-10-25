using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace KanbanDrawer.UI.Widgets
{
    /// <summary>
    /// Wrapping list of picture tiles (library items). Click = <see cref="ItemClicked"/>; right click offers 削除
    /// (<see cref="DeleteRequested"/>) on deletable tiles. Selection is drawn as a ring.
    /// </summary>
    internal sealed class ThumbnailList : FlowLayoutPanel
    {
        readonly Size tileSize;
        string selected;

        public event Action<string> ItemClicked;
        public event Action<string> DeleteRequested;

        public ThumbnailList(Size tileSize)
        {
            this.tileSize = tileSize;
            AutoScroll = true;
            WrapContents = true;
            BackColor = Color.FromArgb(60, 60, 64);
            Padding = new Padding(2);
        }

        public void Clear()
        {
            foreach (Control c in Controls) c.Dispose();
            Controls.Clear();
        }

        /// <param name="image">tile picture (owned by the tile from now on), or null for a text tile</param>
        public void Add(string id, Image image, string text, bool deletable)
        {
            var tile = new Tile(id, image, text, deletable) { Size = tileSize, Margin = new Padding(3) };
            tile.Selected = id == selected;
            tile.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) ItemClicked?.Invoke(id);
                else if (e.Button == MouseButtons.Right && deletable)
                {
                    var menu = new ContextMenuStrip();
                    menu.Items.Add("削除", null, (s2, e2) => DeleteRequested?.Invoke(id));
                    menu.Show(tile, e.Location);
                }
            };
            Controls.Add(tile);
        }

        public string Selected
        {
            get => selected;
            set
            {
                selected = value;
                foreach (Control c in Controls)
                    if (c is Tile t) t.Selected = t.Id == value;
            }
        }

        sealed class Tile : Control
        {
            public readonly string Id;
            readonly Image image;
            readonly string text;
            bool sel;

            public Tile(string id, Image image, string text, bool deletable)
            {
                Id = id; this.image = image; this.text = text;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
                Cursor = Cursors.Hand;
                if (deletable) new ToolTip().SetToolTip(this, "クリック: 使う / 右クリック: 削除");
            }

            public bool Selected { set { if (sel != value) { sel = value; Invalidate(); } } }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Color.FromArgb(90, 90, 96));
                var inner = new Rectangle(3, 3, Width - 6, Height - 6);
                if (image != null)
                {
                    // checkerboard under transparent pictures
                    for (int y = inner.Top; y < inner.Bottom; y += 6)
                        for (int x = inner.Left; x < inner.Right; x += 6)
                            g.FillRectangle(((x + y) / 6) % 2 == 0 ? Brushes.White : Brushes.Gainsboro,
                                x, y, Math.Min(6, inner.Right - x), Math.Min(6, inner.Bottom - y));
                    double f = Math.Min((double)inner.Width / image.Width, (double)inner.Height / image.Height);
                    int w = Math.Max(1, (int)(image.Width * f)), h = Math.Max(1, (int)(image.Height * f));
                    g.InterpolationMode = f >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(image, inner.Left + (inner.Width - w) / 2, inner.Top + (inner.Height - h) / 2, w, h);
                }
                else
                {
                    TextRenderer.DrawText(g, text, Font, inner, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                }
                if (sel)
                    using (var p = new Pen(Color.FromArgb(0, 150, 255), 3)) g.DrawRectangle(p, 1, 1, Width - 3, Height - 3);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) image?.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
