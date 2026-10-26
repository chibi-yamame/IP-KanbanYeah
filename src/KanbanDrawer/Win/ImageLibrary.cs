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
    /// Missing files and broken lines are skipped. Writes go through a .tmp file and a rename. Deleted files wait in
    /// trash\ until the next deletion or startup (<see cref="EmptyTrash"/>) so the last deletion can be restored.
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

        string TrashDir => Path.Combine(dir, "trash");
        string BatchPath => Path.Combine(TrashDir, "batch.txt");

        /// <summary>
        /// Removes the ids from the list and moves their files to trash\ (not in index.txt), replacing the previous
        /// deletion there. Unknown ids are ignored. Returns the record for <see cref="Restore"/>.
        /// </summary>
        public DeletionRecord Delete(IEnumerable<string> ids)
        {
            var before = Ids();
            var wanted = new HashSet<string>(ids ?? Enumerable.Empty<string>());
            var record = new DeletionRecord(Guid.NewGuid().ToString("N"), before);
            if (!before.Any(wanted.Contains)) return record;
            EmptyTrash();
            Directory.CreateDirectory(TrashDir);
            File.WriteAllText(BatchPath, record.Batch, new UTF8Encoding(false));
            for (int i = 0; i < before.Count; i++)
            {
                if (!wanted.Contains(before[i])) continue;
                string id = before[i];
                try
                {
                    File.Move(FileOf(id), Path.Combine(TrashDir, id + ".png"));
                    record.Items.Add((id, i));
                }
                catch (IOException) { }                     // stays as a stray file outside the list
                catch (UnauthorizedAccessException) { }
            }
            WriteIndex(before.Where(id => !wanted.Contains(id)).ToList());
            return record;
        }

        public DeletionRecord Delete(string id) => Delete(new[] { id });

        /// <summary>True while the record is the last deletion and its files are still in trash\.</summary>
        public bool CanRestore(DeletionRecord record)
        {
            if (record == null || record.Items.Count == 0) return false;
            try { return File.Exists(BatchPath) && File.ReadAllText(BatchPath).Trim() == record.Batch; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        /// <summary>
        /// Puts the record's items back at their old positions in the list: each after its old neighbour (exact when the
        /// list did not change since).
        /// Returns the ids put back; none when the record is not the last deletion (see <see cref="CanRestore"/>).
        /// </summary>
        public List<string> Restore(DeletionRecord record)
        {
            var back = new List<string>();
            if (!CanRestore(record)) return back;
            var ids = Ids();
            foreach (var (id, index) in record.Items.OrderBy(it => it.Index))
            {
                string from = Path.Combine(TrashDir, id + ".png");
                try
                {
                    if (!File.Exists(from) || File.Exists(FileOf(id)) || ids.Contains(id)) continue;
                    File.Move(from, FileOf(id));
                }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                // right after the nearest earlier neighbour that is still listed (exact when nothing else changed)
                int at = 0;
                for (int k = index - 1; k >= 0; k--)
                {
                    int found = ids.IndexOf(record.Before[k]);
                    if (found >= 0) { at = found + 1; break; }
                }
                ids.Insert(at, id);
                back.Add(id);
            }
            WriteIndex(ids);
            EmptyTrash(); // the batch is used up
            return back;
        }

        /// <summary>Deletes trash\ and the deletion it holds (at startup). Files that cannot be deleted stay.</summary>
        public void EmptyTrash()
        {
            try { if (Directory.Exists(TrashDir)) Directory.Delete(TrashDir, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
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

    /// <summary>What <see cref="ImageLibrary.Delete(IEnumerable{string})"/> removed: ids and their positions in the list.</summary>
    public sealed class DeletionRecord
    {
        internal readonly string Batch;
        internal readonly List<(string Id, int Index)> Items = new List<(string Id, int Index)>();
        internal readonly List<string> Before; // the list before the deletion
        internal DeletionRecord(string batch, List<string> before) { Batch = batch; Before = before; }

        /// <summary>Ids removed from the list, in list order.</summary>
        public IReadOnlyList<string> Ids => Items.Select(it => it.Id).ToList();
        public int Count => Items.Count;
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
