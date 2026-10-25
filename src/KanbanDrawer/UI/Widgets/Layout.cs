using System;
using System.Drawing;
using System.Windows.Forms;

namespace KanbanDrawer.UI.Widgets
{
    /// <summary>Children stacked top to bottom in the order added, full width; scrolls when taller than the panel.</summary>
    internal sealed class StackPanel : Panel
    {
        public StackPanel()
        {
            AutoScroll = true;
            Padding = new Padding(6);
            BackColor = SystemColors.Window;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            int y = Padding.Top + AutoScrollPosition.Y;
            int w = ClientSize.Width - Padding.Horizontal;
            foreach (Control c in Controls)
            {
                if (!c.Visible) continue;
                c.SetBounds(Padding.Left + c.Margin.Left, y + c.Margin.Top, Math.Max(10, w - c.Margin.Horizontal), c.Height);
                y += c.Height + c.Margin.Vertical;
            }
            AutoScrollMinSize = new Size(0, y - AutoScrollPosition.Y + Padding.Bottom);
        }
    }

    internal static class Ui
    {
        public const int LabelWidth = 76;

        /// <summary>Label on the left, control filling the rest.</summary>
        public static Control Row(string label, Control c, int height = 0)
        {
            var p = new Panel { Height = height > 0 ? height : Math.Max(26, c.Height), Margin = new Padding(0, 1, 0, 1) };
            var l = new Label { Text = label, Dock = DockStyle.Left, Width = LabelWidth, TextAlign = ContentAlignment.MiddleLeft };
            c.Dock = c is PaletteSwatches ? DockStyle.Left : DockStyle.Fill;
            p.Controls.Add(c);
            p.Controls.Add(l);
            return p;
        }

        /// <summary>Buttons (or other controls) side by side.</summary>
        public static Control Buttons(params Control[] controls)
        {
            var p = new FlowLayoutPanel { Height = 30, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
            foreach (var c in controls)
            {
                c.Margin = new Padding(0, 0, 4, 0);
                p.Controls.Add(c);
            }
            return p;
        }

        public static Button Button(string text, Action click)
        {
            var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(4, 0, 4, 0) };
            b.Click += (s, e) => click();
            return b;
        }

        public static Label Heading(string text) =>
            new Label { Text = text, Height = 22, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft, Margin = new Padding(0, 6, 0, 0) };

        public static Label Note(string text, int height = 18) =>
            new Label { Text = text, Height = height, ForeColor = SystemColors.GrayText };

        public static ComboBox DropDown(params string[] items)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            c.Items.AddRange(items);
            c.SelectedIndex = 0;
            return c;
        }
    }
}
