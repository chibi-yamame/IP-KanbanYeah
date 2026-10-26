using System;
using System.IO;
using System.Linq;
using KanbanDrawer.Core;
using KanbanDrawer.Win;
using Xunit;

namespace KanbanDrawer.Tests
{
    /// <summary>Deleting from a library into trash\ and restoring the last deletion (temp root, never the user's library).</summary>
    public class LibraryDeleteTests : IDisposable
    {
        readonly string root = Path.Combine(Path.GetTempPath(), "kd-test-" + Guid.NewGuid().ToString("N"));
        static readonly Raster Pic = new Raster(2, 2, new[] { unchecked((int)0xFFFF0000), 0, 0x7F00FF00, 1 });

        public void Dispose()
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }

        string Trash => Path.Combine(root, "stamps", "trash");

        StampLibrary WithFive(out string[] ids)
        {
            var lib = new StampLibrary(root);
            ids = Enumerable.Range(0, 5).Select(_ => lib.Add(Pic)).ToArray();
            return lib;
        }

        [Fact]
        public void BatchDeleteThenRestorePutsIdsBackInOrder()
        {
            var lib = WithFive(out var ids);
            var rec = lib.Delete(new[] { ids[3], ids[0], ids[1] });
            Assert.Equal(new[] { ids[0], ids[1], ids[3] }, rec.Ids);
            Assert.Equal(new[] { ids[2], ids[4] }, lib.Ids());
            Assert.False(File.Exists(Path.Combine(root, "stamps", ids[0] + ".png")));
            Assert.True(File.Exists(Path.Combine(Trash, ids[0] + ".png")));
            Assert.DoesNotContain("trash", File.ReadAllText(Path.Combine(root, "stamps", "index.txt")));

            Assert.True(lib.CanRestore(rec));
            Assert.Equal(new[] { ids[0], ids[1], ids[3] }, lib.Restore(rec));
            Assert.Equal(ids, lib.Ids());
            Assert.Equal(Pic.Pixels[0], lib.Load(ids[3]).Pixels[0]);
            Assert.False(lib.CanRestore(rec));   // used up
            Assert.Empty(lib.Restore(rec));
            Assert.Equal(ids, lib.Ids());
        }

        [Fact]
        public void NextDeletionMakesThePreviousOneUnrestorable()
        {
            var lib = WithFive(out var ids);
            var first = lib.Delete(new[] { ids[1] });
            var second = lib.Delete(ids[2]);
            Assert.False(lib.CanRestore(first));
            Assert.Empty(lib.Restore(first));
            Assert.False(File.Exists(Path.Combine(Trash, ids[1] + ".png")));
            Assert.Equal(new[] { ids[0], ids[3], ids[4] }, lib.Ids());
            Assert.Equal(new[] { ids[2] }, lib.Restore(second));
            Assert.Equal(new[] { ids[0], ids[2], ids[3], ids[4] }, lib.Ids());
        }

        [Fact]
        public void MissingAndBrokenFilesDoNotCrash()
        {
            var lib = WithFive(out var ids);
            File.Delete(Path.Combine(root, "stamps", ids[0] + ".png"));   // listed, but the file is gone
            File.AppendAllText(Path.Combine(root, "stamps", "index.txt"), "nosuch\n..\\evil\n\n");
            var rec = lib.Delete(new[] { ids[0], "nosuch", "..\\evil", "", ids[1] });
            Assert.Equal(new[] { ids[1] }, rec.Ids);
            Assert.Equal(new[] { ids[2], ids[3], ids[4] }, lib.Ids());

            // a file taken out of trash before restoring is skipped, the rest comes back
            var rec2 = lib.Delete(new[] { ids[2], ids[3] });
            File.Delete(Path.Combine(Trash, ids[2] + ".png"));
            Assert.Equal(new[] { ids[3] }, lib.Restore(rec2));
            Assert.Equal(new[] { ids[3], ids[4] }, lib.Ids());

            Assert.Empty(lib.Delete(new string[0]).Ids);
            Assert.Empty(lib.Restore(null));
            Assert.False(lib.CanRestore(null));
            Assert.Empty(new StampLibrary(Path.Combine(root, "empty")).Delete(new[] { "x" }).Ids);
        }

        [Fact]
        public void EmptyTrashAtStartupForgetsTheDeletion()
        {
            var lib = WithFive(out var ids);
            var rec = lib.Delete(new[] { ids[0], ids[4] });
            Assert.True(Directory.Exists(Trash));
            var next = new StampLibrary(root);   // the next start of the program
            next.EmptyTrash();
            Assert.False(Directory.Exists(Trash));
            Assert.False(next.CanRestore(rec));
            Assert.Empty(next.Restore(rec));
            Assert.Equal(new[] { ids[1], ids[2], ids[3] }, next.Ids());
            next.EmptyTrash();                    // nothing to empty: no error
        }

        [Fact]
        public void FramesUseTheirOwnTrash()
        {
            var stamps = WithFive(out var ids);
            var frames = new FrameLibrary(root);
            string f = frames.Add(Pic);
            var rs = stamps.Delete(ids[0]);
            var rf = frames.Delete(f);
            Assert.True(stamps.CanRestore(rs));
            Assert.True(frames.CanRestore(rf));
            Assert.True(File.Exists(Path.Combine(root, "frames", "trash", f + ".png")));
        }
    }
}
