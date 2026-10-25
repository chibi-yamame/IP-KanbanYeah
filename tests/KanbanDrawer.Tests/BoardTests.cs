using System;
using System.IO;
using System.Linq;
using KanbanDrawer.Core;
using KanbanDrawer.Core.Board;
using KanbanDrawer.Win;
using Xunit;

namespace KanbanDrawer.Tests
{
    /// <summary>Text as filled boxes: each character a block of Size/2 x Size (outline one pixel around).</summary>
    internal sealed class BlockText : ITextRasterizer, IRenderServices
    {
        public ITextRasterizer Text => this;

        public void Measure(TextObject t, out double width, out double height)
        {
            width = t.Text.Length * t.Size / 2;
            height = t.Size;
        }

        public void Rasterize(TextObject t, byte[] mask, int w, int h)
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    t.PageToLocal(x + 0.5, y + 0.5, out double lx, out double ly);
                    double hw = t.LocalW / 2, hh = t.LocalH / 2;
                    if (Math.Abs(lx) <= hw && Math.Abs(ly) <= hh) mask[y * w + x] = 1;
                    else if (t.Outline >= 0 && Math.Abs(lx) <= hw + 1 && Math.Abs(ly) <= hh + 1) mask[y * w + x] = 2;
                }
        }
    }

    public class CompositorTests
    {
        static Raster Solid(int w, int h, int argb)
        {
            var r = new Raster(w, h);
            for (int i = 0; i < r.Pixels.Length; i++) r.Pixels[i] = argb;
            return r;
        }

        [Fact]
        public void BackgroundAndFrameAreFixed()
        {
            var doc = new BoardDoc { BackgroundColor = 6 };
            var c = Compositor.Build(doc, new BlockText());
            Assert.All(c.Fixed, f => Assert.Equal(6, f));

            var frame = new IndexedImage(320, 240);
            for (int i = 0; i < frame.Index.Length; i++) frame.Index[i] = (byte)(i % 10);
            doc.Frame = frame;
            var q = Quantizer.Quantize(Compositor.Build(doc, new BlockText()), new QuantizeOptions());
            Assert.Equal(frame.Index, q.Index); // frames come out exactly as registered
        }

        [Fact]
        public void DitheringOnThePictureDoesNotLeakIntoTheFrame()
        {
            var frame = new IndexedImage(320, 240);
            for (int i = 0; i < frame.Index.Length; i++) frame.Index[i] = Palette.White;
            // a grey picture covering the left half only: dithers into black/grey/white
            var pic = new Raster(320, 240);
            for (int y = 0; y < 240; y++)
                for (int x = 0; x < 160; x++) pic[x, y] = unchecked((int)0xFF707070);
            var doc = new BoardDoc { Frame = frame, Picture = pic };
            var q = Quantizer.Quantize(Compositor.Build(doc, null), new QuantizeOptions { Dither = DitherKind.FloydSteinberg });
            for (int y = 0; y < 240; y++)
                for (int x = 160; x < 320; x++) Assert.Equal(Palette.White, q[x, y]);
            Assert.True(Enumerable.Range(0, 160).Select(x => q[x, 100]).Distinct().Count() >= 2);
        }

        [Fact]
        public void TransparentPictureShowsTheBackground()
        {
            var doc = new BoardDoc { BackgroundColor = 8, Picture = new Raster(320, 240) };
            var q = Quantizer.Quantize(Compositor.Build(doc, null), new QuantizeOptions());
            Assert.All(q.Index, b => Assert.Equal(8, b));
        }

        [Fact]
        public void TextIsFixedInItsColourWithOutline()
        {
            var doc = new BoardDoc();
            doc.Picture = Solid(320, 240, unchecked((int)0xFF808080)); // under the text, gets dithered
            var t = new TextObject { Text = "AB", Size = 20, Color = 4, Outline = 0, Cx = 160, Cy = 120 };
            var services = new BlockText();
            t.Measure(services);
            doc.Objects.Add(t);
            var q = Quantizer.Quantize(Compositor.Build(doc, services), new QuantizeOptions());
            Assert.Equal(4, q[160, 120]);  // letter
            Assert.Equal(4, q[151, 111]);  // inside the 20x20 block (x 150..170, y 110..130)
            Assert.Equal(0, q[160, 109]);  // outline row above the block
        }

        [Fact]
        public void StampsAreTransformed()
        {
            // 20x10 stamp: left half red, right half blue
            var pic = new Raster(20, 10);
            for (int y = 0; y < 10; y++)
                for (int x = 0; x < 20; x++) pic[x, y] = x < 10 ? unchecked((int)0xFFFF0500) : unchecked((int)0xFF0205CA);
            var s = new StampObject("s", pic) { Cx = 100, Cy = 100, Scale = 2 };
            var doc = new BoardDoc();
            doc.Objects.Add(s);
            var q = Quantizer.Quantize(Compositor.Build(doc, null), new QuantizeOptions { Dither = DitherKind.None });
            Assert.Equal(4, q[85, 100]);   // red, 40 px wide at scale 2
            Assert.Equal(6, q[115, 100]);  // blue
            Assert.Equal(Palette.White, q[125, 100]); // outside
            // rotated 90° clockwise: left half (red) ends up on top
            s.Rot = Math.PI / 2;
            q = Quantizer.Quantize(Compositor.Build(doc, null), new QuantizeOptions { Dither = DitherKind.None });
            Assert.Equal(4, q[100, 85]);
            Assert.Equal(6, q[100, 115]);
            Assert.Equal(Palette.White, q[115, 100]);
            Assert.True(s.Hit(100, 85));
            Assert.False(s.Hit(115, 100));
        }

        [Fact]
        public void StampTransparencyIsHonouredForPicking()
        {
            var pic = new Raster(10, 10); // transparent except the centre
            pic[5, 5] = unchecked((int)0xFFFF0000);
            var s = new StampObject("s", pic) { Cx = 50, Cy = 50 };
            Assert.True(s.Hit(50.5, 50.5));
            Assert.False(s.Hit(46, 46));
        }
    }

    public class EditingTests
    {
        static StampObject Stamp(double cx = 100, double cy = 100) =>
            new StampObject("s", new Raster(40, 20, Enumerable.Repeat(unchecked((int)0xFFFF0000), 800).ToArray())) { Cx = cx, Cy = cy };

        [Fact]
        public void HandlesAreAtTheCornersAndTheKnobAbove()
        {
            var s = Stamp();
            Assert.Equal(Handle.ScaleTL, Handles.Hit(s, 80, 90));
            Assert.Equal(Handle.ScaleBR, Handles.Hit(s, 120, 110));
            Assert.Equal(Handle.Rotate, Handles.Hit(s, 100, 90 - Handles.RotateOffset));
            Assert.Equal(Handle.Body, Handles.Hit(s, 100, 100));
            Assert.Equal(Handle.None, Handles.Hit(s, 200, 200));
        }

        [Fact]
        public void KnobMovesBelowWhenItWouldLeaveTheBoard()
        {
            var s = Stamp(100, 12); // top edge at y=2: the knob would be at -12
            var p = Handles.Points(s);
            Assert.True(p[9] > 22);
        }

        [Fact]
        public void DragsMoveScaleAndRotate()
        {
            var s = Stamp();
            var move = new Drag(s, Handle.Body, 100, 100);
            move.Update(130, 90, false);
            Assert.Equal((130.0, 90.0), (s.Cx, s.Cy));

            s = Stamp();
            var scale = new Drag(s, Handle.ScaleBR, 120, 110);
            scale.Update(140, 120, false); // twice as far from the centre
            Assert.Equal(2, s.Scale, 6);

            s = Stamp();
            var rot = new Drag(s, Handle.Rotate, 100, 50);
            rot.Update(150, 100, false); // a quarter turn clockwise around (100,100)
            Assert.Equal(Math.PI / 2, s.Rot, 6);
            rot.Update(150, 95, true);   // snapped to 15°
            Assert.Equal(Math.PI / 2 - Math.PI / 12 * Math.Round(Math.Atan2(5, 50) / (Math.PI / 12)), s.Rot, 6);
        }

        [Fact]
        public void ScaleIsClamped()
        {
            var s = Stamp();
            var d = new Drag(s, Handle.ScaleBR, 120, 110);
            d.Update(100.01, 100.01, false);
            Assert.True(s.PageSize >= BoardObject.MinSize - 1e-9);
        }

        [Fact]
        public void UndoRedoRestoresObjectsBackgroundAndSelection()
        {
            var doc = new BoardDoc();
            var s = Stamp();
            doc.Add(s);
            doc.Checkpoint();
            s.Cx = 10;
            doc.Checkpoint();
            doc.BackgroundColor = 5;
            Assert.True(doc.Undo());
            Assert.Equal(Palette.White, doc.BackgroundColor);
            Assert.Equal(10, doc.Objects[0].Cx);
            Assert.True(doc.Undo());
            Assert.Equal(100, doc.Objects[0].Cx);
            Assert.Same(doc.Objects[0], doc.Selected);
            Assert.True(doc.Undo());
            Assert.Empty(doc.Objects);
            Assert.Null(doc.Selected);
            Assert.False(doc.Undo());
            Assert.True(doc.Redo());
            Assert.Single(doc.Objects);
        }

        [Fact]
        public void RestackAndHitTestUseTheZOrder()
        {
            var doc = new BoardDoc();
            var a = Stamp();
            var b = Stamp();
            doc.Add(a);
            doc.Add(b);
            Assert.Same(b, doc.HitTest(100, 100));
            doc.Restack(b, -1);
            Assert.Same(a, doc.HitTest(100, 100));
        }
    }

    public class StampPrepTests
    {
        [Fact]
        public void BackgroundIsRemovedAndTheRestTrimmed()
        {
            // white picture with a red 10x10 square and a white hole inside it
            var r = new Raster(50, 40);
            for (int i = 0; i < r.Pixels.Length; i++) r.Pixels[i] = unchecked((int)0xFFFFFFFF);
            for (int y = 10; y < 20; y++)
                for (int x = 20; x < 30; x++) r[x, y] = unchecked((int)0xFFFF0000);
            r[25, 15] = unchecked((int)0xFFFFFFFF); // not connected to the border: stays
            var s = StampPrep.Prepare(r, true);
            Assert.Equal((10, 10), (s.Width, s.Height));
            Assert.Equal(unchecked((int)0xFFFFFFFF), s[5, 5]);
            Assert.Equal(unchecked((int)0xFFFF0000), s[0, 0]);
        }

        [Fact]
        public void OnlyPicturesUpToTheBoardSizeFit()
        {
            Assert.True(StampPrep.FitsBoard(new Raster(320, 240)));
            Assert.False(StampPrep.FitsBoard(new Raster(321, 10)));
            Assert.False(StampPrep.FitsBoard(new Raster(10, 241)));
            // kept at its own pixels: trimming may make a big picture fit
            var r = new Raster(1024, 256);
            for (int y = 100; y < 150; y++) for (int x = 500; x < 600; x++) r[x, y] = unchecked((int)0xFF00FF00);
            var s = StampPrep.Prepare(r, false);
            Assert.Equal((100, 50), (s.Width, s.Height));
            Assert.True(StampPrep.FitsBoard(s));
        }
    }

    public class LibraryTests
    {
        [Fact]
        public void AddLoadDeleteAndSkipBrokenLines()
        {
            string root = Path.Combine(Path.GetTempPath(), "kd-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var lib = new StampLibrary(root);
                Assert.Empty(lib.Ids());
                var pic = new Raster(3, 2, new[] { unchecked((int)0xFFFF0000), 0, 0x7F00FF00, 1, 2, 3 });
                string a = lib.Add(pic);
                string b = lib.Add(pic);
                Assert.Equal(new[] { a, b }, lib.Ids());
                Assert.Equal(pic.Pixels[0], lib.Load(a).Pixels[0]);
                File.AppendAllText(Path.Combine(root, "stamps", "index.txt"), "missing\n..\\evil\n\n");
                Assert.Equal(new[] { a, b }, lib.Ids());
                lib.Delete(a);
                Assert.Equal(new[] { b }, lib.Ids());
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        }

        [Fact]
        public void FramesComeBackAsPaletteIndices()
        {
            string root = Path.Combine(Path.GetTempPath(), "kd-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var lib = new FrameLibrary(root);
                var img = new IndexedImage(320, 240);
                for (int i = 0; i < img.Index.Length; i++) img.Index[i] = (byte)((i / 7) % 10);
                string id = lib.Add(img.ToRaster());
                Assert.Equal(img.Index, lib.LoadIndexed(id).Index);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { }
            }
        }
    }
}

