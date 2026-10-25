using System.Drawing;
using System.Windows.Forms;

namespace KanbanDrawer.UI
{
    /// <summary>Small always-on-top status box shown while drawing; never takes the focus away from the game.</summary>
    internal sealed class ProgressOverlay : Form
    {
        const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20;
        readonly Label label = new Label { Dock = DockStyle.Fill, ForeColor = Color.White, TextAlign = ContentAlignment.MiddleLeft };

        public ProgressOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(40, 40, 48);
            Font = new Font("Yu Gothic UI", 10f);
            Size = new Size(300, 64);
            Padding = new Padding(10, 4, 10, 4);
            TopMost = true;
            label.Text = "準備中...\nEsc で中止";
            Controls.Add(label);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_TRANSPARENT;
                return cp;
            }
        }

        public void SetText(string s)
        {
            if (!IsDisposed) label.Text = s;
        }
    }
}
