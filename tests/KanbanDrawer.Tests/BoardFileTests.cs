using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using KanbanDrawer.Core;
using KanbanDrawer.Core.Board;
using KanbanDrawer.UI;
using KanbanDrawer.Win;
using Xunit;

namespace KanbanDrawer.Tests
{
    /// <summary>
    /// Stand-in for PNG: a PNG head (signature + IHDR width / height, as <see cref="BoardFileTests.PngHead"/>) followed
    /// by the raw ARGB pixels.
    /// </summary>
    internal sealed class RawCodec : IImageCodec
    {
        public byte[] EncodePng(Raster image)
        {
            var b = new byte[24 + image.Pixels.Length * 4];
            Array.Copy(BoardFileTests.PngHead(image.Width, image.Height), b, 24);
            Buffer.BlockCopy(image.Pixels, 0, b, 24, image.Pixels.Length * 4);
            return b;
        }

        public Raster DecodePng(byte[] png)
        {
            if (!BoardFile.TryReadPngSize(png, out int w, out int h)) throw new InvalidDataException("not a png");
            if (png.Length != 24 + (long)w * h * 4) throw new InvalidDataException("short");
            var px = new int[w * h];
            Buffer.BlockCopy(png, 24, px, 0, px.Length * 4);
            return new Raster(w, h, px);
        }
    }

    public class BoardFileTests
    {
        static readonly RawCodec Codec = new RawCodec();

        /// <summary>The first 24 bytes of a PNG: signature, IHDR length and type, width, height (big endian).</summary>
        internal static byte[] PngHead(long w, long h)
        {
            var b = new byte[24];
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(b, 0);
            for (int i = 0; i < 4; i++)
            {
                b[16 + i] = (byte)(w >> (24 - 8 * i));
                b[20 + i] = (byte)(h >> (24 - 8 * i));
            }
            return b;
        }

        static Raster Pattern(int w, int h, int seed)
        {
            var r = new Raster(w, h);
            var rnd = new Random(seed);
            for (int i = 0; i < r.Pixels.Length; i++)
                r.Pixels[i] = (rnd.Next(4) == 0 ? 0 : rnd.Next(64, 256) << 24) | rnd.Next(0x1000000);
            return r;
        }

        static BoardDoc Sample(BlockText text)
        {
            var doc = new BoardDoc { BackgroundColor = 6, Frame = Pattern(320, 240, 1), FrameId = "frame-ab12" };
            doc.Objects.Add(new PictureObject("写真 a=b.png", Pattern(200, 150, 2)) { Cx = 100.25, Cy = 90.5, Scale = 0.7, Rot = 0.3 });
            doc.Objects.Add(new StampObject("st=1\\x", Pattern(40, 30, 3)) { Cx = 250, Cy = 60, Scale = 1.75, Rot = -1.1 });
            var t = new TextObject
            {
                Text = "一行目 a=b\n二行目 \\n\r\\", Font = "Some Font", Size = 18.5, Bold = true, Color = 4, Outline = 0, OutlineWidth = 2,
                Cx = 160, Cy = 200, Scale = 1.3, Rot = 0.1,
            };
            t.Measure(text);
            doc.Objects.Add(t);
            var o = new TextObject { Text = "公式", Size = 12, Color = 0, Official = true, Cx = 40, Cy = 20 };
            o.Measure(text);
            doc.Objects.Add(o);
            return doc;
        }

        static byte[] Save(BoardDoc doc, QuantizeOptions q = null)
        {
            using (var ms = new MemoryStream())
            {
                BoardFile.Write(doc, q ?? new QuantizeOptions(), Codec, ms);
                return ms.ToArray();
            }
        }

        static BoardContent Open(byte[] file, ITextRasterizer text = null, Func<string, string> fonts = null)
        {
            using (var ms = new MemoryStream(file))
                return BoardFile.Read(ms, Codec, text ?? new BlockText(), fonts);
        }

