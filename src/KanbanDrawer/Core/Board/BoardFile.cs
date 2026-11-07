using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace KanbanDrawer.Core.Board
{
    /// <summary>PNG encoding / decoding (Win/PngCodec; tests use a stand-in that stores raw pixels).</summary>
    public interface IImageCodec
    {
        /// <summary>ARGB, alpha kept.</summary>
        byte[] EncodePng(Raster image);
        /// <summary>Throws when the data is broken (the caller reports the file as corrupt).</summary>
        Raster DecodePng(byte[] png);
    }

    /// <summary>What a .kanban file holds = a board before it is applied to the editor.</summary>
    public sealed class BoardContent
    {
        public int BackgroundColor = Palette.White;
        /// <summary>null = no frame.</summary>
        public Raster Frame;
        public string FrameId;
        public List<BoardObject> Objects = new List<BoardObject>();
        /// <summary>null = keep the current settings (新規).</summary>
        public QuantizeOptions Quantize;
        /// <summary>Warnings for the user (fonts replaced, ...).</summary>
        public List<string> Warnings = new List<string>();

        /// <summary>White, no frame, no objects, Quantize = null.</summary>
        public static BoardContent Empty() => new BoardContent();
    }

    public enum BoardFileProblem { Corrupt, Newer }

    /// <summary>A .kanban file that cannot be opened. <see cref="Exception.Message"/> is the text shown to the user.</summary>
    public sealed class BoardFileException : Exception
    {
        public BoardFileProblem Problem { get; }

        public BoardFileException(BoardFileProblem problem, string message) : base(message)
        {
            Problem = problem;
        }

        internal static BoardFileException Corrupt(string reason) =>
            new BoardFileException(BoardFileProblem.Corrupt, $"看板のファイルが壊れているため開けません（{reason}）");

        internal static BoardFileException Newer(int version) =>
            new BoardFileException(BoardFileProblem.Newer,
                $"このファイルは新しい版の看板屋で保存されています（形式 {version}）。看板屋を新しくしてから開いてください");
    }

    /// <summary>
    /// The edited board as a .kanban file (docs/design/boardfile.md): a ZIP with board.txt (key=value lines), frame.png and
    /// images/&lt;i&gt;.png. Reading validates everything before anything is returned, and caps every size (the ZIP's
    /// declared sizes are not trusted).
    /// </summary>
    public static class BoardFile
    {
        public const int Version = 1;
        public const string Extension = ".kanban";

        const string BodyEntry = "board.txt", FrameEntry = "frame.png", MagicKey = "kanban-board";
        public const int MaxBodyBytes = 4 * 1024 * 1024;
        public const int MaxImageBytes = 16 * 1024 * 1024;
        public const int MaxObjects = 1000;
        public const long MaxTotalPixels = 32000000;
        public const int MaxImageSide = PictureObject.MaxSide;
        public const int MaxTextLength = 10000;

        static readonly string[] DitherNames = { "none", "fs", "atkinson", "bayer" }; // DitherKind order, as CLI --dither

        // ---------------------------------------------------------------- write

        /// <summary>Writes the document and the quantize settings (output: writable and seekable; left open).</summary>
        public static void Write(BoardDoc doc, QuantizeOptions quantize, IImageCodec codec, Stream output)
        {
            var sb = new StringBuilder();
            void Put(string key, string value) => sb.Append(key).Append('=').Append(value).Append('\n');
            void PutNum(string key, double v) => Put(key, v.ToString("R", CultureInfo.InvariantCulture));
            void PutInt(string key, int v) => Put(key, v.ToString(CultureInfo.InvariantCulture));

            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                PutInt(MagicKey, Version);
                PutInt("background", doc.BackgroundColor);
                var frame = doc.Frame;
                if (frame != null && frame.Width == BoardDoc.W && frame.Height == BoardDoc.H)
                {
                    AddBytes(zip, FrameEntry, codec.EncodePng(frame));
                    Put("frame.entry", FrameEntry);
                    if (doc.FrameId != null) Put("frame.id", Escape(doc.FrameId));
                }
                var q = quantize ?? new QuantizeOptions();
                Put("quantize.dither", DitherNames[Math.Max(0, Math.Min(DitherNames.Length - 1, (int)q.Dither))]);
                PutInt("quantize.strength", (int)Math.Round(q.Strength * 100));
                PutInt("quantize.brightness", q.Brightness);
                PutInt("quantize.contrast", q.Contrast);
                PutInt("quantize.saturation", q.Saturation);
                var mask = new char[Palette.Count];
                for (int i = 0; i < mask.Length; i++) mask[i] = i < q.Enabled.Length && q.Enabled[i] ? '1' : '0';
                Put("quantize.colors", new string(mask));

                PutInt("objects", doc.Objects.Count);
                for (int i = 0; i < doc.Objects.Count; i++)
                {
                    var o = doc.Objects[i];
                    string k = "object." + i.ToString(CultureInfo.InvariantCulture) + ".";
                    Put(k + "type", o is PictureObject ? "picture" : o is StampObject ? "stamp" : o is TextObject ? "text"
                        : throw new InvalidOperationException("unknown object " + o.GetType().Name));
                    PutNum(k + "x", o.Cx);
                    PutNum(k + "y", o.Cy);
                    PutNum(k + "scale", o.Scale);
                    PutNum(k + "rot", o.Rot);
                    switch (o)
                    {
                        case ImageObject img:
                            string entry = "images/" + i.ToString(CultureInfo.InvariantCulture) + ".png";
                            AddBytes(zip, entry, codec.EncodePng(img.Picture));
                            Put(k + "image", entry);
                            if (img is PictureObject p) Put(k + "name", Escape(p.FileName ?? ""));
                            if (img is StampObject s && s.Id != null) Put(k + "id", Escape(s.Id));
                            break;
                        case TextObject t:
                            Put(k + "text", Escape(t.Text ?? ""));
                            Put(k + "font", Escape(t.Font ?? ""));
                            PutNum(k + "size", t.Size);
                            Put(k + "bold", t.Bold ? "1" : "0");
                            PutInt(k + "color", t.Color);
                            PutInt(k + "outline", t.Outline);
                            PutInt(k + "outline.width", t.OutlineWidth);
                            Put(k + "official", t.Official ? "1" : "0");
                            break;
                    }
                }
                var body = zip.CreateEntry(BodyEntry, CompressionLevel.Optimal);
                using (var s = body.Open())
                {
                    var bytes = new UTF8Encoding(false).GetBytes(sb.ToString());
                    s.Write(bytes, 0, bytes.Length);
                }
            }
        }

        static void AddBytes(ZipArchive zip, string name, byte[] data)
        {
            var e = zip.CreateEntry(name, CompressionLevel.NoCompression); // PNG is compressed already
            using (var s = e.Open()) s.Write(data, 0, data.Length);
        }

        // ---------------------------------------------------------------- read

        /// <summary>
        /// Reads and validates a file; touches no document. Failures are <see cref="BoardFileException"/>.
        /// <paramref name="text"/> measures text objects (null = not measured); <paramref name="fontResolver"/> maps a font
        /// name to the one to use (another name = replaced with a warning; null = never replaced).
        /// </summary>
        public static BoardContent Read(Stream input, IImageCodec codec, ITextRasterizer text, Func<string, string> fontResolver)
        {
            ZipArchive zip;
            try { zip = new ZipArchive(input, ZipArchiveMode.Read, true); }
            catch (Exception e) when (e is InvalidDataException || e is ArgumentException || e is NotSupportedException)
            {
                throw BoardFileException.Corrupt("ZIP ではありません");
            }
            using (zip)
            {
                try
                {
                    return ReadZip(zip, codec, text, fontResolver);
                }
                catch (InvalidDataException) // a damaged entry inside an otherwise readable ZIP
                {
                    throw BoardFileException.Corrupt("ZIP の中身が読めません");
                }
            }
        }

        static BoardContent ReadZip(ZipArchive zip, IImageCodec codec, ITextRasterizer text, Func<string, string> fontResolver)
        {
            var bodyEntry = zip.GetEntry(BodyEntry) ?? throw BoardFileException.Corrupt(BodyEntry + " がありません");
            string body = new UTF8Encoding(false, false).GetString(ReadCapped(bodyEntry, MaxBodyBytes, BodyEntry));
            if (body.Length > 0 && body[0] == '﻿') body = body.Substring(1);

            // the version first: a newer file is not interpreted any further
            int nl = body.IndexOf('\n');
            string first = (nl < 0 ? body : body.Substring(0, nl)).TrimEnd('\r');
            if (!first.StartsWith(MagicKey + "=", StringComparison.Ordinal))
                throw BoardFileException.Corrupt("1 行目が " + MagicKey + " ではありません");
            if (!int.TryParse(first.Substring(MagicKey.Length + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int version))
                throw BoardFileException.Corrupt("形式の版数が整数ではありません");
            if (version > Version) throw BoardFileException.Newer(version);
            if (version < 1) throw BoardFileException.Corrupt("形式の版数が不正です");

            var keys = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var raw in body.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue; // not key=value: ignored like an unknown key
                keys[line.Substring(0, eq)] = line.Substring(eq + 1);
            }
            var r = new KeyReader(keys);
            var c = new BoardContent();

            c.BackgroundColor = r.Int("background", 0, Palette.Count - 1);

            var q = new QuantizeOptions();
            int dither = Array.IndexOf(DitherNames, r.String("quantize.dither"));
            if (dither < 0) throw BoardFileException.Corrupt("quantize.dither の値が不正です");
            q.Dither = (DitherKind)dither;
            q.Strength = r.Int("quantize.strength", 0, 100) / 100.0;
            q.Brightness = r.Int("quantize.brightness", -100, 100);
            q.Contrast = r.Int("quantize.contrast", -100, 100);
            q.Saturation = r.Int("quantize.saturation", 0, 200);
            string mask = r.String("quantize.colors");
            if (mask.Length != Palette.Count || mask.Trim('0', '1').Length != 0 || mask.IndexOf('1') < 0)
                throw BoardFileException.Corrupt("quantize.colors の値が不正です");
            for (int i = 0; i < Palette.Count; i++) q.Enabled[i] = mask[i] == '1';
            c.Quantize = q;

            if (!keys.TryGetValue("objects", out string countText)) throw BoardFileException.Corrupt("objects がありません");
            if (!int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) || count < 0)
                throw BoardFileException.Corrupt("objects の値が不正です");
            if (count > MaxObjects) throw BoardFileException.Corrupt("オブジェクトが多すぎます");
            foreach (var k in keys.Keys)
                if (k.Length > 12 && k.StartsWith("object.", StringComparison.Ordinal) && k.EndsWith(".type", StringComparison.Ordinal)
                    && int.TryParse(k.Substring(7, k.Length - 12), NumberStyles.None, CultureInfo.InvariantCulture, out int j) && j >= count)
                    throw BoardFileException.Corrupt("objects とオブジェクトの数が合いません");

            // every image's size from its PNG head first: too many pixels is refused before anything is decoded
            keys.TryGetValue("frame.entry", out string frameEntry);
            long pixels = 0;
            if (frameEntry != null) CheckHead(zip, frameEntry, "frame.entry", BoardDoc.W, BoardDoc.H, BoardDoc.W, BoardDoc.H, ref pixels);
            for (int i = 0; i < count; i++)
            {
                string k = "object." + i.ToString(CultureInfo.InvariantCulture) + ".";
                if (keys.TryGetValue(k + "type", out string type) && (type == "picture" || type == "stamp")
                    && keys.TryGetValue(k + "image", out string image))
                    CheckHead(zip, image, k + "image", 1, 1, MaxImageSide, MaxImageSide, ref pixels);
            }

            if (frameEntry != null)
            {
                c.Frame = ReadImage(zip, codec, frameEntry, "frame.entry", BoardDoc.W, BoardDoc.H, BoardDoc.W, BoardDoc.H);
                c.FrameId = r.OptString("frame.id");
            }

            var replacedFonts = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                string k = "object." + i.ToString(CultureInfo.InvariantCulture) + ".";
                if (!keys.ContainsKey(k + "type")) throw BoardFileException.Corrupt("objects とオブジェクトの数が合いません（" + k + "type）");
                string type = r.String(k + "type");
                BoardObject o;
                switch (type)
                {
                    case "picture":
                    case "stamp":
                    {
                        var pic = ReadImage(zip, codec, r.String(k + "image"), k + "image", 1, 1, MaxImageSide, MaxImageSide);
                        if (type == "picture") o = new PictureObject(r.OptString(k + "name") ?? "", pic);
                        else o = new StampObject(r.OptString(k + "id"), pic);
                        break;
                    }
                    case "text":
                    {
                        var t = new TextObject
                        {
                            Text = r.String(k + "text"),
                            Font = r.OptString(k + "font") ?? "",
                            Size = r.Num(k + "size"),
                            Bold = r.Bool(k + "bold", false),
                            Color = r.Int(k + "color", 0, Palette.Count - 1),
                            Outline = r.OptInt(k + "outline", -1, Palette.Count - 1, -1),
                            OutlineWidth = r.OptInt(k + "outline.width", 0, 50, 1),
                            Official = r.Bool(k + "official", false),
                        };
                        if (t.Text.Length == 0 || t.Text.Length > MaxTextLength)
                            throw BoardFileException.Corrupt(k + "text の長さが不正です");
                        if (t.Size < 1 || t.Size > 2000) throw BoardFileException.Corrupt(k + "size の値が不正です");
                        if (!t.Official && fontResolver != null)
                        {
                            string use = fontResolver(t.Font);
                            if (use != null && use != t.Font)
                            {
                                if (t.Font.Length > 0 && replacedFonts.Add(t.Font))
                                    c.Warnings.Add($"フォント「{t.Font}」がこの PC に無いため、既定のフォントで開きました");
                                t.Font = use;
                            }
                        }
                        o = t;
                        break;
                    }
                    default:
                        throw BoardFileException.Corrupt(k + "type の値が不正です");
                }
                o.Cx = r.Num(k + "x");
                o.Cy = r.Num(k + "y");
                o.Scale = r.Num(k + "scale");
                o.Rot = r.Num(k + "rot");
                if (!(o.Scale > 0)) throw BoardFileException.Corrupt(k + "scale の値が不正です");
                bool measured = true;
                if (o is TextObject to)
                {
                    if (to.Official) { to.Rot = 0; to.Scale = 1; }
                    if (text != null) to.Measure(text);
                    else measured = false; // the size is unknown: keep the scale as saved
                }
                if (o.CanScale && measured) o.ClampScale();
                c.Objects.Add(o);
            }
            return c;
        }

        /// <summary>The size range from the entry's PNG head (24 bytes read), added to the pixel total.</summary>
        static void CheckHead(ZipArchive zip, string name, string key, int minW, int minH, int maxW, int maxH, ref long pixels)
        {
            var entry = Entry(zip, name, key);
            var head = new byte[24];
            int got = 0;
            using (var s = entry.Open())
                for (int n; got < head.Length && (n = s.Read(head, got, head.Length - got)) > 0; ) got += n;
            CheckSize(got == head.Length ? head : null, name, minW, minH, maxW, maxH, out int w, out int h);
            pixels += (long)w * h;
            if (pixels > MaxTotalPixels) throw BoardFileException.Corrupt("画像が大きすぎます");
        }

        static ZipArchiveEntry Entry(ZipArchive zip, string name, string key) =>
            zip.GetEntry(name) ?? throw BoardFileException.Corrupt(key + " の画像 " + name + " がありません");

        static void CheckSize(byte[] png, string name, int minW, int minH, int maxW, int maxH, out int w, out int h)
        {
            if (!TryReadPngSize(png, out w, out h)) throw BoardFileException.Corrupt(name + " が PNG ではありません");
            if (w < minW || h < minH || w > maxW || h > maxH)
                throw BoardFileException.Corrupt($"{name} の大きさが範囲外です（{w}x{h}）");
        }

        /// <summary>An image entry (its head checked again), decoded; the decoded size must match the head.</summary>
        static Raster ReadImage(ZipArchive zip, IImageCodec codec, string name, string key, int minW, int minH, int maxW, int maxH)
        {
            var data = ReadCapped(Entry(zip, name, key), MaxImageBytes, name);
            CheckSize(data, name, minW, minH, maxW, maxH, out int w, out int h);
            Raster r;
            try { r = codec.DecodePng(data); }
            catch (Exception) { throw BoardFileException.Corrupt(name + " を読めません"); }
            if (r == null || r.Width != w || r.Height != h) throw BoardFileException.Corrupt(name + " を読めません");
            return r;
        }

        /// <summary>The entry's bytes, at most <paramref name="max"/> (reads max + 1 at most, whatever the ZIP declares).</summary>
        static byte[] ReadCapped(ZipArchiveEntry entry, int max, string name)
        {
            using (var s = entry.Open())
            using (var ms = new MemoryStream())
            {
                var buf = new byte[81920];
                int n;
                while ((n = s.Read(buf, 0, (int)Math.Min(buf.Length, max + 1L - ms.Length))) > 0)
                {
                    ms.Write(buf, 0, n);
                    if (ms.Length > max) throw BoardFileException.Corrupt(name + " が大きすぎます");
                }
                return ms.ToArray();
            }
        }

        static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>Width and height from the PNG head (8-byte signature, then the IHDR chunk). False when it is not a PNG.</summary>
        public static bool TryReadPngSize(byte[] png, out int width, out int height)
        {
            width = height = 0;
            if (png == null || png.Length < 24) return false;
            for (int i = 0; i < PngSignature.Length; i++)
                if (png[i] != PngSignature[i]) return false;
            if (png[12] != 'I' || png[13] != 'H' || png[14] != 'D' || png[15] != 'R') return false;
            long w = BigEndian(png, 16), h = BigEndian(png, 20);
            if (w > int.MaxValue || h > int.MaxValue) { width = height = int.MaxValue; return true; }
            width = (int)w; height = (int)h;
            return true;
        }

        static long BigEndian(byte[] b, int at) =>
            ((long)b[at] << 24) | ((long)b[at + 1] << 16) | ((long)b[at + 2] << 8) | b[at + 3];

        // ---------------------------------------------------------------- values

        /// <summary>Strings on one line: \ → \\, LF → \n, CR → \r.</summary>
        public static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (char ch in s)
            {
                if (ch == '\\') sb.Append("\\\\");
                else if (ch == '\n') sb.Append("\\n");
                else if (ch == '\r') sb.Append("\\r");
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>Inverse of <see cref="Escape"/> (an unknown \x stays as it is).</summary>
        public static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '\\' && i + 1 < s.Length)
                {
                    char n = s[i + 1];
                    if (n == '\\') { sb.Append('\\'); i++; continue; }
                    if (n == 'n') { sb.Append('\n'); i++; continue; }
                    if (n == 'r') { sb.Append('\r'); i++; continue; }
                }
                sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>Typed access to board.txt's keys; a missing or bad value is a corrupt file naming the key.</summary>
        sealed class KeyReader
        {
            readonly Dictionary<string, string> keys;
            public KeyReader(Dictionary<string, string> keys) { this.keys = keys; }

            string Raw(string key) =>
                keys.TryGetValue(key, out string v) ? v : throw BoardFileException.Corrupt(key + " がありません");

            static BoardFileException Bad(string key) => BoardFileException.Corrupt(key + " の値が不正です");

            public string String(string key) => Unescape(Raw(key));
            public string OptString(string key) => keys.TryGetValue(key, out string v) ? Unescape(v) : null;

            public int Int(string key, int min, int max)
            {
                if (!int.TryParse(Raw(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) || v < min || v > max) throw Bad(key);
                return v;
            }

            public int OptInt(string key, int min, int max, int def) => keys.ContainsKey(key) ? Int(key, min, max) : def;

            public double Num(string key)
            {
                if (!double.TryParse(Raw(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                    || double.IsNaN(v) || double.IsInfinity(v)) throw Bad(key);
                return v;
            }

            public bool Bool(string key, bool def)
            {
                if (!keys.TryGetValue(key, out string v)) return def;
                if (v == "0") return false;
                if (v == "1") return true;
                throw Bad(key);
            }
        }
    }
}
