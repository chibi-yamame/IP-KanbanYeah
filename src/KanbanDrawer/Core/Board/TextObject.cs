using System;

namespace KanbanDrawer.Core.Board
{
    /// <summary>Turns text into pixel masks without anti-aliasing (implemented with GDI+ on Windows).</summary>
    public interface ITextRasterizer
    {
        /// <summary>Size of the text's box at Scale 1 (outline included).</summary>
        void Measure(TextObject t, out double width, out double height);

        /// <summary>
        /// Paints the text with its transform into a page mask (row major, page size): 1 = letter, 2 = outline,
        /// 0 = untouched.
        /// </summary>
        void Rasterize(TextObject t, byte[] mask, int pageW, int pageH);
    }

    /// <summary>
    /// Text in palette colours only: pixels are fixed (never dithered), so letters stay crisp at 1 px.
    /// LocalW/H come from the rasterizer (<see cref="Measure"/> after every change of text, font, size, outline).
    /// </summary>
    public sealed class TextObject : BoardObject
    {
        public string Text = "";
        public string Font = "";
        /// <summary>Em size in pixels at Scale 1.</summary>
        public double Size = 24;
        public bool Bold;
        public int Color;
        /// <summary>Palette index of the outline, -1 = none.</summary>
        public int Outline = -1;
        public int OutlineWidth = 1;
        /// <summary>
        /// 公式と同じ: the program's own text (Java SansSerif = Arial + MS Gothic bitmap glyphs), Size 7-22 px, plain, upright,
        /// unscaled, on whole pixels. <see cref="Font"/>, <see cref="Bold"/>, <see cref="Rot"/> and <see cref="BoardObject.Scale"/>
        /// are ignored.
        /// </summary>
        public bool Official;

        public override bool CanRotate => !Official;
        public override bool CanScale => !Official;

        double w = 1, h = 1;

        public override double LocalW => w;
        public override double LocalH => h;

        public void Measure(ITextRasterizer r)
        {
            r.Measure(this, out w, out h);
            w = Math.Max(1, w);
            h = Math.Max(1, h);
        }

        public override BoardObject Clone()
        {
            var o = (TextObject)MemberwiseClone();
            return o;
        }

        public override void Render(Composite target, IRenderServices services)
        {
            if (string.IsNullOrEmpty(Text) || services?.Text == null) return;
            var mask = new byte[target.Width * target.Height];
            services.Text.Rasterize(this, mask, target.Width, target.Height);
            for (int i = 0; i < mask.Length; i++)
            {
                if (mask[i] == 1) target.SetFixed(i, Color);
                else if (mask[i] == 2 && Outline >= 0) target.SetFixed(i, Outline);
            }
        }
    }
}
