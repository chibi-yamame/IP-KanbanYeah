using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using KanbanDrawer.Core;
using KanbanDrawer.Core.Board;
using KanbanDrawer.UI.Features;
using KanbanDrawer.Win;
using Part = KanbanDrawer.Core.Board.Handle;

namespace KanbanDrawer.UI
{
    /// <summary>
    /// The board at 1:1 (what will be drawn, or the colours before quantizing) with the selection's handles.
    /// Click picks the topmost object, drag moves it, corners scale, the knob rotates (Shift: 15° steps); arrows nudge
    /// (Shift: 10 px), Delete removes. A margin around the board keeps handles at the edges reachable.
    /// </summary>
    internal sealed class EditorView : Control
    {
        public const int BoardMargin = 10;
        readonly EditorHost host;
        Bitmap image;
        bool showDraft;
        Drag drag;
        bool dragMoved;

        public EditorView(EditorHost host)
        {
            this.host = host;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                     | ControlStyles.Selectable, true);
            TabStop = true;
            Size = new Size(BoardDoc.W + 2 * BoardMargin, BoardDoc.H + 2 * BoardMargin);
            BackColor = Color.FromArgb(60, 60, 64);
            host.Rendered += UpdateImage;
            host.DraftChanged += UpdateImage;
            host.Doc.SelectionChanged += Invalidate;
        }

        /// <summary>Show the draft (10 colours) instead of the full-colour board, while the draft is current.</summary>
        public bool ShowDraft
        {
            get => showDraft;
            set { showDraft = value; UpdateImage(); }
        }

        /// <summary>The draft is what is on screen now.</summary>
        public bool DraftShown => showDraft && host.Draft != null && !host.DraftStale;

        void UpdateImage()
        {
            var r = DraftShown ? host.Draft.ToRaster() : host.Composite?.ToRaster();
            if (r == null) return;
            image?.Dispose();
            image = ImageLoader.ToBitmap(r);
            Invalidate();
        }

        static PointF Page(Point p) => new PointF(p.X - BoardMargin, p.Y - BoardMargin);

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button != MouseButtons.Left) return;
            var p = Page(e.Location);
            var doc = host.Doc;
            var handle = Handles.Pick(doc, p.X, p.Y, out var target);
            doc.Select(target);
            if (handle != Part.None)
            {
                drag = new Drag(doc.Selected, handle, p.X, p.Y);
                dragMoved = false;
                Capture = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var p = Page(e.Location);
            if (drag == null)
            {
                var h = Handles.Pick(host.Doc, p.X, p.Y, out var target);
                Cursor = h == Part.Body ? (target == host.Doc.Selected ? Cursors.SizeAll : Cursors.Hand)
                    : h == Part.Rotate ? Cursors.Hand
                    : h == Part.None ? Cursors.Default
                    : (h == Part.ScaleTL || h == Part.ScaleBR) ? Cursors.SizeNWSE : Cursors.SizeNESW;
                return;
            }
            if (!dragMoved)
            {
                host.EndCheckpointBurst();
                host.Doc.Checkpoint();
                dragMoved = true;
            }
            drag.Update(p.X, p.Y, (ModifierKeys & Keys.Shift) != 0);
            host.Doc.NotifyChanged();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            drag = null;
            Capture = false;
        }

        protected override bool IsInputKey(Keys k)
        {
            var key = k & Keys.KeyCode;
            return key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            var o = host.Doc.Selected;
            if (o == null) { base.OnKeyDown(e); return; }
            int step = e.Shift ? 10 : 1;
            int dx = 0, dy = 0;
            switch (e.KeyCode)
            {
                case Keys.Left: dx = -step; break;
                case Keys.Right: dx = step; break;
                case Keys.Up: dy = -step; break;
                case Keys.Down: dy = step; break;
                case Keys.Delete:
                    host.Doc.Remove(o);
                    e.Handled = true;
                    return;
                case Keys.Escape:
                    host.Doc.Select(null);
                    e.Handled = true;
                    return;
                default:
                    base.OnKeyDown(e);
                    return;
            }
            host.CheckpointFor("nudge");
            o.Cx += dx;
            o.Cy += dy;
            host.Doc.NotifyChanged();
            e.Handled = true;
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (image != null)
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(image, BoardMargin, BoardMargin, image.Width, image.Height);
            }
            g.DrawRectangle(Focused ? Pens.DeepSkyBlue : Pens.Black, BoardMargin - 1, BoardMargin - 1, BoardDoc.W + 1, BoardDoc.H + 1);
            var o = host.Doc.Selected;
            if (o == null) return;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Default;
            var pts = Handles.Points(o);
            PointF P(int i) => new PointF((float)pts[i * 2] + BoardMargin, (float)pts[i * 2 + 1] + BoardMargin);
            var box = new[] { P(0), P(1), P(2), P(3) };
            using (var white = new Pen(Color.White, 1))
            using (var dash = new Pen(Color.FromArgb(0, 120, 215), 1) { DashStyle = DashStyle.Dash })
            {
                g.DrawPolygon(white, box);
                g.DrawPolygon(dash, box);
                if (o.CanRotate) g.DrawLine(white, P(5), P(4));
            }
            if (o.CanScale) foreach (var c in box) DrawHandle(g, c, false);
            if (o.CanRotate) DrawHandle(g, P(4), true);
        }

        static void DrawHandle(Graphics g, PointF c, bool round)
        {
            var r = new RectangleF(c.X - 3.5f, c.Y - 3.5f, 7, 7);
            using (var b = new SolidBrush(round ? Color.FromArgb(0, 120, 215) : Color.White))
            {
                if (round) g.FillEllipse(b, r); else g.FillRectangle(b, r);
            }
            if (round) g.DrawEllipse(Pens.White, r);
            else using (var p = new Pen(Color.FromArgb(0, 120, 215))) g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) image?.Dispose();
            base.Dispose(disposing);
        }
    }
}