        static void AssertSameComposite(BoardDoc a, BoardDoc b, IRenderServices s)
        {
            var ca = Compositor.Build(a, s);
            var cb = Compositor.Build(b, s);
            Assert.Equal(ca.Fixed, cb.Fixed);
            Assert.Equal(ca.Rgb, cb.Rgb);
        }

        [Fact]
        public void RoundTripKeepsTheCompositeAndTheAttributes()
        {
            var text = new BlockText();
            var doc = Sample(text);
            var c = Open(Save(doc), text, n => n);
            var back = new BoardDoc();
            back.Load(c);

            AssertSameComposite(doc, back, text);
            Assert.Equal(6, back.BackgroundColor);
            Assert.Equal("frame-ab12", back.FrameId);
            Assert.Equal(doc.Frame.Pixels, back.Frame.Pixels);
            Assert.Equal(doc.Objects.Select(o => o.GetType()), back.Objects.Select(o => o.GetType()));
            for (int i = 0; i < doc.Objects.Count; i++)
            {
                Assert.Equal(doc.Objects[i].Cx, back.Objects[i].Cx);
                Assert.Equal(doc.Objects[i].Cy, back.Objects[i].Cy);
                Assert.Equal(doc.Objects[i].Scale, back.Objects[i].Scale);
                Assert.Equal(doc.Objects[i].Rot, back.Objects[i].Rot);
            }
            Assert.Equal("写真 a=b.png", ((PictureObject)back.Objects[0]).FileName);
            Assert.Equal(((PictureObject)doc.Objects[0]).Picture.Pixels, ((PictureObject)back.Objects[0]).Picture.Pixels);
            Assert.Equal("st=1\\x", ((StampObject)back.Objects[1]).Id);
            var t0 = (TextObject)doc.Objects[2];
            var t1 = (TextObject)back.Objects[2];
            Assert.Equal(t0.Text, t1.Text);
            Assert.Equal("Some Font", t1.Font);
            Assert.Equal(18.5, t1.Size);
            Assert.True(t1.Bold);
            Assert.Equal(4, t1.Color);
            Assert.Equal(0, t1.Outline);
            Assert.Equal(2, t1.OutlineWidth);
            Assert.False(t1.Official);
            Assert.Equal(t0.LocalW, t1.LocalW);
            Assert.True(((TextObject)back.Objects[3]).Official);
            Assert.Empty(c.Warnings);
        }

        [Fact]
        public void RoundTripKeepsTheQuantizeSettings()
        {
            foreach (DitherKind d in Enum.GetValues(typeof(DitherKind)))
            {
                var q = new QuantizeOptions
                {
                    Dither = d, Strength = 0.37, Brightness = -12, Contrast = 55, Saturation = 180,
                    Enabled = new[] { true, false, true, true, false, true, true, true, false, true },
                };
                var r = Open(Save(new BoardDoc(), q)).Quantize;
                Assert.Equal(d, r.Dither);
                Assert.Equal(0.37, r.Strength, 6);
                Assert.Equal(-12, r.Brightness);
                Assert.Equal(55, r.Contrast);
                Assert.Equal(180, r.Saturation);
                Assert.Equal(q.Enabled, r.Enabled);
            }
        }

        [Fact]
        public void EmptyBoardRoundTrips()
        {
            var c = Open(Save(new BoardDoc()));
            Assert.Equal(Palette.White, c.BackgroundColor);
            Assert.Null(c.Frame);
            Assert.Null(c.FrameId);
            Assert.Empty(c.Objects);
            Assert.NotNull(c.Quantize);
        }