namespace KanbanDrawer.Tests
{
    public class GdiTextTests
    {
        [Fact]
        public void TextIsBinaryWithOutlineAroundTheLetters()
        {
            var r = new GdiTextRasterizer();
            var t = new TextObject { Text = "看板A", Font = GdiTextRasterizer.DefaultFont(), Size = 40, Color = 4, Outline = 0, OutlineWidth = 2, Cx = 160, Cy = 120 };
            t.Measure(r);
            Assert.InRange(t.LocalW, 60, 200);
            Assert.InRange(t.LocalH, 25, 70);
            var mask = new byte[320 * 240];
            r.Rasterize(t, mask, 320, 240);
            int fill = mask.Count(m => m == 1), outline = mask.Count(m => m == 2);
            Assert.True(fill > 200, "letters drawn");
            Assert.True(outline > 100, "outline drawn");
            Assert.All(mask, m => Assert.InRange(m, (byte)0, (byte)2));
            // everything stays inside the measured box (+1 px)
            for (int i = 0; i < mask.Length; i++)
                if (mask[i] != 0) Assert.True(t.InsideBox(i % 320 + 0.5, i / 320 + 0.5, 1.5), $"pixel {i % 320},{i / 320} outside");
        }

        [Fact]
        public void RenderingIsFastEnoughForDragging()
        {
            var doc = new BoardDoc();
            doc.Picture = new Raster(320, 240, Enumerable.Range(0, 76800).Select(i => unchecked((int)0xFF000000) | (i * 2654435761 % 0xFFFFFF > 0 ? (int)(i * 2654435761u % 0xFFFFFF) : 0)).ToArray());
            var text = new GdiTextRasterizer();
            var t = new TextObject { Text = "テスト", Font = GdiTextRasterizer.DefaultFont(), Size = 30, Cx = 100, Cy = 60 };
            t.Measure(text);
            doc.Objects.Add(t);
            var opt = new QuantizeOptions();
            Quantizer.Quantize(Compositor.Build(doc, text), opt); // warm up the colour table
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 5; i++) Quantizer.Quantize(Compositor.Build(doc, text), opt);
            Assert.True(sw.ElapsedMilliseconds / 5 < 120, $"{sw.ElapsedMilliseconds / 5} ms per frame");
        }
    }
}

