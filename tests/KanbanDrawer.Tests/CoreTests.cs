using System;
using System.IO;
using System.Linq;
using KanbanDrawer.Core;
using KanbanDrawer.Win;
using Xunit;

namespace KanbanDrawer.Tests
{
    public class PaletteTests
    {
        [Fact]
        public void TenDistinctColoursInFiveColumnsTwoRows()
        {
            Assert.Equal(10, Palette.All.Count);
            Assert.Equal(10, Palette.All.Select(c => c.Rgb).Distinct().Count());
            Assert.Equal(10, Palette.All.Select(c => c.Col * 10 + c.Row).Distinct().Count());
            Assert.Equal(0x060606, Palette.All[0].Rgb); // the program's black is not #000000
            Assert.Equal(7, Palette.IndexOf(0x7FFFDD00)); // alpha ignored
            Assert.Equal(-1, Palette.IndexOf(0x000000));
        }
    }

    public class QuantizerTests
    {
        static Raster PaletteStripes()
        {
            var r = new Raster(BoardGeometry.Width, BoardGeometry.Height);
            for (int i = 0; i < r.Pixels.Length; i++)
                r.Pixels[i] = unchecked((int)0xFF000000) | Palette.All[(i % 320) / 32].Rgb;
            return r;
        }

        [Theory]
        [InlineData(DitherKind.None)]
        [InlineData(DitherKind.FloydSteinberg)]
        [InlineData(DitherKind.Atkinson)]
        public void PaletteColoursMapToThemselves(DitherKind d)
        {
            var q = Quantizer.Quantize(PaletteStripes(), new QuantizeOptions { Dither = d });
            for (int i = 0; i < q.Index.Length; i++) Assert.Equal((i % 320) / 32, q.Index[i]);
        }

        [Fact]
        public void DisabledColoursAreNeverUsed()
        {
            var opt = new QuantizeOptions();
            opt.Enabled[9] = false; // 茶
            opt.Enabled[3] = false; // 桃
            var q = Quantizer.Quantize(Gradient(), opt);
            var h = q.Histogram();
            Assert.Equal(0, h[9]);
            Assert.Equal(0, h[3]);
        }

        [Fact]
        public void TransparentPixelsTakeTheBackground()
        {
            var r = new Raster(320, 240); // all 0 = transparent
            var q = Quantizer.Quantize(r, new QuantizeOptions { Background = 0x00BB09 });
            Assert.All(q.Index, b => Assert.Equal(8, b));
        }

        [Fact]
        public void DitheringMixesColoursForInBetweenTones()
        {
            var r = new Raster(320, 240);
            for (int i = 0; i < r.Pixels.Length; i++) r.Pixels[i] = unchecked((int)0xFF7F7F7F) - 0x202020; // dark grey
            var plain = Quantizer.Quantize(r, new QuantizeOptions { Dither = DitherKind.None }).Histogram();
            var fs = Quantizer.Quantize(r, new QuantizeOptions { Dither = DitherKind.FloydSteinberg }).Histogram();
            Assert.Equal(1, plain.Count(n => n > 0));
            Assert.True(fs.Count(n => n > 0) >= 2);
        }

        internal static Raster Gradient()
        {
            var r = new Raster(320, 240);
            for (int y = 0; y < 240; y++)
                for (int x = 0; x < 320; x++)
                {
                    int red = x * 255 / 319, green = y * 255 / 239, blue = (x + y) * 255 / 558;
                    r[x, y] = unchecked((int)0xFF000000) | red << 16 | green << 8 | blue;
                }
            return r;
        }
    }

    public class ImageFitTests
    {
        [Fact]
        public void ContainLetterboxes()
        {
            var f = ImageFit.Compute(640, 240, 320, 240, FitMode.Contain);
            Assert.Equal((0, 60, 320, 120), (f.DstX, f.DstY, f.DstW, f.DstH));
        }

        [Fact]
        public void CoverCutsTheMiddle()
        {
            var f = ImageFit.Compute(640, 240, 320, 240, FitMode.Cover);
            Assert.Equal((0, 0, 320, 240), (f.DstX, f.DstY, f.DstW, f.DstH));
            Assert.Equal(320, f.SrcW, 3);
            Assert.Equal(160, f.SrcX, 3);
            Assert.Equal(240, f.SrcH, 3);
        }