        [Fact]
        public void BodyIsReadableKeyValueText()
        {
            var doc = new BoardDoc { BackgroundColor = 2 };
            doc.Objects.Add(new StampObject("ab12cd34", Pattern(10, 10, 5)) { Cx = 160, Cy = 120 });
            var body = Entries(Save(doc))["board.txt"];
            Assert.False(body.Length >= 3 && body[0] == 0xEF); // no BOM
            var lines = Encoding.UTF8.GetString(body).Split('\n');
            Assert.Equal("kanban-board=1", lines[0]);
            Assert.Contains("object.0.type=stamp", lines);
            Assert.Contains("object.0.id=ab12cd34", lines);
            Assert.Contains("object.0.image=images/0.png", lines);
            Assert.Contains("quantize.dither=fs", lines);
            Assert.DoesNotContain(lines, l => l.Contains("\r"));
        }

        [Fact]
        public void StampsAndFramesNotInAnyLibraryOpen()
        {
            // ids that no library knows: the pixels come from the file
            var text = new BlockText();
            var doc = new BoardDoc { Frame = Pattern(320, 240, 9), FrameId = "gone" };
            doc.Objects.Add(new StampObject("deleted-stamp", Pattern(30, 30, 8)) { Cx = 50, Cy = 50 });
            var back = new BoardDoc();
            back.Load(Open(Save(doc), text));
            AssertSameComposite(doc, back, text);
            Assert.Equal("gone", back.FrameId);
            Assert.Equal("deleted-stamp", ((StampObject)back.Objects[0]).Id);
        }

        [Fact]
        public void FrameOfAnotherSizeIsWrittenAsNoFrame()
        {
            var c = Open(Save(new BoardDoc { Frame = Pattern(100, 100, 1), FrameId = "x" }));
            Assert.Null(c.Frame);
            Assert.Null(c.FrameId);
        }

        // ---------------------------------------------------------------- versions and broken files

        const string Body =
            "kanban-board=1\nbackground=2\nquantize.dither=fs\nquantize.strength=100\nquantize.brightness=0\nquantize.contrast=0\n" +
            "quantize.saturation=100\nquantize.colors=1111111111\nobjects=1\nobject.0.type=stamp\nobject.0.x=160\nobject.0.y=120\n" +
            "object.0.scale=1\nobject.0.rot=0\nobject.0.id=ab12cd34\nobject.0.image=images/0.png\n";

        static byte[] Stamp10 => new RawCodec().EncodePng(Pattern(10, 10, 4));

