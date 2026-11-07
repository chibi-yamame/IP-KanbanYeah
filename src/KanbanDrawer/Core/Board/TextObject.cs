using System;

namespace KanbanDrawer.Core.Board
{
    /// <summary>
    /// Turns text into pixel masks: 2-value masks without anti-aliasing (<see cref="Rasterize"/>) or anti-aliased coverage
    /// (<see cref="RasterizeCoverage"/>, for full-colour mode). Implemented with GDI+ on Windows.
    /// </summary>
    public interface ITextRasterizer
    {
        /// <summary>Size of the text's box at Scale 1 (outline included).</summary>
        void Measure(TextObject t, out double width, out double height);

        /// <summary>
        /// Paints the text with its transform into a page mask (row major, page size): 1 = letter, 2 = outline,
        /// 0 = untouched.
        /// </summary>
        void Rasterize(TextObject t, byte[] mask, int pageW, int pageH);

        /// <summary>
        /// Anti-aliased coverage 0..255 (0 = untouched, 255 = fully covered) written into two page-size arrays (row major,
        /// zeroed by the caller): <paramref name="letter"/> = the letters, <paramref name="outline"/> = the area including
        /// the outline (255 under the letters too; all 0 without an outline or with width 0). Same transform as
        /// <see cref="Rasterize"/>. For <see cref="TextObject.Official"/> the 2-value result (0 / 255).
        /// </summary>
        void RasterizeCoverage(TextObject t, byte[] letter, byte[] outline, int pageW, int pageH);
    }

    /// <summary>
    /// Text in palette colours only: pixels are fixed (never dithered), so letters stay crisp at 1 px. With
    /// <see cref="IRenderServices.Smooth"/> (full-colour mode) non-official text is blended by coverage instead (free pixels).
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
            if (services.Smooth && !Official) { RenderSmooth(target, services.Text); return; }
            var mask = new byte[target.Width * target.Height];
            services.Text.Rasterize(this, mask, target.Width, target.Height);
            for (int i = 0; i < mask.Length; i++)
            {
                if (mask[i] == 1) target.SetFixed(i, Color);
                else if (mask[i] == 2 && Outline >= 0) target.SetFixed(i, Outline);
            }
        }

        /// <summary>Full-colour mode: outline first, then the letters over it, blended by coverage (pixels become free).</summary>
        void RenderSmooth(Composite target, ITextRasterizer text)
        {
            int n = target.Width * target.Height;
            var letter = new byte[n];
            var outline = new byte[n];
            text.RasterizeCoverage(this, letter, outline, target.Width, target.Height);
            var fg = Palette.All[Color];
            var oc = Outline >= 0 ? Palette.All[Outline] : null;
            for (int i = 0; i < n; i++)
            {
                if (oc != null && outline[i] > 0) target.Blend(i, oc.R, oc.G, oc.B, outline[i] / 255.0);
                if (letter[i] > 0) target.Blend(i, fg.R, fg.G, fg.B, letter[i] / 255.0);
            }
        }
    }
}