namespace KanbanDrawer.Tests
{
    public class OfficialTextTests
    {
        const string Mixed = "看板あア、。「」ｱｲｳ ＡＢ１２★♪→〒\nInterPot 0123456789 !\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~\néü©°±×÷ αβγ ДЖЯ ‘’“” €";

        /// <summary>With the Java that opens .jar files (skipped when there is none, e.g. on CI), the tool's text is the program's, at every size.</summary>
        [Fact]
        public void SameAsTheProgramsTextToolAtEverySize()
        {
            var oracle = KanbanTextOracle.Start();
            if (oracle == null) return; // no Java for .jar files on this PC
            using (oracle)
                for (int size = OfficialText.MinSize; size <= OfficialText.MaxSize; size++)
                {
                    var java = oracle.Render(Mixed, size);
                    var tool = OfficialText.Render(Mixed, size);
                    Assert.True(OfficialText.Exact);
                    Assert.Equal((java.Width, java.Height), (tool.Width, tool.Height));
                    Assert.Equal(java.Bits, tool.Bits);
                }
        }

        /// <summary>Without Java the GDI approximation still gets Japanese (MS Gothic bitmap glyphs) and the layout right.</summary>
        [Fact]
        public void ApproximationMatchesJapaneseExactly()
        {
            const string japanese = "看板あア、。「」ｱｲｳ\nＡＢ１２★♪→〒テスト";
            var oracle = KanbanTextOracle.Start();
            if (oracle == null) return;
            using (oracle)
                for (int size = OfficialText.MinSize; size <= OfficialText.MaxSize; size++)
                {
                    var java = oracle.Render(japanese, size);
                    var gdi = OfficialText.Approximate(japanese, size);
                    Assert.Equal((java.Width, java.Height), (gdi.Width, gdi.Height));
                    Assert.Equal(java.Bits, gdi.Bits);
                }
        }

