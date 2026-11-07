using System;
using System.Linq;
using System.Windows.Forms;
using KanbanDrawer.Core;
using KanbanDrawer.Core.Board;
using KanbanDrawer.UI;
using KanbanDrawer.UI.Features;
using KanbanDrawer.Win;
using Xunit;

namespace KanbanDrawer.Tests
{
    /// <summary>
    /// Stand-in rasterizer with known pixels. 2-value: pixel 0 = letter, pixel 1 = outline. Coverage: pixel 10 = letter 128,
    /// pixel 11 = letter 255 over outline 255, pixel 12 = outline only 255, pixel 13 = outline only 128. The coverage arrays are
    /// filled even without an outline, so the tests can see that <see cref="TextObject"/> ignores them then.
    /// </summary>
    internal sealed class CoverageText : ITextRasterizer, IRenderServices
    {
        public bool Smooth { get; set; }
        public ITextRasterizer Text => this;
        public int RasterizeCalls, CoverageCalls;

        public void Measure(TextObject t, out double width, out double height) { width = 10; height = 10; }

        public void Rasterize(TextObject t, byte[] mask, int w, int h)
        {
            RasterizeCalls++;
            mask[0] = 1;
            mask[1] = 2;
        }

        public void RasterizeCoverage(TextObject t, byte[] letter, byte[] outline, int w, int h)
        {
            CoverageCalls++;
            letter[10] = 128;
            letter[11] = 255; outline[11] = 255;
            outline[12] = 255;
            outline[13] = 128;
        }
    }

    public class SmoothTextTests
    {
        const int Red = 4, Blue = 6, Bg = Palette.White;

        static Composite White()
        {
            var c = new Composite(BoardDoc.W, BoardDoc.H);
            for (int i = 0; i < c.Fixed.Length; i++) c.SetFixed(i, Bg);
            return c;
        }

        static TextObject Text(int outline = Blue, bool official = false) =>
            new TextObject { Text = "A", Color = Red, Outline = outline, Official = official, Cx = 160, Cy = 120 };

        static void AssertColour(Composite c, int i, double r, double g, double b)
        {
            Assert.Equal(r, c.Rgb[i * 3], 3);
            Assert.Equal(g, c.Rgb[i * 3 + 1], 3);
            Assert.Equal(b, c.Rgb[i * 3 + 2], 3);
        }

        static void AssertPalette(Composite c, int i, int index)
        {
            var p = Palette.All[index];
            AssertColour(c, i, p.R, p.G, p.B);
        }

        [Fact]
        public void WithoutSmoothTheTextStaysFixedAndTwoValued()
        {
            var r = new CoverageText { Smooth = false };
            var c = White();
            Text().Render(c, r);
            Assert.Equal(1, r.RasterizeCalls);
            Assert.Equal(0, r.CoverageCalls);
            Assert.Equal(Red, c.Fixed[0]);
            Assert.Equal(Blue, c.Fixed[1]);
            AssertPalette(c, 0, Red);
            AssertPalette(c, 1, Blue);
            for (int i = 2; i < c.Fixed.Length; i++) Assert.Equal(Bg, c.Fixed[i]);
        }

        [Fact]
        public void SmoothBlendsByCoverageIntoFreePixels()
        {
            var r = new CoverageText { Smooth = true };
            var c = White();
            Text().Render(c, r);
            Assert.Equal(0, r.RasterizeCalls);
            Assert.Equal(1, r.CoverageCalls);

            // 128: free, halfway between the white background and red
            double a = 128 / 255.0;
            var red = Palette.All[Red];
            Assert.Equal(-1, c.Fixed[10]);
            AssertColour(c, 10, red.R * a + 255 * (1 - a), red.G * a + 255 * (1 - a), red.B * a + 255 * (1 - a));

            // 255: exactly the text colour, free (letters over the outline)
            Assert.Equal(-1, c.Fixed[11]);
            AssertPalette(c, 11, Red);

            // outline only: the outline colour, free; its 128 edge is mixed with the background
            Assert.Equal(-1, c.Fixed[12]);
            AssertPalette(c, 12, Blue);
            var blue = Palette.All[Blue];
            Assert.Equal(-1, c.Fixed[13]);
            AssertColour(c, 13, blue.R * a + 255 * (1 - a), blue.G * a + 255 * (1 - a), blue.B * a + 255 * (1 - a));

            // the rest is untouched
            foreach (int i in new[] { 0, 1, 9, 14, 1000 }) { Assert.Equal(Bg, c.Fixed[i]); AssertPalette(c, i, Bg); }
        }

        [Fact]
        public void WithoutAnOutlineTheOutlineCoverageIsNotUsed()
        {
            var r = new CoverageText { Smooth = true };
            var c = White();
            Text(outline: -1).Render(c, r);
            Assert.Equal(Bg, c.Fixed[12]);
            Assert.Equal(Bg, c.Fixed[13]);
            AssertPalette(c, 12, Bg);
            AssertPalette(c, 11, Red);
        }

