using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using KanbanDrawer.Core;
using KanbanDrawer.Core.Board;
using KanbanDrawer.Win;

namespace KanbanDrawer.UI.Features
{
    /// <summary>
    /// What features share: the document, quantize settings, libraries, text rendering, settings, log.
    /// Editing shows the board in full colour (<see cref="Composite"/>, re-rendered at most once per pass of the message
    /// loop however many changes arrive, so dragging stays smooth). The 10-colour picture that will be drawn and its
    /// strokes are the draft (<see cref="Draft"/> / <see cref="Plan"/>), made only on request (下書き) and marked
    /// stale by any later change.
    /// </summary>
    internal sealed class EditorHost
    {
        public readonly BoardDoc Doc = new BoardDoc();
        public QuantizeOptions Quantize { get; private set; } = new QuantizeOptions();
        public readonly GdiTextRasterizer Text = new GdiTextRasterizer();
        public readonly StampLibrary Stamps = new StampLibrary();
        public readonly FrameLibrary Frames = new FrameLibrary();
        public readonly Settings Settings;
        public readonly Control Ui;
        public Action<string> Log = s => { };

        /// <summary>The board in full colour (fixed pixels already in palette colours).</summary>
        public Composite Composite { get; private set; }
        /// <summary>The last draft (10 colours), or null. See <see cref="DraftStale"/>.</summary>
        public IndexedImage Draft { get; private set; }
        /// <summary>The board or the quantize settings changed after the draft was made.</summary>
        public bool DraftStale { get; private set; } = true;
        /// <summary>Strokes for <see cref="Draft"/>, null while being computed (or without a draft).</summary>
        public DrawPlan Plan { get; private set; }

        /// <summary>The full-colour board was re-rendered.</summary>
        public event Action Rendered;
        /// <summary>The draft was made, became stale, or its plan is ready.</summary>
        public event Action DraftChanged;
        /// <summary>The quantize settings changed (the draft is stale now).</summary>
        public event Action QuantizeChanged;

        readonly CompositeCache cache = new CompositeCache();
        bool renderPending;
        int draftVersion;
        object lastCheckpointKey;
        readonly Stopwatch sinceCheckpoint = Stopwatch.StartNew();

        public EditorHost(Settings settings, Control ui)
        {
            Settings = settings;
            Ui = ui;
            Doc.Changed += Invalidate;
            planTimer.Tick += (s, e) => PlanNow();
        }

        public IWin32Window Owner => Ui.FindForm();

        /// <summary>Asks for a re-render; changes arriving before it runs are rendered together.</summary>
        public void Invalidate()
        {
            MarkStale();
            if (renderPending) return;
            renderPending = true;
            if (Ui.IsHandleCreated) Ui.BeginInvoke((Action)RenderNow);
            else RenderNow();
        }

        /// <summary>Renders now (pending changes included).</summary>
        public void RenderNow()
        {
            renderPending = false;
            Composite = cache.Build(Doc, Text);
            Rendered?.Invoke();
        }

        public void SetQuantize(QuantizeOptions options)
        {
            Quantize = options;
            int before = draftVersion;
            QuantizeChanged?.Invoke();           // a listener may remake the draft right away
            if (draftVersion == before) MarkStale();
        }

        void MarkStale()
        {
            if (DraftStale) return;
            DraftStale = true;
            draftVersion++;
            Plan = null;
            DraftChanged?.Invoke();
        }

        /// <summary>Makes the draft: quantize now, plan the strokes in the background (<see cref="DraftChanged"/> twice).</summary>
        public void MakeDraft()
        {
            if (Array.IndexOf(Quantize.Enabled, true) < 0) { Log("使う色を 1 つ以上選んでください"); return; }
            if (renderPending || Composite == null) RenderNow();
            Draft = Quantizer.Quantize(Composite, Quantize);
            DraftStale = false;
            Plan = null;
            ++draftVersion;
            DraftChanged?.Invoke();
            // the plan follows after a pause, so dragging a 減色 slider with the draft shown does not queue plans
            planTimer.Stop();
            planTimer.Start();
        }

        readonly Timer planTimer = new Timer { Interval = 250 };

        void PlanNow()
        {
            planTimer.Stop();
            int v = draftVersion;
            var target = Draft;
            if (target == null || DraftStale) return;
            Task.Run(() => StrokePlanner.PlanFull(target)).ContinueWith(t =>
            {
                if (t.IsFaulted || v != draftVersion) return;
                Plan = t.Result;
                DraftChanged?.Invoke();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>The current draft and its plan, made first when missing or stale.</summary>
        public async Task<(IndexedImage Target, DrawPlan Plan)> EnsureDraftAsync()
        {
            if (Draft == null || DraftStale) MakeDraft();
            var target = Draft;
            var plan = Plan ?? await Task.Run(() => StrokePlanner.PlanFull(target));
            return (target, plan);
        }

        /// <summary>
        /// Undo step for slider-like edits: one checkpoint per burst of changes to the same thing (key) within 1.5 s.
        /// </summary>
        public void CheckpointFor(object key)
        {
            if (Equals(key, lastCheckpointKey) && sinceCheckpoint.ElapsedMilliseconds < 1500)
            {
                sinceCheckpoint.Restart();
                return;
            }
            Doc.Checkpoint();
            lastCheckpointKey = key;
            sinceCheckpoint.Restart();
        }

        /// <summary>Forgets the slider burst (after undo / redo / another kind of edit).</summary>
        public void EndCheckpointBurst() => lastCheckpointKey = null;

        /// <summary>Places a new object at the board centre and selects it.</summary>
        public void AddObject(BoardObject o)
        {
            o.Cx = BoardDoc.W / 2.0;
            o.Cy = BoardDoc.H / 2.0;
            EndCheckpointBurst();
            Doc.Add(o);
        }
    }
}