        [Fact]
        public void OfficialTextIsUprightUnscaledAndOnWholePixels()
        {
            var r = new GdiTextRasterizer();
            var t = new TextObject { Official = true, Text = "Abcあ", Size = 12, Color = 4, Cx = 100.4, Cy = 50.6, Rot = 1, Scale = 3 };
            t.Measure(r);
            Assert.False(t.CanRotate);
            Assert.False(t.CanScale);
            var bmp = OfficialText.Render("Abcあ", 12);
            Assert.Equal(bmp.Width, (int)t.LocalW);
            Assert.Equal(16, (int)t.LocalH);
            var mask = new byte[320 * 240];
            r.Rasterize(t, mask, 320, 240);
            Assert.Equal(bmp.Bits.Count(b => b == 1), mask.Count(m => m == 1)); // same pixels, not scaled
            Assert.Equal(Handle.Body, Handles.Hit(t, 100, 50));
            var p = Handles.Points(t);
            Assert.NotEqual(Handle.ScaleTL, Handles.Hit(t, p[0], p[1]));
        }

        [Fact]
        public void OutlineGrowsTheLetters()
        {
            var r = new GdiTextRasterizer();
            var t = new TextObject { Official = true, Text = "あ", Size = 12, Color = 0, Outline = 2, OutlineWidth = 1, Cx = 50, Cy = 50 };
            t.Measure(r);
            var mask = new byte[320 * 240];
            r.Rasterize(t, mask, 320, 240);
            Assert.True(mask.Count(m => m == 2) > mask.Count(m => m == 1));
            for (int i = 0; i < mask.Length; i++)
                if (mask[i] == 2)
                {
                    int x = i % 320, y = i / 320;
                    bool nearLetter = false;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) if (mask[(y + dy) * 320 + x + dx] == 1) nearLetter = true;
                    Assert.True(nearLetter);
                }
        }
    }
}

