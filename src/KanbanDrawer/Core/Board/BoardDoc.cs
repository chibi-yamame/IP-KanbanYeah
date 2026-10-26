using System;
using System.Collections.Generic;
using System.Linq;

namespace KanbanDrawer.Core.Board
{
    /// <summary>
    /// The board being edited: background (colour or frame), the full-board picture, objects (stamps, text) in
    /// z order (last = front), selection, snapshot undo/redo. Callers take <see cref="Checkpoint"/> before a change
    /// and call <see cref="NotifyChanged"/> after it.
    /// </summary>
    public sealed class BoardDoc
    {
        public const int W = BoardGeometry.Width, H = BoardGeometry.Height;
        const int UndoDepth = 50;

        /// <summary>Palette index of the plain background (used when there is no frame).</summary>
        public int BackgroundColor = Palette.White;
        /// <summary>Frame (320x240 palette image, already converted when registered) or null.</summary>
        public IndexedImage Frame;
        public string FrameId;
        /// <summary>Full-board picture already placed on 320x240 (ARGB, transparent margins), or null.</summary>
        public Raster Picture;
        public readonly List<BoardObject> Objects = new List<BoardObject>();
        public BoardObject Selected;

        readonly Stack<Snapshot> undo = new Stack<Snapshot>();
        readonly Stack<Snapshot> redo = new Stack<Snapshot>();

        public event Action Changed;
        public event Action SelectionChanged;

        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;

        public void NotifyChanged() => Changed?.Invoke();

        public void Select(BoardObject o)
        {
            if (o != null && !Objects.Contains(o)) o = null;
            if (Selected == o) return;
            Selected = o;
            SelectionChanged?.Invoke();
        }

        /// <summary>Topmost object picked by a click at the page point, or null.</summary>
        public BoardObject HitTest(double x, double y)
        {
            for (int i = Objects.Count - 1; i >= 0; i--)
                if (Objects[i].Hit(x, y)) return Objects[i];
            return null;
        }

        public void Add(BoardObject o)
        {
            Checkpoint();
            Objects.Add(o);
            Select(o);
            NotifyChanged();
        }

        public void Remove(BoardObject o)
        {
            if (o == null || !Objects.Contains(o)) return;
            Checkpoint();
            Objects.Remove(o);
            if (Selected == o) Select(null);
            NotifyChanged();
        }

        /// <summary>Moves an object one step to the front (+1) or back (-1).</summary>
        public void Restack(BoardObject o, int delta)
        {
            int i = Objects.IndexOf(o);
            int j = Math.Max(0, Math.Min(Objects.Count - 1, i + delta));
            if (i < 0 || i == j) return;
            Checkpoint();
            Objects.RemoveAt(i);
            Objects.Insert(j, o);
            NotifyChanged();
        }

        /// <summary>Saves the current state for undo (clears redo).</summary>
        public void Checkpoint()
        {
            undo.Push(Capture());
            if (undo.Count > UndoDepth)
            {
                var keep = undo.Take(UndoDepth).Reverse().ToList();
                undo.Clear();
                foreach (var s in keep) undo.Push(s);
            }
            redo.Clear();
        }

        public bool Undo()
        {
            if (undo.Count == 0) return false;
            redo.Push(Capture());
            Restore(undo.Pop());
            return true;
        }

        public bool Redo()
        {
            if (redo.Count == 0) return false;
            undo.Push(Capture());
            Restore(redo.Pop());
            return true;
        }

        Snapshot Capture() => new Snapshot
        {
            BackgroundColor = BackgroundColor,
            Frame = Frame,
            FrameId = FrameId,
            Picture = Picture,
            Objects = Objects.Select(o => o.Clone()).ToList(),
            Selected = Selected == null ? -1 : Objects.IndexOf(Selected),
        };

        void Restore(Snapshot s)
        {
            BackgroundColor = s.BackgroundColor;
            Frame = s.Frame;
            FrameId = s.FrameId;
            Picture = s.Picture;
            Objects.Clear();
            Objects.AddRange(s.Objects.Select(o => o.Clone()));
            Selected = s.Selected >= 0 && s.Selected < Objects.Count ? Objects[s.Selected] : null;
            SelectionChanged?.Invoke();
            NotifyChanged();
        }

        /// <summary>State kept for undo. Rasters / frames are never modified in place, so they are shared.</summary>
        sealed class Snapshot
        {
            public int BackgroundColor;
            public IndexedImage Frame;
            public string FrameId;
            public Raster Picture;
            public List<BoardObject> Objects;
            public int Selected;
        }
    }

    /// <summary>Builds the composite: background (fixed) → picture (free) → objects in z order.</summary>
    public static class Compositor
    {
        public static Composite Build(BoardDoc doc, IRenderServices services)
        {
            var c = BuildBase(doc);
            AddObjects(doc, c, services);
            return c;
        }

        public static void AddObjects(BoardDoc doc, Composite c, IRenderServices services)
        {
            foreach (var o in doc.Objects) o.Render(c, services);
        }

        /// <summary>Background and picture only (what does not change while objects are dragged).</summary>
        public static Composite BuildBase(BoardDoc doc)
        {
            var c = new Composite(BoardDoc.W, BoardDoc.H);
            int n = BoardDoc.W * BoardDoc.H;
            for (int i = 0; i < n; i++)
                c.SetFixed(i, doc.Frame != null && doc.Frame.Index[i] != IndexedImage.Transparent ? doc.Frame.Index[i] : doc.BackgroundColor);
            if (doc.Picture != null)
            {
                var px = doc.Picture.Pixels;
                for (int i = 0; i < n; i++)
                {
                    int p = px[i], a = (p >> 24) & 255;
                    if (a == 0) continue;
                    c.Blend(i, (p >> 16) & 255, (p >> 8) & 255, p & 255, a / 255.0);
                }
            }
            return c;
        }
    }

    /// <summary>
    /// Keeps the background + picture composite between frames: while objects move, only they are rendered again
    /// (on a copy of the cached base).
    /// </summary>
    public sealed class CompositeCache
    {
        Composite baseLayer;
        int bg = -1;
        IndexedImage frame;
        Raster picture;

        public Composite Build(BoardDoc doc, IRenderServices services)
        {
            if (baseLayer == null || bg != doc.BackgroundColor || !ReferenceEquals(frame, doc.Frame) || !ReferenceEquals(picture, doc.Picture))
            {
                baseLayer = Compositor.BuildBase(doc);
                bg = doc.BackgroundColor; frame = doc.Frame; picture = doc.Picture;
            }
            var c = baseLayer.Clone();
            Compositor.AddObjects(doc, c, services);
            return c;
        }
    }
}
