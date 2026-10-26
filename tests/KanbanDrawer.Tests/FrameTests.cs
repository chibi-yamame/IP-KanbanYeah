using System;
using System.IO;
using System.Linq;
using KanbanDrawer.Core;
using KanbanDrawer.Core.Board;
using KanbanDrawer.Win;
using Xunit;

namespace KanbanDrawer.Tests
{
    /// <summary>Frames with transparency (M8b): registration, library round trip, compositing over the background colour.</summary>
    public class FrameTests
    {
        const int W = BoardDoc.W, H = BoardDoc.H;

        /// <summary>Left half transparent, right half a pattern of palette colours.</summary>
        static IndexedImage HalfTransparentFrame()
        {
            var f = new IndexedImage(W, H);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    f[x, y] = x < W / 2 ? IndexedImage.Transparent : (byte)((x + y) % Palette.Count);
            return f;
        }

        static string TempRoot() => Path.Combine(Path.GetTempPath(), "kd-test-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public void TransparentPixelsBecomeAlphaZeroAndAreNotCounted()
        {
            var f = HalfTransparentFrame();
            var r = f.ToRaster();
            Assert.Equal(0, r[0, 0]);
            Assert.Equal(255, (r[W - 1, 0] >> 24) & 255);
            Assert.Equal(W * H / 2, f.Histogram().Sum());
        }

        [Fact]
        public void TransparentFrameRoundTripsThroughTheLibrary()
        {
            string root = TempRoot();
            try
            {
                var lib = new FrameLibrary(root);
                var f = HalfTransparentFrame();
                string id = lib.Add(f.ToRaster());
                Assert.Equal(f.Index, lib.LoadIndexed(id).Index);
                // the thumbnail path (plain Load) keeps the alpha too
                var r = lib.Load(id);
                Assert.Equal(0, (r[0, 0] >> 24) & 255);
                Assert.Equal(255, (r[W - 1, 0] >> 24) & 255);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        }

        [Fact]
        public void BackgroundShowsOnlyThroughTransparentFramePixels()
        {
            var f = HalfTransparentFrame();
            var doc = new BoardDoc { Frame = f, BackgroundColor = 4 };
            var a = Compositor.BuildBase(doc);
            doc.BackgroundColor = 6;
            var b = Compositor.BuildBase(doc);
            for (int i = 0; i < W * H; i++)
            {
                if (f.Index[i] == IndexedImage.Transparent)
                {
                    Assert.Equal(4, a.Fixed[i]);
                    Assert.Equal(6, b.Fixed[i]);
                }
                else
                {
                    Assert.Equal((int)f.Index[i], a.Fixed[i]);
                    Assert.Equal((int)f.Index[i], b.Fixed[i]);
                }
            }
            // what gets quantized and drawn never contains the transparent index
            var q = Quantizer.Quantize(b, new QuantizeOptions());
            Assert.DoesNotContain(IndexedImage.Transparent, q.Index);
            Assert.NotNull(StrokePlanner.PlanFull(q));
        }

        [Fact]
        public void CacheRebuildsWhenTheBackgroundColourChanges()
        {
            var doc = new BoardDoc { Frame = HalfTransparentFrame(), BackgroundColor = 4 };
            var cache = new CompositeCache();
            var services = new BlockText();
            Assert.Equal(4, cache.Build(doc, services).Fixed[0]);
            doc.BackgroundColor = 8;
            Assert.Equal(8, cache.Build(doc, services).Fixed[0]);
            Assert.Equal((sbyte)((W - 1) % Palette.Count), cache.Build(doc, services).Fixed[W - 1]);
        }

        [Fact]
        public void RegistrationKeepsTransparencyAndIgnoresTheBackgroundColour()
        {
            var src = new Raster(W, H);
            var rnd = new Random(1);
            for (int i = 0; i < src.Pixels.Length; i++)
            {
                int a = rnd.Next(256);
                src.Pixels[i] = a << 24 | rnd.Next(1 << 24);
            }
            foreach (var dither in new[] { DitherKind.None, DitherKind.FloydSteinberg, DitherKind.Atkinson, DitherKind.Bayer4 })
            {
                var white = new QuantizeOptions { Dither = dither, Background = 0xFFFFFF };
                var black = new QuantizeOptions { Dither = dither, Background = 0x000000 };
                var q1 = Quantizer.QuantizeFrame(src, white);
                var q2 = Quantizer.QuantizeFrame(src, black);
                Assert.Equal(q1.Index, q2.Index);
                for (int i = 0; i < src.Pixels.Length; i++)
                {
                    bool transparent = ((src.Pixels[i] >> 24) & 255) < Quantizer.FrameAlphaThreshold;
                    Assert.Equal(transparent, q1.Index[i] == IndexedImage.Transparent);
                    if (!transparent) Assert.True(q1.Index[i] < Palette.Count);
                }
            }
        }

        [Fact]
        public void RegistrationUsesTheColourWithoutTheAlpha()
        {
            var src = new Raster(W, H);
            int red = Palette.All[4].Rgb, blue = Palette.All[6].Rgb;
            for (int i = 0; i < src.Pixels.Length; i++)
                src.Pixels[i] = (i % 3 == 0 ? 128 : i % 3 == 1 ? 127 : 255) << 24 | (i % 2 == 0 ? red : blue);
            var q = Quantizer.QuantizeFrame(src, new QuantizeOptions { Dither = DitherKind.FloydSteinberg, Background = 0x000000 });
            for (int i = 0; i < src.Pixels.Length; i++)
            {
                if (i % 3 == 1) Assert.Equal(IndexedImage.Transparent, q.Index[i]);
                else Assert.Equal(i % 2 == 0 ? 4 : 6, q.Index[i]);
            }
        }

        [Fact]
        public void ContainMarginsStayTransparentAfterRegistration()
        {
            // a picture placed with 全体を収める: opaque in the middle, transparent margins left and right
            var placed = new Raster(W, H);
            for (int y = 0; y < H; y++)
                for (int x = 40; x < W - 40; x++)
                    placed[x, y] = unchecked((int)0xFF000000) | Palette.All[7].Rgb;
            string root = TempRoot();
            try
            {
                var lib = new FrameLibrary(root);
                string id = lib.Add(Quantizer.QuantizeFrame(placed, new QuantizeOptions()).ToRaster());
                var f = lib.LoadIndexed(id);
                var doc = new BoardDoc { Frame = f, BackgroundColor = 9 };
                var c = Compositor.BuildBase(doc);
                Assert.Equal(9, c.Fixed[0]);
                Assert.Equal(9, c.Fixed[W - 1]);
                Assert.Equal(7, c.Fixed[W / 2]);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        }
    }
}