namespace KanbanDrawer.Tests
{
    /// <summary>The program's text renderer (KanbanText, the embedded jar) on the Java that opens .jar files.</summary>
    internal sealed class KanbanTextOracle : System.IDisposable
    {
        readonly System.Diagnostics.Process p;
        KanbanTextOracle(System.Diagnostics.Process p) { this.p = p; }

        public static KanbanTextOracle Start()
        {
            string java = JavaHelper.FindJava();
            string jar = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Data", "kanban-text.jar");
            if (java == null || !System.IO.File.Exists(jar)) return null;
            var psi = new System.Diagnostics.ProcessStartInfo(java, $"-Djava.awt.headless=true -cp \"{jar}\" KanbanText")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true,
            };
            return new KanbanTextOracle(System.Diagnostics.Process.Start(psi));
        }

        public TextBitmap Render(string text, int size)
        {
            p.StandardInput.WriteLine($"{size} 0 {System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text))}");
            p.StandardInput.Flush();
            var parts = p.StandardOutput.ReadLine().Split(' ');
            return new TextBitmap(int.Parse(parts[0]), int.Parse(parts[1]), System.Convert.FromBase64String(parts[2]));
        }

        public void Dispose()
        {
            try { p.StandardInput.Close(); if (!p.WaitForExit(2000)) p.Kill(); } catch (System.Exception) { }
        }
    }
}
