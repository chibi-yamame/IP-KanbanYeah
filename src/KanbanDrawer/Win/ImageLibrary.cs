using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using KanbanDrawer.Core;

namespace KanbanDrawer.Win
{
    /// <summary>
    /// PNG files in AppPaths.DataDir\&lt;folder&gt;\ with an index.txt (one id per line, in display order).
    /// Missing files and broken lines are skipped. Writes go through a .tmp file and a rename.
    /// </summary>
    public class ImageLibrary
    {
        readonly string dir;
        readonly string prefix;

        public ImageLibrary(string folder, string idPrefix, string root = null)
        {
            dir = Path.Combine(root ?? AppPaths.DataDir, folder);
            prefix = idPrefix;
        }

        string IndexPath => Path.Combine(dir, "index.txt");
        string FileOf(string id) => Path.Combine(dir, id + ".png");

        public List<string> Ids()
        {
            try
            {
                return File.ReadAllLines(IndexPath, Encoding.UTF8)
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0 && l.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && File.Exists(FileOf(l)))
                    .Distinct()
                    .ToList();
            }
            catch (IOException) { return new List<string>(); }
            catch (UnauthorizedAccessException) { return new List<string>(); }
        }

        public Raster Load(string id)
        {
            using (var bmp = ImageLoader.Load(FileOf(id))) return ImageLoader.ToRaster(bmp);
        }

        /// <summary>Stores a picture (ARGB, alpha kept) at the end of the list; returns its id.</summary>
        public string Add(Raster picture)
        {
            Directory.CreateDirectory(dir);
            string id = prefix + DateTime.Now.ToString("yyyyMMddHHmmssfff");
            while (File.Exists(FileOf(id))) id += "x";
            using (var bmp = ImageLoader.ToBitmap(picture))
            {
                string tmp = FileOf(id) + ".tmp";
                bmp.Save(tmp, ImageFormat.Png);
                File.Move(tmp, FileOf(id));
            }
            var ids = Ids();
            ids.Add(id);
            WriteIndex(ids);
            return id;
        }

        public void Delete(string id)
        {
            var ids = Ids();
            ids.Remove(id);
            WriteIndex(ids);
            try { File.Delete(FileOf(id)); } catch (IOException) { }
        }

        void WriteIndex(List<string> ids)
        {
            Directory.CreateDirectory(dir);
            string tmp = IndexPath + ".tmp";
            File.WriteAllText(tmp, string.Join("\n", ids) + "\n", new UTF8Encoding(false));
            if (File.Exists(IndexPath)) File.Delete(IndexPath);
            File.Move(tmp, IndexPath);
        }
    }

    /// <summary>Stamps: trimmed transparent PNGs (StampPrep.Prepare), at most 320x240 (StampPrep.FitsBoard).</summary>
    public sealed class StampLibrary : ImageLibrary
    {
        public StampLibrary(string root = null) : base("stamps", "s", root) { }
    }

    /// <summary>Frames: 320x240 pictures already in palette colours (converted when registered).</summary>
    public sealed class FrameLibrary : ImageLibrary
    {
        public FrameLibrary(string root = null) : base("frames", "f", root) { }

        /// <summary>The frame as palette indices; colours that are not exact palette colours map to the nearest one.</summary>
        public IndexedImage LoadIndexed(string id)
        {
            var r = Load(id);
            if (r.Width != BoardGeometry.Width || r.Height != BoardGeometry.Height)
                throw new InvalidDataException("frame is not 320x240");
            var all = new bool[Palette.Count];
            for (int i = 0; i < all.Length; i++) all[i] = true;
            var img = new IndexedImage(r.Width, r.Height);
            for (int i = 0; i < r.Pixels.Length; i++)
            {
                int k = Palette.IndexOf(r.Pixels[i]);
                img.Index[i] = (byte)(k >= 0 ? k : Quantizer.NearestExact((r.Pixels[i] >> 16) & 255, (r.Pixels[i] >> 8) & 255, r.Pixels[i] & 255, all));
            }
            return img;
        }
    }
}
