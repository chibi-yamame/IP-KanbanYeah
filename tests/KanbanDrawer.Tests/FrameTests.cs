using System;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using KanbanDrawer.Core;
using KanbanDrawer.Core.Board;
using KanbanDrawer.Win;
using Xunit;

namespace KanbanDrawer.Tests
{
    /// <summary>
    /// Frames in full colour (M9b / M9c): registration (background removal, fit), library round trip, compositing over
    /// the background colour, quantized with the draft.
    /// </summary>
    public class FrameTests
    {
        const int W = BoardDoc.W, H = BoardDoc.H;

        static string TempRoot() => Path.Combine(Path.GetTempPath(), "kd-test-" + Guid.NewGuid().ToString("N"));

        static void Delete(string root)
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }

        static int Argb(int a, int rgb) => a << 24 | (rgb & 0xFFFFFF);

        /// <summary>Left third transparent, middle third half transparent black, right third opaque full colour gradient.</summary>
        static Raster ThirdsFrame()
        {
            var f = new Raster(W, H);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    f[x, y] = x < W / 3 ? 0
                        : x < 2 * W / 3 ? Argb(128, 0x000000)
                        : Argb(255, (x * 255 / W) << 16 | (y * 255 / H) << 8 | 0x40);
            return f;
        }

        static void SavePng(Raster r, string path)
        {
            using (var bmp = ImageLoader.ToBitmap(r)) bmp.Save(path, ImageFormat.Png);
        }

        [Fact]
        public void FrameRoundTripsThroughTheLibraryInFullColourWithAlpha()
        {
            string root = TempRoot();
            try
            {
                var lib = new FrameLibrary(root);
                var f = ThirdsFrame();
                for (int x = 0; x < W; x++) f[x, 0] = Argb(x % 256, 0x123456); // every alpha level
                var r = lib.Load(lib.Add(f));
                Assert.Equal(W, r.Width);
                Assert.Equal(H, r.Height);
                for (int i = 0; i < f.Pixels.Length; i++)
                {
                    int alpha = (f.Pixels[i] >> 24) & 255;
                    Assert.Equal(alpha, (r.Pixels[i] >> 24) & 255);
                    if (alpha == 255) Assert.Equal(f.Pixels[i], r.Pixels[i]);
                    else if (alpha >= 128) // GDI+ goes through premultiplied colour: close, not exact
                        for (int sh = 0; sh < 24; sh += 8)
                            Assert.InRange(((r.Pixels[i] >> sh) & 255) - ((f.Pixels[i] >> sh) & 255), -2, 2);
                }
            }
            finally { Delete(root); }
        }

        [Fact]
        public void RegistrationPlacesWithoutQuantizing()
        {
            string root = TempRoot(), file = Path.Combine(Path.GetTempPath(), "kd-frame-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                SavePng(ThirdsFrame(), file);
                var lib = new FrameLibrary(root);
                var r = lib.Load(lib.Register(file, FitMode.Stretch, false));
                Assert.Equal(W, r.Width);
                Assert.Equal(H, r.Height);
                // colours that are not in the palette are kept (more than 10 colours), alpha too
                Assert.True(r.Pixels.Where(p => ((p >> 24) & 255) == 255).Distinct().Count() > Palette.Count);
                Assert.Equal(0, (r[W / 6, H / 2] >> 24) & 255);
                Assert.InRange((r[W / 2, H / 2] >> 24) & 255, 120, 136);
                Assert.Equal(255, (r[5 * W / 6, H / 2] >> 24) & 255);
            }
            finally
            {
                Delete(root);
                try { File.Delete(file); } catch (IOException) { }
            }
        }

        [Fact]
        public void TransparentShowsTheBackgroundAndSemiTransparentMixesWithIt()
        {
            var f = ThirdsFrame();
            var doc = new BoardDoc { Frame = f, BackgroundColor = Palette.White };
            var c = Compositor.BuildBase(doc);
            int left = (H / 2) * W + W / 6, mid = (H / 2) * W + W / 2, right = (H / 2) * W + 5 * W / 6;
            Assert.Equal(Palette.White, c.Fixed[left]);
            Assert.Equal(-1, c.Fixed[mid]);
            Assert.InRange(c.Rgb[mid * 3], 125f, 129f); // half black over white
            Assert.Equal(-1, c.Fixed[right]);
            Assert.Equal((f.Pixels[right] >> 16) & 255, c.Rgb[right * 3]);
            Assert.Equal((f.Pixels[right] >> 8) & 255, c.Rgb[right * 3 + 1]);
            Assert.Equal(f.Pixels[right] & 255, c.Rgb[right * 3 + 2]);
        }

