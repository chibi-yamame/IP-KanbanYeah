using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace KanbanDrawer.UI.Widgets
{
    /// <summary>
    /// Wrapping list of picture tiles (library items). Click = <see cref="ItemClicked"/> (use the item). Deletable tiles
    /// can also be picked for deletion (<see cref="CheckedIds"/>, translucent blue + ✓): Ctrl+click toggles, Shift+click
    /// picks a range, Ctrl+A picks all; a plain click clears the picks. Right click 削除 / the Delete key ask for
    /// <see cref="DeleteRequested"/>. The item in use (<see cref="Selected"/>) is drawn as a ring, independently.
    /// </summary>
    internal sealed class ThumbnailList : FlowLayoutPanel
    {
        readonly Size tileSize;
        string selected;
        readonly HashSet<string> picked = new HashSet<string>();
        string anchor; // Shift+click range start

        public event Action<string> ItemClicked;
        /// <summary>Ids to delete, in list order: the picks, or the right-clicked tile when it is not picked.</summary>
        public event Action<IReadOnlyList<string>> DeleteRequested;
        /// <summary><see cref="CheckedIds"/> changed.</summary>
        public event Action CheckedChanged;

        public ThumbnailList(Size tileSize)
        {
            this.tileSize = tileSize;
            AutoScroll = true;
            WrapContents = true;
            BackColor = Color.FromArgb(60, 60, 64);
            Padding = new Padding(2);
            SetStyle(ControlStyles.Selectable, true); // takes focus for Ctrl+A / Delete
            TabStop = true;
        }

        IEnumerable<Tile> Tiles => Controls.OfType<Tile>();

        public void Clear()
        {
            foreach (Control c in Controls) c.Dispose();
            Controls.Clear();
            anchor = null;
            if (picked.Count > 0) { picked.Clear(); CheckedChanged?.Invoke(); }
        }

        /// <param name="image">tile picture (owned by the tile from now on), or null for a text tile</param>
        public void Add(string id, Image image, string text, bool deletable)
        {
            var tile = new Tile(id, image, text, deletable) { Size = tileSize, Margin = new Padding(3) };
            tile.Selected = id == selected;
            tile.MouseDown += (s, e) => Focus();
            tile.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) OnTileClick(tile);
                else if (e.Button == MouseButtons.Right && deletable)
                {
                    var ids = picked.Contains(id) ? CheckedIds : new[] { id };
                    var menu = new ContextMenuStrip();
                    menu.Items.Add(ids.Count > 1 ? $"削除（{ids.Count} 個）" : "削除", null, (s2, e2) => DeleteRequested?.Invoke(ids));
                    menu.Show(tile, e.Location);
                }
            };
            Controls.Add(tile);
        }

        /// <summary>The item in use (ring).</summary>
        public string Selected
        {
            get => selected;
            set
            {
                selected = value;
                foreach (var t in Tiles) t.Selected = t.Id == value;
            }
        }

        /// <summary>Picked ids, in list order.</summary>
        public IReadOnlyList<string> CheckedIds => Tiles.Where(t => picked.Contains(t.Id)).Select(t => t.Id).ToList();

        void OnTileClick(Tile tile)
        {
            var mods = ModifierKeys;
            bool ctrl = (mods & Keys.Control) != 0, shift = (mods & Keys.Shift) != 0;
            if (!ctrl && !shift)
            {
                anchor = tile.Deletable ? tile.Id : null;
                SetPicked(Enumerable.Empty<string>());
                ItemClicked?.Invoke(tile.Id);
                return;
            }
            if (!tile.Deletable) return;
            if (shift)
            {
                var all = Tiles.ToList();
                int a = anchor == null ? -1 : all.FindIndex(t => t.Id == anchor);
                int b = all.IndexOf(tile);
                if (a < 0) a = b;
                var range = all.Skip(Math.Min(a, b)).Take(Math.Abs(a - b) + 1).Where(t => t.Deletable).Select(t => t.Id);
                SetPicked(ctrl ? picked.Concat(range) : range);
            }
            else
            {
                anchor = tile.Id;
                var next = new HashSet<string>(picked);
                if (!next.Remove(tile.Id)) next.Add(tile.Id);
                SetPicked(next);
            }
        }

        void SetPicked(IEnumerable<string> ids)
        {
            var next = new HashSet<string>(ids);
            if (next.SetEquals(picked)) return;
            picked.Clear();
            picked.UnionWith(next);
            foreach (var t in Tiles) t.Checked = picked.Contains(t.Id);
            CheckedChanged?.Invoke();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.A && e.Control && !e.Shift && !e.Alt)
            {
                SetPicked(Tiles.Where(t => t.Deletable).Select(t => t.Id));
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Delete && e.Modifiers == Keys.None && picked.Count > 0)
            {
                DeleteRequested?.Invoke(CheckedIds);
                e.Handled = true;
            }
            else base.OnKeyDown(e);
        }

        sealed class Tile : Control
        {
            public readonly string Id;
            public readonly bool Deletable;
            readonly Image image;
            readonly string text;
            bool sel, check;

            public Tile(string id, Image image, string text, bool deletable)
            {
                Id = id; Deletable = deletable; this.image = image; this.text = text;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
                SetStyle(ControlStyles.Selectable, false); // focus stays on the list
                Cursor = Cursors.Hand;
                if (deletable) new ToolTip().SetToolTip(this, "クリック: 使う / Ctrl・Shift+クリック: 選ぶ / 右クリック: 削除");
            }

            public bool Selected { set { if (sel != value) { sel = value; Invalidate(); } } }
            public bool Checked { set { if (check != value) { check = value; Invalidate(); } } }

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
                    g.PixelOffsetMode = PixelOffsetMode.Default;
                }
                else
                {
                    TextRenderer.DrawText(g, text, Font, inner, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                }
                if (check)
                {
                    // picked for deletion: translucent blue + ✓ in the top right corner
                    using (var b = new SolidBrush(Color.FromArgb(110, 30, 120, 255))) g.FillRectangle(b, 0, 0, Width, Height);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    var c = new Rectangle(Width - 19, 3, 16, 16);
                    using (var b = new SolidBrush(Color.FromArgb(0, 110, 230))) g.FillEllipse(b, c);
                    using (var p = new Pen(Color.White, 2))
                        g.DrawLines(p, new[] { new Point(c.Left + 4, c.Top + 8), new Point(c.Left + 7, c.Top + 11), new Point(c.Left + 12, c.Top + 5) });
                    g.SmoothingMode = SmoothingMode.Default;
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
