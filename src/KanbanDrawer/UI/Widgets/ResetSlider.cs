using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KanbanDrawer.UI.Widgets
{
    /// <summary>
    /// The standard Windows slider (TrackBar, steps of 1, no snapping) with a ▼ above the default value's position
    /// that resets it, and the value in a fixed-width box on the right (never runs out of the control).
    /// </summary>
    internal sealed class ResetSlider : UserControl
    {
        const int MarkerH = 9, ValueW = 42;
        readonly TrackBar bar;
        readonly Marker marker;
        readonly Label valueLabel = new Label { Dock = DockStyle.Right, Width = ValueW, TextAlign = ContentAlignment.MiddleRight };
        readonly int def;
        bool silent;
        string unit = "";

        public event EventHandler ValueChanged;

        public ResetSlider(int min, int max, int def)
        {
            this.def = def;
            bar = new TrackBar
            {
                Minimum = min, Maximum = max, Value = def,
                SmallChange = 1, LargeChange = 10,
                TickStyle = TickStyle.None, AutoSize = false, Height = 26,
                Dock = DockStyle.Fill,
            };
            marker = new Marker(this) { Dock = DockStyle.Top, Height = MarkerH };
            var track = new Panel { Dock = DockStyle.Fill };
            track.Controls.Add(bar);
            track.Controls.Add(marker);
            Controls.Add(track);
            // − / ＋ beside the slider (1 step; held down they repeat), level with the slider below the ▼ strip
            Controls.Add(StepButton("＋", +1, DockStyle.Right));
            Controls.Add(StepButton("－", -1, DockStyle.Left));
            Controls.Add(valueLabel);
            Height = MarkerH + 26;
            bar.ValueChanged += (s, e) =>
            {
                ShowValue();
                marker.Invalidate();
                if (!silent) ValueChanged?.Invoke(this, EventArgs.Empty);
            };
            bar.Resize += (s, e) => marker.Invalidate();
            ShowValue();
        }

        /// <summary>Text after the number (e.g. "%" or "°").</summary>
        public string Unit
        {
            get => unit;
            set { unit = value; ShowValue(); }
        }

        public int Default => def;
        public int Minimum => bar.Minimum;
        public int Maximum => bar.Maximum;

        public int Value
        {
            get => bar.Value;
            set => bar.Value = Math.Max(bar.Minimum, Math.Min(bar.Maximum, value));
        }

        /// <summary>Sets the value without raising <see cref="ValueChanged"/> (showing a selected object's state).</summary>
        public void SetSilently(int v)
        {
            silent = true;
            try { Value = v; } finally { silent = false; }
        }

        public override Color BackColor
        {
            get => base.BackColor;
            set { base.BackColor = value; if (bar != null) bar.BackColor = value; }
        }

        void ShowValue() => valueLabel.Text = bar.Value + unit;

        Control StepButton(string text, int step, DockStyle dock)
        {
            var holder = new Panel { Dock = dock, Width = 24, Padding = new Padding(1, MarkerH + 2, 1, 2) };
            var b = new RepeatButton { Text = text, Dock = DockStyle.Fill, Padding = new Padding(0), Margin = new Padding(0), TabStop = false };
            b.Step += () => Value += step;
            holder.Controls.Add(b);
            return holder;
        }

        /// <summary>A button that acts on press and keeps acting while held (after 400 ms, every 60 ms).</summary>
        sealed class RepeatButton : Button
        {
            readonly Timer timer = new Timer();
            public event Action Step;

            public RepeatButton()
            {
                timer.Tick += (s, e) => { timer.Interval = 60; Step?.Invoke(); };
            }

            bool byMouse;

            // keyboard / programmatic clicks step once; mouse presses already stepped on the press
            protected override void OnClick(EventArgs e)
            {
                if (!byMouse) Step?.Invoke();
                byMouse = false;
                base.OnClick(e);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                byMouse = true;
                Step?.Invoke();
                timer.Interval = 400;
                timer.Start();
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                timer.Stop();
                base.OnMouseUp(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                timer.Stop();
                base.OnMouseLeave(e);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) timer.Dispose();
                base.Dispose(disposing);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref RECT r);

        const int TBM_GETTHUMBRECT = 0x0419, TBM_GETCHANNELRECT = 0x041A;

        /// <summary>
        /// Centre x (in the slider's coordinates) of the thumb when at <paramref name="v"/>: the channel minus a thumb
        /// width is spread linearly over the range (the same mapping the control uses).
        /// </summary>
        int ThumbCenterX(int v)
        {
            if (!bar.IsHandleCreated) return bar.Width / 2;
            var ch = new RECT();
            var th = new RECT();
            SendMessage(bar.Handle, TBM_GETCHANNELRECT, IntPtr.Zero, ref ch);
            SendMessage(bar.Handle, TBM_GETTHUMBRECT, IntPtr.Zero, ref th);
            int tw = th.Right - th.Left;
            int range = Math.Max(1, bar.Maximum - bar.Minimum);
            double span = (ch.Right - ch.Left) - tw;
            return ch.Left + tw / 2 + (int)Math.Round((v - bar.Minimum) * span / range);
        }

        void Reset() => Value = def;

        /// <summary>The strip above the slider with the ▼ at the default value.</summary>
        sealed class Marker : Control
        {
            readonly ResetSlider owner;

            public Marker(ResetSlider owner)
            {
                this.owner = owner;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
                new ToolTip().SetToolTip(this, "▼ を押すと元の値に戻ります");
            }

            int X => owner.ThumbCenterX(owner.def);

            protected override void OnMouseMove(MouseEventArgs e) =>
                Cursor = Math.Abs(e.X - X) <= 7 ? Cursors.Hand : Cursors.Default;

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && Math.Abs(e.X - X) <= 7) owner.Reset();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(owner.BackColor);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                int x = X;
                bool atDefault = owner.Value == owner.def;
                using (var b = new SolidBrush(atDefault ? Color.FromArgb(160, 160, 160) : Color.FromArgb(0, 120, 215)))
                    g.FillPolygon(b, new[] { new Point(x - 5, 1), new Point(x + 5, 1), new Point(x, Height - 1) });
            }
        }
    }
}
