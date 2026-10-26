using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using KanbanDrawer.Win;
using Xunit;
using Kind = KanbanDrawer.Win.ClipboardContent.ContentKind;

namespace KanbanDrawer.Tests
{
    /// <summary>貼り付け: what <see cref="ClipboardContent.Read"/> takes from a data object (in memory, not the real clipboard).</summary>
    public class PasteTests : IDisposable
    {
        static readonly Color HalfRed = Color.FromArgb(128, 255, 0, 0);
        readonly string dir = Path.Combine(Path.GetTempPath(), "kd-paste-" + Guid.NewGuid().ToString("N"));

        public PasteTests() => Directory.CreateDirectory(dir);

        public void Dispose()
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }

        /// <summary>2x1: a half transparent red pixel and a fully transparent one.</summary>
        static Bitmap Picture()
        {
            var b = new Bitmap(2, 1, PixelFormat.Format32bppArgb);
            b.SetPixel(0, 0, HalfRed);
            b.SetPixel(1, 0, Color.Transparent);
            return b;
        }

        static MemoryStream Png()
        {
            var s = new MemoryStream();
            using (var b = Picture()) b.Save(s, ImageFormat.Png);
            s.Position = 0;
            return s;
        }

        string MakeFile(string name)
        {
            string path = Path.Combine(dir, name);
            if (name.EndsWith(".png")) using (var b = Picture()) b.Save(path, ImageFormat.Png);
            else File.WriteAllText(path, "x");
            return path;
        }

        [Fact]
        public void FilesComeFirstAndOnlyExistingPictures()
        {
            string png = MakeFile("a.png"), txt = MakeFile("b.txt"), missing = Path.Combine(dir, "c.png");
            var d = new DataObject();
            d.SetData(DataFormats.FileDrop, new[] { txt, missing, png });
            d.SetData("PNG", Png());
            d.SetText("text");
            var c = ClipboardContent.Read(d);
            Assert.Equal(Kind.Files, c.Kind);
            Assert.Equal(new[] { png }, c.Files);
        }

        [Fact]
        public void NoPictureFilesFallsThroughToTheNextKind()
        {
            var d = new DataObject();
            d.SetData(DataFormats.FileDrop, new[] { MakeFile("b.txt") });
            d.SetText("text");
            Assert.Equal(Kind.Text, ClipboardContent.Read(d).Kind);
        }

        [Fact]
        public void PngKeepsAlphaAndWinsOverBitmapAndText()
        {
            var d = new DataObject();
            d.SetData("PNG", Png());
            using (var b = new Bitmap(5, 5)) d.SetImage(b);
            d.SetText("text");
            var c = ClipboardContent.Read(d);
            Assert.Equal(Kind.Image, c.Kind);
            Assert.Equal(2, c.Image.Width);
            Assert.Equal(HalfRed.ToArgb(), c.Image[0, 0]);
            Assert.Equal(0, (c.Image[1, 0] >> 24) & 0xFF);
        }

        [Fact]
        public void BitmapIsOpaqueAndWinsOverText()
        {
            var d = new DataObject();
            using (var b = Picture())
            {
                d.SetImage(b);
                d.SetText("text");
                var c = ClipboardContent.Read(d);
                Assert.Equal(Kind.Image, c.Kind);
                Assert.Equal(2, c.Image.Width);
                Assert.All(c.Image.Pixels, p => Assert.Equal(0xFF, (p >> 24) & 0xFF));
            }
        }

        [Fact]
        public void BrokenPngFallsThrough()
        {
            var d = new DataObject();
            d.SetData("PNG", new MemoryStream(new byte[] { 1, 2, 3 }));
            d.SetText("text");
            Assert.Equal(Kind.Text, ClipboardContent.Read(d).Kind);
        }

        [Fact]
        public void TextIsTrimmedButKeepsInnerLines()
        {
            var d = new DataObject();
            d.SetText(" \r\n\r\n  一行目\r\n\r\n三行目 \t\r\n\r\n");
            var c = ClipboardContent.Read(d);
            Assert.Equal(Kind.Text, c.Kind);
            Assert.Equal("一行目\r\n\r\n三行目", c.Text);
        }

        [Fact]
        public void LongTextIsNotCut()
        {
            string s = new string('あ', 5000);
            var d = new DataObject();
            d.SetText(s);
            Assert.Equal(s, ClipboardContent.Read(d).Text);
        }

        [Theory]
        [InlineData(" \r\n\t\r\n　")]
        [InlineData("")]
        public void BlankTextIsNothing(string text)
        {
            var d = new DataObject();
            d.SetData(DataFormats.UnicodeText, text);
            Assert.Equal(Kind.None, ClipboardContent.Read(d).Kind);
        }

        [Fact]
        public void NothingIsNone()
        {
            Assert.Equal(Kind.None, ClipboardContent.Read(new DataObject()).Kind);
            Assert.Equal(Kind.None, ClipboardContent.Read(null).Kind);
            var d = new DataObject();
            d.SetData("Something else", "x");
            Assert.Equal(Kind.None, ClipboardContent.Read(d).Kind);
        }
    }
}
