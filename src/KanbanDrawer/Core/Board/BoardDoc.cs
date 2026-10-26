using System;
using System.Collections.Generic;
using System.Linq;

namespace KanbanDrawer.Core.Board
{
    /// <summary>
    /// The board being edited: background (colour and frame), objects (pictures, stamps, text) in z order (last = front),
    /// selection, snapshot undo/redo. Callers take <see cref="Checkpoint"/> before a change and call
    /// <see cref="NotifyChanged"/> after it.
    /// </summary>
    public sealed class BoardDoc
    {
        public const int W = BoardGeometry.Width, H = BoardGeometry.Height;
        const int UndoDepth = 50;

        /// <summary>Palette index of the background (shows where there is no frame or the frame is transparent).</summary>
        public int BackgroundColor = Palette.White;
        /// <summary>Frame (320x240 ARGB picture, full colour, quantized with the draft like pictures and stamps) or null.</summary>
        public Raster Frame;
        public string FrameId;
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

        public void Add(BoardObject o) => Insert(Objects.Count, o);

        /// <summary>Adds an object at a z position (0 = the very back) and selects it.</summary>
        public void Insert(int index, BoardObject o)
        {
            Checkpoint();
            Objects.Insert(Math.Max(0, Math.Min(Objects.Count, index)), o);
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
            Objects = Objects.Select(o => o.Clone()).ToList(),
            Selected = Selected == null ? -1 : Objects.IndexOf(Selected),
        };

        void Restore(Snapshot s)
        {
            BackgroundColor = s.BackgroundColor;
            Frame = s.Frame;
            FrameId = s.FrameId;
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
            public Raster Frame;
            public string FrameId;
            public List<BoardObject> Objects;
            public int Selected;
        }
    }

    /// <summary>Builds the composite: background colour (fixed) → frame (free) → objects in z order.</summary>
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

        /// <summary>
        /// Background only (what does not change while objects are dragged): the background colour as fixed pixels, the
        /// frame blended over it (its pixels become free; semi-transparent edges mix with the background colour).
        /// </summary>
        public static Composite BuildBase(BoardDoc doc)
        {
            var c = new Composite(BoardDoc.W, BoardDoc.H);
            int n = BoardDoc.W * BoardDoc.H;
            for (int i = 0; i < n; i++) c.SetFixed(i, doc.BackgroundColor);
            var f = doc.Frame;
            if (f == null || f.Width != BoardDoc.W || f.Height != BoardDoc.H) return c;
            for (int i = 0; i < n; i++)
            {
                int p = f.Pixels[i];
                c.Blend(i, (p >> 16) & 255, (p >> 8) & 255, p & 255, ((p >> 24) & 255) / 255.0);
            }
            return c;
        }
    }

    /// <summary>
    /// Keeps the background composite between frames: while objects move, only they are rendered again
    /// (on a copy of the cached base).
    /// </summary>
    public sealed class CompositeCache
    {
        Composite baseLayer;
        int bg = -1;
        Raster frame;

        public Composite Build(BoardDoc doc, IRenderServices services)
        {
            if (baseLayer == null || bg != doc.BackgroundColor || !ReferenceEquals(frame, doc.Frame))
            {
                baseLayer = Compositor.BuildBase(doc);
                bg = doc.BackgroundColor; frame = doc.Frame;
            }
            var c = baseLayer.Clone();
            Compositor.AddObjects(doc, c, services);
            return c;
        }
    }
}
