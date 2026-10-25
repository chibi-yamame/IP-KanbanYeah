using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using KanbanDrawer.Core;
using KanbanDrawer.Core.Board;
using KanbanDrawer.UI.Widgets;
using KanbanDrawer.Win;

namespace KanbanDrawer.UI.Features
{
    /// <summary>
    /// 文字: text objects in palette colours only, drawn without anti-aliasing (crisp at 1 px), any installed font.
    /// With a text selected the controls edit it; otherwise they set up the next 文字を追加.
    /// </summary>
    internal sealed class TextFeature : IFeature
    {
        EditorHost host;
        readonly TextBox text = new TextBox { Multiline = true, Height = 54, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };
        readonly ComboBox font = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
        readonly NumericUpDown size = new NumericUpDown { Minimum = 6, Maximum = 200, Value = 24 };
        readonly CheckBox bold = new CheckBox { Text = "太字", AutoSize = true };
        readonly PaletteSwatches color = new PaletteSwatches(false, 0);
        readonly PaletteSwatches outline = new PaletteSwatches(true, -1);
        readonly NumericUpDown outlineWidth = new NumericUpDown { Minimum = 1, Maximum = 5, Value = 1, Width = 50 };
        readonly ResetSlider angle = new ResetSlider(-180, 180, 0) { Unit = "°" };
        readonly RadioButton official = new RadioButton { Text = "公式と同じ（ドット文字）", AutoSize = true, Checked = true };
        readonly RadioButton freeFont = new RadioButton { Text = "フォントを選ぶ", AutoSize = true };
        readonly Label fallbackNote = new Label
        {
            Height = 18, ForeColor = Color.FromArgb(200, 80, 0), Visible = false,
            Text = "InterPot 用の Java が見つからないため、英数字は近い形で表示しています",
        };
        // the size control is shared; each mode remembers its own size
        int officialSize = OfficialText.DefaultSize, freeSize = 24;
        Control objectButtons;
        TextObject current;
        bool showing;

        public string Title => "文字";