        static byte[] Zip(string body, params (string Name, byte[] Data)[] entries)
        {
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    if (body != null) Put(zip, "board.txt", Encoding.UTF8.GetBytes(body));
                    foreach (var e in entries) Put(zip, e.Name, e.Data);
                }
                return ms.ToArray();
            }
        }

        static void Put(ZipArchive zip, string name, byte[] data)
        {
            using (var s = zip.CreateEntry(name).Open()) s.Write(data, 0, data.Length);
        }

        static Dictionary<string, byte[]> Entries(byte[] file)
        {
            var d = new Dictionary<string, byte[]>();
            using (var zip = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read))
                foreach (var e in zip.Entries)
                    using (var s = e.Open())
                    using (var m = new MemoryStream()) { s.CopyTo(m); d[e.FullName] = m.ToArray(); }
            return d;
        }

        static string With(string key, string value)
        {
            var lines = Body.TrimEnd('\n').Split('\n').ToList();
            int i = lines.FindIndex(l => l.StartsWith(key + "="));
            if (value == null) lines.RemoveAt(i);
            else if (i < 0) lines.Add(key + "=" + value);
            else lines[i] = key + "=" + value;
            return string.Join("\n", lines) + "\n";
        }

        static BoardFileException Fails(byte[] file)
        {
            var doc = new BoardDoc { BackgroundColor = 7 };
            doc.Objects.Add(new StampObject("keep", Pattern(5, 5, 1)));
            var e = Assert.Throws<BoardFileException>(() => doc.Load(Open(file)));
            Assert.Equal(7, doc.BackgroundColor); // nothing applied
            Assert.Single(doc.Objects);
            return e;
        }

        static void Corrupt(byte[] file, string inMessage = null)
        {
            var e = Fails(file);
            Assert.Equal(BoardFileProblem.Corrupt, e.Problem);
            Assert.StartsWith("看板のファイルが壊れているため開けません（", e.Message);
            if (inMessage != null) Assert.Contains(inMessage, e.Message);
        }

        [Fact]
        public void TheMinimalBodyOpens()
        {
            var c = Open(Zip(Body, ("images/0.png", Stamp10)));
            Assert.Single(c.Objects);
            Assert.Equal("ab12cd34", ((StampObject)c.Objects[0]).Id);
        }

        [Fact]
        public void NewerVersionIsRefusedBeforeAnythingElse()
        {
            var e = Fails(Zip(With("kanban-board", "2")));
            Assert.Equal(BoardFileProblem.Newer, e.Problem);
            Assert.Contains("形式 2", e.Message);
            // the rest broken: still "newer"
            e = Fails(Zip("kanban-board=3\nbackground=99\nobjects=x\n"));
            Assert.Equal(BoardFileProblem.Newer, e.Problem);
        }

        [Fact]
        public void BadVersionsAreCorrupt()
        {
            Corrupt(Zip(With("kanban-board", "0"), ("images/0.png", Stamp10)));
            Corrupt(Zip(With("kanban-board", "1.5"), ("images/0.png", Stamp10)));
            Corrupt(Zip(With("kanban-board", "x"), ("images/0.png", Stamp10)));
            Corrupt(Zip("background=2\n" + Body, ("images/0.png", Stamp10)), "kanban-board");
        }

        [Fact]
        public void BrokenContainersAreCorrupt()
        {
            Corrupt(Encoding.UTF8.GetBytes(Body));                              // not a ZIP
            Corrupt(new byte[0]);
            Corrupt(Zip(null, ("images/0.png", Stamp10)), "board.txt");         // no body
        }

        [Fact]
        public void BrokenKeysAreCorrupt()
        {
            (string Key, string Value)[] cases =
            {
                ("background", null), ("background", "10"), ("background", "-1"),
                ("quantize.dither", "best"), ("quantize.strength", "101"), ("quantize.colors", "0000000000"), ("quantize.colors", "11"),
                ("objects", null), ("objects", "2"), ("objects", "-1"),
                ("object.0.type", "circle"), ("object.0.x", null), ("object.0.x", "NaN"), ("object.0.y", "Infinity"),
                ("object.0.scale", "0"), ("object.0.scale", "NaN"), ("object.0.scale", "-1"), ("object.0.rot", "abc"),
                ("object.0.image", null), ("object.0.image", "images/9.png"),
            };
            foreach (var (k, v) in cases) Corrupt(Zip(With(k, v), ("images/0.png", Stamp10)), k);
            // more objects in the file than "objects" says
            Corrupt(Zip(With("object.1.type", "stamp"), ("images/0.png", Stamp10)));
            Corrupt(Zip(With("objects", "1001")), "多すぎ");
        }

        [Fact]
        public void BrokenTextIsCorrupt()
        {
            string body = With("objects", "1").Replace("object.0.type=stamp", "object.0.type=text")
                + "object.0.text=A\nobject.0.font=X\nobject.0.size=20\nobject.0.color=3\n";
            Assert.IsType<TextObject>(Open(Zip(body)).Objects[0]);
            Corrupt(Zip(body + "object.0.color=10\n"), "color");
            Corrupt(Zip(body + "object.0.outline=-2\n"), "outline");
            Corrupt(Zip(body + "object.0.size=0\n"), "size");
            Corrupt(Zip(body + "object.0.size=2001\n"), "size");
            Corrupt(Zip(body + "object.0.text=\n"), "text");
            Corrupt(Zip(body + "object.0.bold=2\n"), "bold");
            Corrupt(Zip(body + "object.0.outline.width=51\n"), "outline.width");
        }

        [Fact]
        public void BrokenImagesAreCorrupt()
        {
            Corrupt(Zip(Body, ("images/0.png", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25 })), "PNG");
            Corrupt(Zip(Body, ("images/0.png", PngHead(0, 10))), "範囲外");
            Corrupt(Zip(Body, ("images/0.png", PngHead(1281, 10))), "範囲外");
            Corrupt(Zip(Body, ("images/0.png", PngHead(0xFFFFFFFF, 0xFFFFFFFF))), "範囲外"); // huge declared size, nothing allocated
            Corrupt(Zip(Body, ("images/0.png", PngHead(10, 10))), "読めません");            // head only: decoding fails
            // the frame must be 320x240 exactly
            string withFrame = Body + "frame.entry=frame.png\n";
            Assert.NotNull(Open(Zip(withFrame, ("images/0.png", Stamp10), ("frame.png", Codec.EncodePng(Pattern(320, 240, 1))))).Frame);
            Corrupt(Zip(withFrame, ("images/0.png", Stamp10), ("frame.png", Codec.EncodePng(Pattern(320, 239, 1)))), "frame.png");
            Corrupt(Zip(withFrame, ("images/0.png", Stamp10)), "frame.png");
        }

        [Fact]
        public void OversizedEntriesAreCorrupt()
        {
            // compressed they are tiny; reading stops one byte past the limit
            Corrupt(Zip(Body + new string(' ', BoardFile.MaxBodyBytes)), "board.txt");
            var big = new byte[BoardFile.MaxImageBytes + 1];
            PngHead(10, 10).CopyTo(big, 0);
            Corrupt(Zip(Body, ("images/0.png", big)), "images/0.png");
        }

        [Fact]
        public void TooManyPixelsIsCorrupt()
        {
            // 20 stamps of 1280x1280 = 32.8 M pixels > 32 M: refused from the PNG heads alone
            var sb = new StringBuilder(With("objects", "20").Replace(Body.Substring(Body.IndexOf("object.0.")), ""));
            var entries = new List<(string, byte[])>();
            for (int i = 0; i < 20; i++)
            {
                sb.Append($"object.{i}.type=stamp\nobject.{i}.x=0\nobject.{i}.y=0\nobject.{i}.scale=1\nobject.{i}.rot=0\nobject.{i}.image=images/{i}.png\n");
                entries.Add(($"images/{i}.png", PngHead(1280, 1280)));
            }
            Corrupt(Zip(sb.ToString(), entries.ToArray()), "大きすぎ");
        }

        [Fact]
        public void UnknownKeysAndEntriesAreIgnored()
        {
            string body = Body + "future.key=1\nobject.0.glow=7\n\nnot a key line\nbackground=3\n"; // the later key wins
            var c = Open(Zip(body, ("images/0.png", Stamp10), ("notes/readme.txt", new byte[] { 1, 2 })));
            Assert.Equal(3, c.BackgroundColor);
            Assert.Single(c.Objects);
        }

        [Fact]
        public void CrLfBodyOpens()
        {
            var c = Open(Zip(Body.Replace("\n", "\r\n"), ("images/0.png", Stamp10)));
            Assert.Equal("ab12cd34", ((StampObject)c.Objects[0]).Id);
        }

        // ---------------------------------------------------------------- fonts, official text

        [Fact]
        public void MissingFontsAreReplacedWithOneWarningEach()
        {
            var text = new BlockText();
            var doc = new BoardDoc();
            foreach (var f in new[] { "Gone", "Gone", "Here" })
            {
                var t = new TextObject { Text = "A", Font = f, Size = 10 };
                t.Measure(text);
                doc.Objects.Add(t);
            }
            var off = new TextObject { Text = "B", Font = "Gone2", Size = 12, Official = true };
            off.Measure(text);
            doc.Objects.Add(off);
            var c = Open(Save(doc), text, n => n == "Here" ? n : "Default");
            Assert.Equal(new[] { "Default", "Default", "Here", "Gone2" }, c.Objects.Cast<TextObject>().Select(t => t.Font));
            Assert.Single(c.Warnings);
            Assert.Contains("「Gone」", c.Warnings[0]);
        }

        [Fact]
        public void OfficialTextIsUprightAndUnscaled()
        {
            string body = With("objects", "1").Replace("object.0.type=stamp", "object.0.type=text")
                .Replace("object.0.rot=0", "object.0.rot=1.2").Replace("object.0.scale=1", "object.0.scale=3")
                + "object.0.text=A\nobject.0.size=12\nobject.0.color=0\nobject.0.official=1\n";
            var t = (TextObject)Open(Zip(body)).Objects[0];
            Assert.True(t.Official);
            Assert.Equal(0, t.Rot);
            Assert.Equal(1, t.Scale);
        }

        // ---------------------------------------------------------------- BoardDoc.Load, codec, title

        [Fact]
        public void LoadClearsHistoryAndSelectionAndNotifiesOnce()
        {
            var doc = new BoardDoc();
            doc.Add(new StampObject("a", Pattern(5, 5, 1)));
            doc.Add(new StampObject("b", Pattern(5, 5, 2)));
            doc.Undo();
            Assert.True(doc.CanUndo && doc.CanRedo && doc.Selected != null);
            int changed = 0, selection = 0;
            doc.Changed += () => changed++;
            doc.SelectionChanged += () => selection++;
            doc.Load(BoardContent.Empty());
            Assert.False(doc.CanUndo);
            Assert.False(doc.CanRedo);
            Assert.Null(doc.Selected);
            Assert.Empty(doc.Objects);
            Assert.Equal(1, changed);
            Assert.Equal(1, selection);
            Assert.Equal(Palette.White, doc.BackgroundColor);
            Assert.Null(doc.Frame);
        }

        [Fact]
        public void PngCodecKeepsAlpha()
        {
            var src = Pattern(37, 23, 11);
            var png = new PngCodec().EncodePng(src);
            Assert.True(BoardFile.TryReadPngSize(png, out int w, out int h));
            Assert.Equal((37, 23), (w, h));
            var back = new PngCodec().DecodePng(png);
            Assert.Equal((37, 23), (back.Width, back.Height));
            for (int i = 0; i < src.Pixels.Length; i++)
                if (((uint)src.Pixels[i] >> 24) != 0) Assert.Equal(src.Pixels[i], back.Pixels[i]);
                else Assert.Equal(0u, (uint)back.Pixels[i] >> 24);
            Assert.ThrowsAny<Exception>(() => new PngCodec().DecodePng(PngHead(10, 10)));
        }

        [Fact]
        public void RealPngRoundTrip()
        {
            var text = new BlockText();
            var doc = Sample(text);
            byte[] file;
            using (var ms = new MemoryStream())
            {
                BoardFile.Write(doc, new QuantizeOptions(), new PngCodec(), ms);
                file = ms.ToArray();
            }
            BoardContent c;
            using (var ms = new MemoryStream(file)) c = BoardFile.Read(ms, new PngCodec(), text, null);
            var back = new BoardDoc();
            back.Load(c);
            AssertSameComposite(doc, back, text);
        }

        [Fact]
        public void Titles()
        {
            Assert.Equal("看板屋", BoardFileCommands.TitleOf(null, false));
            Assert.Equal("看板屋 - (無題)*", BoardFileCommands.TitleOf(null, true));
            Assert.Equal("看板屋 - 店.kanban", BoardFileCommands.TitleOf(@"C:\a\店.kanban", false));
            Assert.Equal("看板屋 - 店.kanban*", BoardFileCommands.TitleOf(@"C:\a\店.kanban", true));
        }
    }
}
