using System;
using System.Linq;
using KanbanDrawer.Core;
using KanbanDrawer.Core.Board;
using Xunit;

namespace KanbanDrawer.Tests
{
    /// <summary>画像 as board objects: placement, shrinking on load, z order / undo, picking, shared rendering with stamps.</summary>
    public class PictureTests
    {
        static readonly int Red = unchecked((int)0xFFFF0500);

        static Raster Solid(int w, int h, int argb) => new Raster(w, h, Enumerable.Repeat(argb, w * h).ToArray());

        [Theory]
        [InlineData(640, 480, PictureFit.Contain, 0.5)]
        [InlineData(640, 480, PictureFit.Cover, 0.5)]
        [InlineData(640, 240, PictureFit.Contain, 0.5)]   // width limits
        [InlineData(640, 240, PictureFit.Cover, 1.0)]     // height limits
        [InlineData(100, 300, PictureFit.Contain, 0.8)]
        [InlineData(100, 300, PictureFit.Cover, 3.2)]
        [InlineData(640, 240, PictureFit.Actual, 1.0)]
        [InlineData(50, 20, PictureFit.Actual, 1.0)]
        public void PlacementScale(int w, int h, PictureFit mode, double scale)
        {
            Assert.Equal(scale, PictureObject.FitScale(w, h, mode), 9);
        }

        [Fact]
        public void FitToCentresUprightAndScales()
        {
            var o = new PictureObject("p", Solid(640, 240, Red)) { Cx = 10, Cy = 20, Rot = 1, Scale = 3 };
            o.FitTo(PictureFit.Contain);
            Assert.Equal(160, o.Cx);
            Assert.Equal(120, o.Cy);
            Assert.Equal(0, o.Rot);
            Assert.Equal(0.5, o.Scale, 9);
        }

        [Fact]
        public void ContainLeavesMarginsCoverFillsActualIsDotByDot()
        {
            var doc = new BoardDoc { BackgroundColor = Palette.White };
            var o = new PictureObject("p", Solid(640, 240, Red));
            doc.Objects.Add(o);
            var opt = new QuantizeOptions { Dither = DitherKind.None };

            o.FitTo(PictureFit.Contain); // 320x120 in the middle: y 60..180
            var q = Quantizer.Quantize(Compositor.Build(doc, null), opt);
            Assert.Equal(Palette.White, q[160, 30]);
            Assert.Equal(4, q[160, 120]);
            Assert.Equal(4, q[0, 61]);
            Assert.Equal(Palette.White, q[160, 210]);

            o.FitTo(PictureFit.Cover); // 640x240, sides cut off
            q = Quantizer.Quantize(Compositor.Build(doc, null), opt);
            Assert.All(q.Index, b => Assert.Equal(4, b));

            var small = new PictureObject("s", Solid(40, 20, Red));
            doc.Objects.Clear();
            doc.Objects.Add(small);
            small.FitTo(PictureFit.Actual); // 40x20 around the centre: x 140..180, y 110..130
            q = Quantizer.Quantize(Compositor.Build(doc, null), opt);
            Assert.Equal(4, q[140, 110]);
            Assert.Equal(4, q[179, 129]);
            Assert.Equal(Palette.White, q[139, 120]);
            Assert.Equal(Palette.White, q[180, 120]);
            Assert.Equal(Palette.White, q[160, 109]);
            Assert.Equal(Palette.White, q[160, 130]);
        }

        [Fact]
        public void LargePicturesAreShrunkTo1280()
        {
            var wide = PictureObject.Shrink(new Raster(2560, 1000));
            Assert.Equal(1280, wide.Width);
            Assert.Equal(500, wide.Height);
            var tall = PictureObject.Shrink(new Raster(1000, 3000));
            Assert.Equal(427, tall.Width);
            Assert.Equal(1280, tall.Height);
            var ok = new Raster(1280, 960);
            Assert.Same(ok, PictureObject.Shrink(ok));
        }

        [Fact]
        public void ShrinkingAveragesTheArea()
        {
            // black / white columns, 2 → 1: every pixel becomes grey
            var src = new Raster(2560, 4);
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 2560; x++) src[x, y] = x % 2 == 0 ? unchecked((int)0xFF000000) : unchecked((int)0xFFFFFFFF);
            var r = PictureObject.Shrink(src);
            Assert.Equal(1280, r.Width);
            Assert.Equal(2, r.Height);
            Assert.All(r.Pixels, p =>
            {
                Assert.Equal(255, (p >> 24) & 255);
                Assert.InRange(p & 255, 120, 135);
            });
        }