        public Control CreatePanel(EditorHost h)
        {
            host = h;
            font.Items.AddRange(GdiTextRasterizer.InstalledFonts().OrderBy(n => n).Cast<object>().ToArray());
            font.Text = GdiTextRasterizer.DefaultFont();

            var p = new StackPanel();
            p.Controls.Add(Ui.Row("文字の形", Ui.Buttons(official, freeFont)));
            p.Controls.Add(fallbackNote);
            p.Controls.Add(Ui.Row("文字", text, 56));
            p.Controls.Add(Ui.Row("フォント", font));
            var sizeRow = new FlowLayoutPanel { WrapContents = false, Height = 26 };
            size.Width = 60;
            sizeRow.Controls.Add(size);
            sizeRow.Controls.Add(new Label { Text = "px", AutoSize = true, Padding = new Padding(0, 6, 8, 0) });
            sizeRow.Controls.Add(bold);
            p.Controls.Add(Ui.Row("サイズ", sizeRow));
            p.Controls.Add(Ui.Row("色", color));
            p.Controls.Add(Ui.Row("縁取り", outline));
            var widthRow = new FlowLayoutPanel { WrapContents = false, Height = 26 };
            widthRow.Controls.Add(outlineWidth);
            widthRow.Controls.Add(new Label { Text = "px", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            p.Controls.Add(Ui.Row("縁の幅", widthRow));
            p.Controls.Add(Ui.Row("回転", angle));
            p.Controls.Add(Ui.Buttons(Ui.Button("文字を追加", Add)));
            objectButtons = Ui.Buttons(
                Ui.Button("前面へ", () => host.Doc.Restack(current, +1)),
                Ui.Button("背面へ", () => host.Doc.Restack(current, -1)),
                Ui.Button("看板から削除", () => host.Doc.Remove(current)));
            p.Controls.Add(objectButtons);
            p.Controls.Add(Ui.Note("文字は 10 色のまま描きます（にじみません）。選択中の文字は、ここでの変更がすぐ反映されます。\n" +
                "公式と同じ: InterPot の文字ツールと同じ字形（10〜22px・既定 12px）。", 50));

            official.CheckedChanged += (s, e) => { if (official.Checked) SwitchMode(true); };
            freeFont.CheckedChanged += (s, e) => { if (freeFont.Checked) SwitchMode(false); };
            text.TextChanged += (s, e) => Edit("text", t => t.Text = text.Text);
            font.SelectedIndexChanged += (s, e) => Edit("font", t => t.Font = font.Text);
            font.Leave += (s, e) => Edit("font", t => t.Font = font.Text);
            size.ValueChanged += (s, e) =>
            {
                if (showing) return;
                if (official.Checked) officialSize = (int)size.Value; else freeSize = (int)size.Value;
                Edit("size", t => { t.Size = (double)size.Value; t.Scale = 1; });
            };
            bold.CheckedChanged += (s, e) => Edit("bold", t => t.Bold = !t.Official && bold.Checked);
            color.SelectedChanged += (s, e) => Edit("color", t => t.Color = color.Selected);
            outline.SelectedChanged += (s, e) => Edit("outline", t => t.Outline = outline.Selected);
            outlineWidth.ValueChanged += (s, e) => Edit("outlineWidth", t => t.OutlineWidth = (int)outlineWidth.Value);
            angle.ValueChanged += (s, e) => Edit("angle", t => t.Rot = angle.Value * Math.PI / 180);
            host.Doc.Changed += () => { if (current != null) Show(current); };
            OnSelection(null);
            ApplyModeUi(official.Checked);
            // once it is known whether the program's renderer (Java) draws the text: note, and redraw official texts
            OfficialText.ExactChanged += () => { if (!h.Ui.IsDisposed) h.Ui.BeginInvoke((Action)RendererChanged); };
            // an editable combo selects its whole text when it is set; nothing should look selected
            font.HandleCreated += (s, e) => font.BeginInvoke((Action)(() => font.SelectionLength = 0));
            font.EnabledChanged += (s, e) => font.SelectionLength = 0;
            return p;
        }

        /// <summary>Mode radio changed by the user: controls follow, the selected text switches too.</summary>
        bool fallbackKnown;

        void RendererChanged()
        {
            fallbackKnown = true;
            fallbackNote.Visible = official.Checked && !OfficialText.Exact;
            bool any = false;
            foreach (var o in host.Doc.Objects)
                if (o is TextObject t && t.Official) { t.Measure(host.Text); any = true; }
            if (any) host.Doc.NotifyChanged();
        }

        void SwitchMode(bool isOfficial)
        {
            if (showing) return;
            ApplyModeUi(isOfficial);
            Edit("mode", t =>
            {
                t.Official = isOfficial;
                t.Size = (double)size.Value;
                t.Scale = 1;
                if (isOfficial) { t.Rot = 0; t.Bold = false; }
            });
        }

        /// <summary>Enables what the mode allows; the size range and the remembered size of that mode.</summary>
        void ApplyModeUi(bool isOfficial)
        {
            bool was = showing;
            showing = true;
            try
            {
                font.Enabled = !isOfficial;
                angle.Enabled = !isOfficial;
                if (isOfficial)
                {
                    size.Minimum = OfficialText.MinSize;
                    size.Maximum = OfficialText.MaxSize;
                    size.Value = Math.Max(size.Minimum, Math.Min(size.Maximum, officialSize));
                }
                else
                {
                    size.Minimum = 6;
                    size.Maximum = 200;
                    size.Value = Math.Max(size.Minimum, Math.Min(size.Maximum, freeSize));
                }
                bold.Enabled = !isOfficial; // the program's tool has no bold
                if (isOfficial) OfficialText.Warmup();
                fallbackNote.Visible = isOfficial && OfficialText.Exact == false && fallbackKnown;
                if (font.IsHandleCreated) font.SelectionLength = 0;
            }
            finally { showing = was; }
        }

        public bool Owns(BoardObject o) => o is TextObject;

        public void OnSelection(BoardObject o)
        {
            current = o as TextObject;
            objectButtons.Enabled = current != null;
            if (current != null) Show(current);
        }

        public void Load(Settings s)
        {
            officialSize = Math.Max(OfficialText.MinSize, Math.Min(OfficialText.MaxSize, s.Get("text.officialSize", OfficialText.DefaultSize)));
            freeSize = Math.Max(6, Math.Min(200, s.Get("text.size", 24)));
            if (s.Get("text.official", 1) != 0) official.Checked = true; else freeFont.Checked = true;
            ApplyModeUi(official.Checked);
            string f = s.Get("text.font", "");
            if (f.Length > 0 && font.Items.Contains(f)) font.Text = f;

            bold.Checked = s.Get("text.bold", 0) != 0;
            color.SetSilently(Math.Max(0, Math.Min(Palette.Count - 1, s.Get("text.color", 0))));
            outline.SetSilently(Math.Max(-1, Math.Min(Palette.Count - 1, s.Get("text.outline", -1))));
            outlineWidth.Value = Math.Max(1, Math.Min(5, s.Get("text.outlineWidth", 1)));
        }

        public void Save(Settings s)
        {
            s.Set("text.font", font.Text);
            s.Set("text.size", freeSize);
            s.Set("text.officialSize", officialSize);
            s.Set("text.official", official.Checked ? 1 : 0);
            s.Set("text.bold", bold.Checked ? 1 : 0);
            s.Set("text.color", color.Selected);
            s.Set("text.outline", outline.Selected);
            s.Set("text.outlineWidth", (int)outlineWidth.Value);
        }

        void Add()
        {
            var t = new TextObject
            {
                Text = text.Text.Length > 0 ? text.Text : "テキスト",
                Font = font.Text,
                Size = (double)size.Value,
                Bold = !official.Checked && bold.Checked,
                Color = color.Selected,
                Outline = outline.Selected,
                OutlineWidth = (int)outlineWidth.Value,
                Rot = official.Checked ? 0 : angle.Value * Math.PI / 180,
                Official = official.Checked,
            };
            t.Measure(host.Text);
            host.AddObject(t);
        }

        /// <summary>Shows a text's values in the controls (without editing it back).</summary>
        void Show(TextObject t)
        {
            showing = true;
            try
            {
                if (official.Checked != t.Official) { official.Checked = t.Official; freeFont.Checked = !t.Official; }
                if (t.Official) officialSize = (int)Math.Round(t.Size); else freeSize = (int)Math.Round(t.Size * t.Scale);
                ApplyModeUi(t.Official);
                if (text.Text != t.Text) text.Text = t.Text;
                if (font.Text != t.Font) font.Text = t.Font;
                decimal sz = Math.Max(size.Minimum, Math.Min(size.Maximum, (decimal)Math.Round(t.Size * t.Scale)));
                if (size.Value != sz) size.Value = sz;
                bold.Checked = t.Bold;
                color.SetSilently(t.Color);
                outline.SetSilently(t.Outline);
                outlineWidth.Value = Math.Max(1, Math.Min(5, t.OutlineWidth));
                angle.SetSilently((int)Math.Round(t.Rot * 180 / Math.PI));
            }
            finally { showing = false; }
        }

        void Edit(string what, Action<TextObject> change)
        {
            if (showing || current == null || host == null) return;
            host.CheckpointFor(current.GetHashCode() + what);
            change(current);
            current.Measure(host.Text);
            host.Doc.NotifyChanged();
        }
    }
}
