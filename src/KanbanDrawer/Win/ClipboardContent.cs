using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using KanbanDrawer.Core;

namespace KanbanDrawer.Win
{
    /// <summary>
    /// What a paste puts on the board: the first of picture files &gt; PNG (alpha kept) &gt; Bitmap (opaque) &gt; text.
    /// Reads any <see cref="IDataObject"/> (an in-memory <see cref="DataObject"/> in tests); the clipboard itself is read by the caller.
    /// </summary>
    public sealed class ClipboardContent
    {
        public enum ContentKind { None, Files, Image, Text }

        /// <summary>Same extensions as the 画像 tab's open dialog.</summary>
        static readonly string[] PictureExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

        public static readonly ClipboardContent None = new ClipboardContent(ContentKind.None);

        public ContentKind Kind { get; }
        /// <summary>Existing picture files, in the clipboard's order (<see cref="ContentKind.Files"/>).</summary>
        public string[] Files { get; }
        /// <summary>ARGB picture (<see cref="ContentKind.Image"/>).</summary>
        public Raster Image { get; }
        /// <summary>Text without leading / trailing blanks and blank lines, never empty (<see cref="ContentKind.Text"/>).</summary>
        public string Text { get; }

        ClipboardContent(ContentKind kind, string[] files = null, Raster image = null, string text = null)
        {
            Kind = kind;
            Files = files;
            Image = image;
            Text = text;
        }

        /// <summary>The first kind found; a format that cannot be read counts as absent. Never throws.</summary>
        public static ClipboardContent Read(IDataObject data)
        {
            if (data == null) return None;
            try
            {
                var files = ReadFiles(data);
                if (files != null) return new ClipboardContent(ContentKind.Files, files: files);
                var image = ReadPng(data) ?? ReadBitmap(data);
                if (image != null) return new ClipboardContent(ContentKind.Image, image: image);
                var text = ReadText(data);
                if (text != null) return new ClipboardContent(ContentKind.Text, text: text);
            }
            catch (Exception) { } // e.g. the clipboard is held by another application
            return None;
        }

        static string[] ReadFiles(IDataObject data)
        {
            try
            {
                if (!(data.GetData(DataFormats.FileDrop) is string[] all)) return null;
                var files = all.Where(f => !string.IsNullOrEmpty(f)
                                           && PictureExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())
                                           && File.Exists(f)).ToArray();
                return files.Length > 0 ? files : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>"PNG" as browsers, Office and paint programs put it: keeps the alpha.</summary>
        static Raster ReadPng(IDataObject data)
        {
            try
            {
                var o = data.GetData("PNG");
                var s = o as Stream ?? (o is byte[] b ? new MemoryStream(b) : null); // the data object's stream is not ours to close
                if (s == null) return null;
                if (s.CanSeek) s.Position = 0;
                using (var img = System.Drawing.Image.FromStream(s))
                using (var bmp = new Bitmap(img))
                    return ImageLoader.ToRaster(bmp);
            }
            catch (Exception) { return null; }
        }

        /// <summary>Bitmap / DIB: opaque (a DIB's fourth byte is not a reliable alpha).</summary>
        static Raster ReadBitmap(IDataObject data)
        {
            try
            {
                if (!(data.GetData(DataFormats.Bitmap) is System.Drawing.Image img)) return null;
                Raster r;
                using (var bmp = new Bitmap(img)) r = ImageLoader.ToRaster(bmp);
                for (int i = 0; i < r.Pixels.Length; i++) r.Pixels[i] |= unchecked((int)0xFF000000);
                return r;
            }
            catch (Exception) { return null; }
        }

        /// <summary>Leading / trailing blanks and blank lines removed; inner line breaks kept; not shortened.</summary>
        static string ReadText(IDataObject data)
        {
            try
            {
                var s = (data.GetData(DataFormats.UnicodeText) as string)?.Trim();
                return string.IsNullOrEmpty(s) ? null : s;
            }
            catch (Exception) { return null; }
        }
    }
}