        [Fact]
        public void StretchUsesEverything()
        {
            var f = ImageFit.Compute(100, 100, 320, 240, FitMode.Stretch);
            Assert.Equal((0.0, 0.0, 100.0, 100.0), (f.SrcX, f.SrcY, f.SrcW, f.SrcH));
            Assert.Equal((320, 240), (f.DstW, f.DstH));
        }
    }

    public class StrokePlannerTests
    {
        static IndexedImage Random(int seed, int colours, double noise)
        {
            var rnd = new Random(seed);
            var img = new IndexedImage(320, 240);
            // blobs + noise, like a dithered picture
            for (int y = 0; y < 240; y++)
                for (int x = 0; x < 320; x++)
                {
                    int c = ((x / 40) + (y / 30) * 3) % colours;
                    if (rnd.NextDouble() < noise) c = rnd.Next(colours);
                    img[x, y] = (byte)c;
                }
            return img;
        }

        static void AssertReproduces(DrawPlan plan, IndexedImage target, int[] board)
        {
            StrokePlanner.Apply(plan, board, 320);
            Assert.Equal(0, StrokePlanner.CountWrong(target, board));
        }

        [Theory]
        [InlineData(1, 2, 0.0)]
        [InlineData(2, 5, 0.05)]
        [InlineData(3, 10, 0.3)]
        [InlineData(4, 10, 1.0)]
        public void FullPlanReproducesTheTarget(int seed, int colours, double noise)
        {
            var t = Random(seed, colours, noise);
            var plan = StrokePlanner.PlanFull(t);
            var board = Enumerable.Repeat(0x123456, 320 * 240).ToArray();
            AssertReproduces(plan, t, board);
        }

        [Fact]
        public void QuantizedGradientIsReproduced()
        {
            var t = Quantizer.Quantize(QuantizerTests.Gradient(), new QuantizeOptions());
            var plan = StrokePlanner.PlanFull(t);
            AssertReproduces(plan, t, new int[320 * 240]);
        }

        [Fact]
        public void StrokesAreStraightInsideTheBoardAndEndOnTheirColour()
        {
            var t = Random(5, 10, 0.2);
            var plan = StrokePlanner.PlanFull(t);
            foreach (var p in plan.Passes)
                foreach (var s in p.Strokes)
                {
                    Assert.True(s.X0 == s.X1 || s.Y0 == s.Y1);
                    Assert.InRange(s.X0, 0, 319); Assert.InRange(s.X1, 0, 319);
                    Assert.InRange(s.Y0, 0, 239); Assert.InRange(s.Y1, 0, 239);
                    // the fence waits for the end pixel: it must be a pixel of this colour
                    Assert.Equal(p.Color, t[s.X1, s.Y1]);
                    Assert.Equal(p.Color, t[s.X0, s.Y0]);
                }
        }

        [Fact]
        public void SolidImageIsJustAClear()
        {
            var t = new IndexedImage(320, 240);
            for (int i = 0; i < t.Index.Length; i++) t.Index[i] = 6;
            var plan = StrokePlanner.PlanFull(t);
            Assert.Equal(6, plan.ClearColor);
            Assert.Equal(0, plan.StrokeCount);
        }

        [Fact]
        public void LaterColoursAreBridged()
        {
            // a red line interrupted by single blue dots: red is drawn first as one stroke per row, blue dots after
            var t = new IndexedImage(320, 240);
            for (int i = 0; i < t.Index.Length; i++) t.Index[i] = Palette.White;
            for (int x = 10; x < 300; x++) t[x, 100] = 4;
            for (int x = 20; x < 300; x += 20) t[x, 100] = 6;
            var plan = StrokePlanner.PlanFull(t);
            Assert.Equal(Palette.White, plan.ClearColor);
            Assert.Equal(new[] { 4, 6 }, plan.Passes.Select(p => p.Color).ToArray());
            Assert.Single(plan.Passes[0].Strokes);
            Assert.Equal(14, plan.Passes[1].Strokes.Count);
        }

        [Fact]
        public void PlanFromKeepsCorrectPixels()
        {
            var t = Random(6, 6, 0.1);
            var board = new int[320 * 240];
            StrokePlanner.Apply(StrokePlanner.PlanFull(t), board, 320);
            // spoil a few pixels
            board[1000] = 0x111111; board[20000] = 0x222222; board[76799] = 0x333333;
            var fix = StrokePlanner.PlanFrom(t, board, true);
            Assert.Equal(-1, fix.ClearColor);
            Assert.True(fix.StrokeCount <= 3);
            AssertReproduces(fix, t, board);
        }

