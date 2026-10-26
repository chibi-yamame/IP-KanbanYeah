using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using KanbanDrawer.Core;
using KanbanDrawer.Core.Board;
using KanbanDrawer.UI.Widgets;

namespace KanbanDrawer.UI.Features
{
    /// <summary>減色: dithering and adjustments for the frame / pictures / stamps (the background colour and text keep their colours), colours used.</summary>
    internal sealed class QuantizeFeature : IFeature
    {
        EditorHost host;
        readonly ComboBox dither = Ui.DropDown("なし", "誤差拡散 (Floyd–Steinberg)", "誤差拡散 (Atkinson)", "組織的ディザ (Bayer)");
        readonly ResetSlider strength = new ResetSlider(0, 100, 100) { Unit = "%" };
        readonly ResetSlider brightness = new ResetSlider(-100, 100, 0);
        readonly ResetSlider contrast = new ResetSlider(-100, 100, 0);
        readonly ResetSlider saturation = new ResetSlider(0, 200, 100) { Unit = "%" };
        readonly CheckBox[] colors = new CheckBox[Palette.Count];
        bool loading;

        public string Title => "減色";

        public Control CreatePanel(EditorHost h)
        {
            host = h;
            var p = new StackPanel();
            p.Controls.Add(Ui.Note("「下書き」で使います。対象はフレーム・画像・スタンプ（背景色・文字は元の色のまま）。", 34));
            p.Controls.Add(Ui.Row("ディザ", dither));
            p.Controls.Add(Ui.Row("ディザの強さ", strength));
            p.Controls.Add(Ui.Row("明るさ", brightness));
            p.Controls.Add(Ui.Row("コントラスト", contrast));
            p.Controls.Add(Ui.Row("彩度", saturation));
            p.Controls.Add(Ui.Note("▼ を押すと元の値に戻ります。", 18));
            p.Controls.Add(Ui.Heading("使う色"));
            var grid = new TableLayoutPanel { Height = 60, ColumnCount = 5, RowCount = 2 };
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            for (int i = 0; i < 5; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            foreach (var c in Palette.All)
            {
                var cb = new SwatchCheck(c) { Checked = true };
                colors[c.Index] = cb;
                grid.Controls.Add(cb, c.Col, c.Row);
                cb.CheckedChanged += (s, e) => Apply();
            }
            p.Controls.Add(grid);
            p.Controls.Add(Ui.Note("イラスト・文字はディザ「なし」、写真は誤差拡散がおすすめです。", 18));
            dither.SelectedIndexChanged += (s, e) => Apply();
            foreach (var s in new[] { strength, brightness, contrast, saturation }) s.ValueChanged += (o, e) => Apply();
            return p;
        }

        public bool Owns(BoardObject o) => false;
        public void OnSelection(BoardObject o) { }

        public void Load(Settings s)
        {
            loading = true;
            dither.SelectedIndex = Math.Max(0, Math.Min(3, s.Get("dither", 1)));
            strength.Value = s.Get("strength", 100);
            string mask = s.Get("colors", "1111111111");
            for (int i = 0; i < Palette.Count; i++) colors[i].Checked = i >= mask.Length || mask[i] != '0';
            loading = false;
            Apply();
        }

        public void Save(Settings s)
        {
            s.Set("dither", dither.SelectedIndex);
            s.Set("strength", strength.Value);
            s.Set("colors", new string(colors.Select(c => c.Checked ? '1' : '0').ToArray()));
        }

        void Apply()
        {
            if (loading || host == null) return;
            if (!colors.Any(c => c.Checked))
            {
                host.Log("使う色を 1 つ以上選んでください");
                return;
            }
            host.SetQuantize(new QuantizeOptions
            {
                Dither = (DitherKind)dither.SelectedIndex,
                Strength = strength.Value / 100.0,
                Brightness = brightness.Value,
                Contrast = contrast.Value,
                Saturation = saturation.Value,
                Enabled = colors.Select(c => c.Checked).ToArray(),
            });
        }

        /// <summary>Checkbox with the colour's name and a swatch.</summary>
        sealed class SwatchCheck : CheckBox
        {
            readonly PaletteColor color;

            public SwatchCheck(PaletteColor c)
            {
                color = c;
                Text = c.Name;
                Dock = DockStyle.Fill;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var r = new Rectangle(Width - 20, (Height - 14) / 2, 16, 14);
                using (var b = new SolidBrush(Color.FromArgb(unchecked((int)0xFF000000) | color.Rgb)))
                    e.Graphics.FillRectangle(b, r);
                e.Graphics.DrawRectangle(Pens.Gray, r);
            }
        }
    }
}