        [Fact]
        public void OfficialTextStaysFixedEvenWhenSmooth()
        {
            var r = new CoverageText { Smooth = true };
            var c = White();
            Text(official: true).Render(c, r);
            Assert.Equal(1, r.RasterizeCalls);
            Assert.Equal(0, r.CoverageCalls);
            Assert.Equal(Red, c.Fixed[0]);
            Assert.Equal(Blue, c.Fixed[1]);
            Assert.Equal(Bg, c.Fixed[10]);
        }

        [Fact]
        public void EmptyTextDrawsNothing()
        {
            var r = new CoverageText { Smooth = true };
            var c = White();
            var t = Text(); t.Text = "";
            t.Render(c, r);
            Assert.Equal(0, r.RasterizeCalls + r.CoverageCalls);
            Assert.All(c.Fixed, f => Assert.Equal(Bg, f));
        }

        [Fact]
        public void HostFullColorSetsSmoothAndRendersOnlyOnChange()
        {
            using (var ui = new Control()) // no handle: Invalidate renders at once
            {
                var host = new EditorHost(new Settings(), ui);
                int renders = 0;
                host.Rendered += () => renders++;
                Assert.False(host.Text.Smooth);
                host.FullColor = true;
                Assert.True(host.Text.Smooth);
                Assert.Equal(1, renders);
                host.FullColor = true;
                Assert.Equal(1, renders);
                host.FullColor = false;
                Assert.False(host.Text.Smooth);
                Assert.Equal(2, renders);
            }
        }

        // ---- GDI+ ----

        static TextObject Big(GdiTextRasterizer r, int outline)
        {
            var t = new TextObject { Text = "看板A", Font = GdiTextRasterizer.DefaultFont(), Size = 60, Color = Red, Outline = outline, OutlineWidth = 2, Cx = 160, Cy = 120 };
            t.Measure(r);
            return t;
        }

        [Fact]
        public void GdiCoverageHasEdgesAndSolidInsides()
        {
            var r = new GdiTextRasterizer { Smooth = true };
            var t = Big(r, Blue);
            var letter = new byte[320 * 240];
            var outline = new byte[320 * 240];
            r.RasterizeCoverage(t, letter, outline, 320, 240);
            Assert.True(letter.Count(v => v == 255) > 300, "solid letters");
            Assert.True(letter.Count(v => v > 0 && v < 255) > 50, "anti-aliased letter edges");
            Assert.True(outline.Count(v => v > 0 && v < 255) > 50, "anti-aliased outline edges");
            // the outline area covers the letters
            for (int i = 0; i < letter.Length; i++)
                if (letter[i] == 255) Assert.Equal(255, outline[i]);

            // about the same area as the 2-value mask
            var mask = new byte[320 * 240];
            r.Rasterize(t, mask, 320, 240);
            int binLetter = mask.Count(m => m == 1), aaLetter = letter.Count(v => v >= 128);
            Assert.InRange(aaLetter, binLetter * 0.85, binLetter * 1.15);
            int binAll = mask.Count(m => m != 0), aaAll = outline.Count(v => v >= 128);
            Assert.InRange(aaAll, binAll * 0.85, binAll * 1.15);
        }

        [Fact]
        public void GdiCoverageWithoutOutlineLeavesOutlineEmpty()
        {
            var r = new GdiTextRasterizer { Smooth = true };
            var t = Big(r, -1);
            var letter = new byte[320 * 240];
            var outline = new byte[320 * 240];
            r.RasterizeCoverage(t, letter, outline, 320, 240);
            Assert.Contains(letter, v => v == 255);
            Assert.All(outline, v => Assert.Equal(0, v));
        }

        [Fact]
        public void GdiSmoothDoesNotChangeTheTwoValueMask()
        {
            var r = new GdiTextRasterizer();
            var t = Big(r, Blue);
            var before = new byte[320 * 240];
            r.Rasterize(t, before, 320, 240);
            r.Smooth = true;
            var after = new byte[320 * 240];
            r.Rasterize(t, after, 320, 240);
            Assert.Equal(before, after);
        }

        [Fact]
        public void GdiSmoothTextRendersFreePixels()
        {
            var r = new GdiTextRasterizer { Smooth = true };
            var doc = new BoardDoc { BackgroundColor = Bg };
            doc.Objects.Add(Big(r, Blue));
            var c = Compositor.Build(doc, r);
            Assert.Contains(c.Fixed, f => f == -1);
            Assert.DoesNotContain(c.Fixed, f => f == Red || f == Blue);
            r.Smooth = false;
            c = Compositor.Build(doc, r);
            Assert.DoesNotContain(c.Fixed, f => f == -1);
            Assert.Contains(c.Fixed, f => f == Red);
        }
    }
}