        [Fact]
        public void FramePixelsAreQuantizedWithTheDraft()
        {
            var grey = new Raster(W, H);
            for (int i = 0; i < grey.Pixels.Length; i++) grey.Pixels[i] = Argb(255, 0x707070);
            var doc = new BoardDoc { Frame = grey, BackgroundColor = 6 };
            var c = Compositor.Build(doc, new BlockText());
            Assert.All(c.Fixed, v => Assert.Equal(-1, v));
            // dithered into several colours
            var q = Quantizer.Quantize(c, new QuantizeOptions { Dither = DitherKind.FloydSteinberg });
            Assert.True(q.Index.Distinct().Count() >= 2);
            // and adjusted like pictures (brightness)
            var bright = Quantizer.Quantize(c, new QuantizeOptions { Dither = DitherKind.None, Brightness = 100 });
            Assert.All(bright.Index, b => Assert.Equal(Palette.White, b));
        }

        [Fact]
        public void CacheRebuildsWhenTheBackgroundColourOrFrameChanges()
        {
            var doc = new BoardDoc { Frame = ThirdsFrame(), BackgroundColor = 4 };
            var cache = new CompositeCache();
            var services = new BlockText();
            int mid = (H / 2) * W + W / 2;
            var a = cache.Build(doc, services);
            Assert.Equal(4, a.Fixed[0]);
            doc.BackgroundColor = 8;
            var b = cache.Build(doc, services);
            Assert.Equal(8, b.Fixed[0]);
            Assert.NotEqual(a.Rgb[mid * 3 + 1], b.Rgb[mid * 3 + 1]); // the half transparent part mixes with the new colour
            doc.Frame = null;
            Assert.Equal(8, cache.Build(doc, services).Fixed[mid]);
        }

        [Fact]
        public void UndoRestoresTheFrame()
        {
            var doc = new BoardDoc();
            var f = ThirdsFrame();
            doc.Checkpoint();
            doc.Frame = f;
            doc.FrameId = "f1";
            Assert.True(doc.Undo());
            Assert.Null(doc.Frame);
            Assert.True(doc.Redo());
            Assert.Same(f, doc.Frame);
            Assert.Equal("f1", doc.FrameId);
        }

        [Fact]
        public void OldTenColourFramesStillLoad()
        {
            // a frame saved by an older version: opaque, already in palette colours
            var img = new IndexedImage(W, H);
            for (int i = 0; i < img.Index.Length; i++) img.Index[i] = (byte)((i / 7) % Palette.Count);
            string root = TempRoot();
            try
            {
                var lib = new FrameLibrary(root);
                var r = lib.Load(lib.Add(img.ToRaster()));
                var doc = new BoardDoc { Frame = r, BackgroundColor = 4 };
                var q = Quantizer.Quantize(Compositor.Build(doc, new BlockText()), new QuantizeOptions { Dither = DitherKind.FloydSteinberg });
                Assert.Equal(img.Index, q.Index); // exact palette colours carry no dithering error
            }
            finally { Delete(root); }
        }

        /// <summary>200x150 picture (4:3, fills the board when placed): light blue with a red block in the middle.</summary>
        static Raster PictureOnPlainBackground()
        {
            var r = new Raster(200, 150);
            for (int y = 0; y < 150; y++)
                for (int x = 0; x < 200; x++)
                    r[x, y] = x >= 50 && x < 150 && y >= 50 && y < 100 ? Argb(255, Palette.All[4].Rgb) : Argb(255, 0x80C0FF);
            return r;
        }

        [Fact]
        public void RemovingTheBackgroundShowsTheBackgroundColour()
        {
            string root = TempRoot(), file = Path.Combine(Path.GetTempPath(), "kd-frame-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                SavePng(PictureOnPlainBackground(), file);
                var lib = new FrameLibrary(root);
                var removed = lib.Load(lib.Register(file, FitMode.Contain, true));
                var kept = lib.Load(lib.Register(file, FitMode.Contain, false));

                Assert.Equal(0, (removed[10, 10] >> 24) & 255);          // edge colour region: transparent
                Assert.Equal(Argb(255, Palette.All[4].Rgb), removed[W / 2, H / 2]); // the picture stays
                var c = Compositor.BuildBase(new BoardDoc { Frame = removed, BackgroundColor = 7 });
                Assert.Equal(7, c.Fixed[10 * W + 10]);
                Assert.Equal(-1, c.Fixed[(H / 2) * W + W / 2]);

                Assert.Equal(Argb(255, 0x80C0FF), kept[10, 10]);         // off: unchanged
                Assert.Equal(-1, Compositor.BuildBase(new BoardDoc { Frame = kept, BackgroundColor = 7 }).Fixed[10 * W + 10]);
            }
            finally
            {
                Delete(root);
                try { File.Delete(file); } catch (IOException) { }
            }
        }

        [Fact]
        public void PrepareDoesNotTrim()
        {
            using (var bmp = ImageLoader.ToBitmap(PictureOnPlainBackground()))
            {
                var r = FrameLibrary.Prepare(bmp, FitMode.Contain, true);
                Assert.Equal(W, r.Width);
                Assert.Equal(H, r.Height);
            }
        }
    }
}