        [Fact]
        public void VerticalRunsAreChosenForVerticalStripes()
        {
            var t = new IndexedImage(320, 240);
            for (int y = 0; y < 240; y++)
                for (int x = 0; x < 320; x++) t[x, y] = (byte)(x % 2 == 0 ? 0 : 2);
            var plan = StrokePlanner.PlanFull(t);
            Assert.Single(plan.Passes);
            Assert.True(plan.Passes[0].Vertical);
            Assert.Equal(160, plan.StrokeCount);
        }
    }

    public class BoardLocatorTests
    {
        [Fact]
        public void FindsTheFakeEditor()
        {
            var fake = new FakeEditor();
            var hits = BoardLocator.Find(fake.Screen);
            Assert.Single(hits);
            Assert.Equal(fake.Origin.X, hits[0].X);
            Assert.Equal(fake.Origin.Y, hits[0].Y);
        }
    }

    public class DrawSessionTests
    {
        static IndexedImage Target() => Quantizer.Quantize(QuantizerTests.Gradient(), new QuantizeOptions());

        [Fact]
        public void DrawsTheWholePictureExactly()
        {
            var fake = new FakeEditor();
            var t = Target();
            var r = new DrawSession(fake, fake.Origin, t, new DrawSettings()).Run(StrokePlanner.PlanFull(t));
            Assert.Null(r.InterruptReason);
            Assert.True(r.Completed);
            Assert.Equal(0, r.WrongPixels);
            Assert.Equal(0, StrokePlanner.CountWrong(t, fake.Canvas()));
            Assert.Equal("pen1", fake.Events[0]);
            Assert.Equal(0, r.RetouchRounds);
        }

        [Fact]
        public void LostEventsAreRedrawnAfterReadingBack()
        {
            var fake = new FakeEditor { DropEveryNthRelease = 37 };
            var t = Target();
            var r = new DrawSession(fake, fake.Origin, t, new DrawSettings()).Run(StrokePlanner.PlanFull(t));
            Assert.True(r.Completed);
            Assert.True(r.RetouchRounds >= 1);
            Assert.Equal(0, StrokePlanner.CountWrong(t, fake.Canvas()));
        }

        [Fact]
        public void InterruptStopsAndResumeFinishes()
        {
            var fake = new FakeEditor();
            var t = Target();
            int checks = 0;
            fake.Interrupt = () => ++checks > 500 ? "Esc" : null;
            var r = new DrawSession(fake, fake.Origin, t, new DrawSettings()).Run(StrokePlanner.PlanFull(t));
            Assert.Equal("Esc", r.InterruptReason);
            Assert.False(r.Completed);
            Assert.True(StrokePlanner.CountWrong(t, fake.Canvas()) > 0);

            fake.Interrupt = () => null;
            var again = new DrawSession(fake, fake.Origin, t, new DrawSettings()).Run(null);
            Assert.True(again.Completed);
            Assert.Equal(0, StrokePlanner.CountWrong(t, fake.Canvas()));
        }

        [Fact]
        public void StopsWhenTheEditorIsGone()
        {
            var fake = new FakeEditor();
            var t = Target();
            int checks = 0;
            fake.Interrupt = () =>
            {
                if (++checks == 300) fake.Covered = true; // another window covers the game half-way
                return null;
            };
            var r = new DrawSession(fake, fake.Origin, t, new DrawSettings { FenceTimeoutMs = 0, PressTimeoutMs = 0 }).Run(StrokePlanner.PlanFull(t));
            Assert.NotNull(r.InterruptReason);
            Assert.Contains("見つかりません", r.InterruptReason);
        }

        [Fact]
        public void StopsWhenTheScreenDoesNotFollowTheDrawing()
        {
            var fake = new FakeEditor();
            fake.FrozenCanvas = fake.Canvas(); // white forever on screen
            var t = Target();
            var r = new DrawSession(fake, fake.Origin, t, new DrawSettings { FenceTimeoutMs = 0 }).Run(StrokePlanner.PlanFull(t));
            Assert.NotNull(r.InterruptReason);
            Assert.Contains("画面が描画に追いついていません", r.InterruptReason);
        }

        [Fact]
        public void NormalizedCoordinatesHitExactPixels()
        {
            foreach (int size in new[] { 1920, 2560, 3840, 1366, 5120 + 1920 })
                for (int v = 0; v < size; v += 7)
                {
                    int n = InputDriver.Normalize(v, size);
                    Assert.Equal(v, (int)((long)n * size / 65536));
                }
        }
    }
}