        [Fact]
        public void InsertedAtTheBackAndUndone()
        {
            var doc = new BoardDoc();
            var stamp = new StampObject("s", Solid(20, 20, Red)) { Cx = 100, Cy = 100 };
            doc.Add(stamp);
            var pic = new PictureObject("p.png", Solid(320, 240, unchecked((int)0xFF0205CA))) { Cx = 160, Cy = 120 };
            doc.Insert(0, pic);
            Assert.Same(pic, doc.Objects[0]);
            Assert.Same(stamp, doc.Objects[1]);
            Assert.Same(pic, doc.Selected);
            Assert.Same(stamp, doc.HitTest(100, 100)); // the stamp stays in front
            Assert.Same(pic, doc.HitTest(10, 10));

            Assert.True(doc.Undo());
            Assert.Single(doc.Objects);
            Assert.IsType<StampObject>(doc.Objects[0]);
            Assert.True(doc.Redo());
            Assert.Equal(2, doc.Objects.Count);
            var back = Assert.IsType<PictureObject>(doc.Objects[0]);
            Assert.Equal("p.png", back.FileName);
        }

        /// <summary>A stamp on a selected full-board picture can be clicked (the picture's body does not take the click).</summary>
        [Fact]
        public void ClickPicksTheStampInFrontOfTheSelectedPicture()
        {
            var doc = new BoardDoc();
            var stamp = new StampObject("s", Solid(20, 20, Red)) { Cx = 100, Cy = 100 };
            doc.Add(stamp);
            var pic = new PictureObject("p.png", Solid(320, 240, unchecked((int)0xFF0205CA))) { Cx = 160, Cy = 120 };
            doc.Insert(0, pic); // selected, behind the stamp

            Assert.Equal(Handle.Body, Handles.Pick(doc, 100, 100, out var target));
            Assert.Same(stamp, target);
            Assert.Equal(Handle.Body, Handles.Pick(doc, 10, 10, out target)); // elsewhere: the selected picture
            Assert.Same(pic, target);

            // the selection's handles still come first, even over an object in front
            doc.Select(stamp);
            var corner = Handles.Points(stamp);
            Assert.Equal(Handle.ScaleTL, Handles.Pick(doc, corner[0], corner[1], out target));
            Assert.Same(stamp, target);
            // an object behind the selected one does not take the selected one's body
            stamp.Scale = 3;
            Assert.Equal(Handle.Body, Handles.Pick(doc, 110, 110, out target));
            Assert.Same(stamp, target);
            // nothing there
            doc.Objects.Clear();
            doc.Select(null);
            Assert.Equal(Handle.None, Handles.Pick(doc, 10, 10, out target));
            Assert.Null(target);
        }

        [Fact]
        public void TransparentPartsAreNotPicked()
        {
            var raster = new Raster(100, 100); // left half transparent
            for (int y = 0; y < 100; y++)
                for (int x = 50; x < 100; x++) raster[x, y] = Red;
            var doc = new BoardDoc();
            doc.Objects.Add(new PictureObject("p", raster) { Cx = 160, Cy = 120 });
            Assert.Null(doc.HitTest(120, 120));
            Assert.NotNull(doc.HitTest(200, 120));
        }

        [Fact]
        public void StampsRenderAndCloneAsBefore()
        {
            var raster = new Raster(30, 20);
            for (int i = 0; i < raster.Pixels.Length; i++) raster.Pixels[i] = i % 3 == 0 ? 0 : Red | (i * 7919 & 0xFFFF);
            var stamp = new StampObject("id", raster) { Cx = 100.3, Cy = 80.7, Scale = 0.6, Rot = 0.4 };
            var pic = new PictureObject("f", raster) { Cx = 100.3, Cy = 80.7, Scale = 0.6, Rot = 0.4 };
            Composite Draw(BoardObject o)
            {
                var doc = new BoardDoc();
                doc.Objects.Add(o);
                return Compositor.Build(doc, null);
            }
            var a = Draw(stamp);
            Assert.Equal(a.Rgb, Draw(pic).Rgb); // the same image code for both

            var c = (StampObject)stamp.Clone();
            Assert.Equal("id", c.Id);
            Assert.Same(raster, c.Picture);
            Assert.Equal((stamp.Cx, stamp.Cy, stamp.Scale, stamp.Rot), (c.Cx, c.Cy, c.Scale, c.Rot));
            Assert.Equal(a.Rgb, Draw(c).Rgb);
        }
    }
}
