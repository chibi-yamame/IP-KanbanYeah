using System;
using System.Collections.Generic;

namespace KanbanDrawer.Core
{
    /// <summary>A straight 1px pen stroke between two inclusive end points (horizontal or vertical, or a single dot).</summary>
    public readonly struct Stroke : IEquatable<Stroke>
    {
        public readonly short X0, Y0, X1, Y1;

        public Stroke(int x0, int y0, int x1, int y1)
        {
            X0 = (short)x0; Y0 = (short)y0; X1 = (short)x1; Y1 = (short)y1;
        }

        public int Length => Math.Abs(X1 - X0) + Math.Abs(Y1 - Y0) + 1;
        public bool Equals(Stroke o) => X0 == o.X0 && Y0 == o.Y0 && X1 == o.X1 && Y1 == o.Y1;
        public override bool Equals(object obj) => obj is Stroke s && Equals(s);
        public override int GetHashCode() => (X0 << 24) ^ (Y0 << 16) ^ (X1 << 8) ^ Y1;
        public override string ToString() => $"({X0},{Y0})-({X1},{Y1})";
    }

    /// <summary>All strokes of one colour, drawn after selecting that colour once.</summary>
    public sealed class ColorPass
    {
        public int Color { get; }
        public bool Vertical { get; }
        public List<Stroke> Strokes { get; }
        /// <summary>Pixels this pass must make correct.</summary>
        public int MustPixels { get; }

        public ColorPass(int color, bool vertical, List<Stroke> strokes, int mustPixels)
        {
            Color = color; Vertical = vertical; Strokes = strokes; MustPixels = mustPixels;
        }
    }

    public sealed class DrawPlan
    {
        /// <summary>Palette index to fill the whole board with first (CLR), or -1 for none.</summary>
        public int ClearColor { get; }
        public IReadOnlyList<ColorPass> Passes { get; }

        public DrawPlan(int clearColor, IReadOnlyList<ColorPass> passes)
        {
            ClearColor = clearColor; Passes = passes;
        }

        public int StrokeCount
        {
            get { int n = 0; foreach (var p in Passes) n += p.Strokes.Count; return n; }
        }

        /// <summary>Colour button clicks (one per pass, plus one for the CLR colour).</summary>
        public int ColorClicks => Passes.Count + (ClearColor >= 0 ? 1 : 0);

        /// <summary>Planner cost: a colour change costs about as much as a few strokes.</summary>
        public int Cost => StrokeCount + 3 * ColorClicks + (ClearColor >= 0 ? 1 : 0);

        public bool IsEmpty => ClearColor < 0 && Passes.Count == 0;

        public override string ToString() => $"clear={ClearColor} passes={Passes.Count} strokes={StrokeCount}";
    }

    /// <summary>
    /// Turns a palette image into colour passes of straight strokes.
    /// The program's 1px pen joins every drag point to the previous one with drawLine (PenTool.mouseDrag / mouseUp), so
    /// press at one end + release at the other end draws a whole run. Pixels that a later colour paints over are
    /// "don't care" for earlier colours, so runs may bridge them; pixels that are already final are never crossed.
    /// </summary>
    public static class StrokePlanner
    {
        /// <summary>Full plan from an unknown board: CLR with the best colour, then passes. Tries every used colour.</summary>
        public static DrawPlan PlanFull(IndexedImage target)
        {
            var hist = target.Histogram();
            DrawPlan best = null;
            for (int c = 0; c < Palette.Count; c++)
            {
                if (hist[c] == 0) continue;
                var plan = PlanAfterClear(target, c);
                if (best == null || plan.Cost < best.Cost) best = plan;
            }
            return best ?? new DrawPlan(-1, new List<ColorPass>());
        }

        public static DrawPlan PlanAfterClear(IndexedImage target, int clearColor)
        {
            var correct = new bool[target.Index.Length];
            for (int i = 0; i < correct.Length; i++) correct[i] = target.Index[i] == clearColor;
            return new DrawPlan(clearColor, Greedy(target, correct));
        }

        /// <summary>
        /// Plan from what the board shows now (0xRRGGBB per pixel, same size as the target): pixels that already have
        /// the target colour are kept. With <paramref name="allowClear"/> a CLR plan is used instead when cheaper.
        /// </summary>
        public static DrawPlan PlanFrom(IndexedImage target, int[] boardRgb, bool allowClear)
        {
            if (boardRgb.Length != target.Index.Length) throw new ArgumentException("size mismatch");
            var correct = new bool[boardRgb.Length];
            for (int i = 0; i < correct.Length; i++)
                correct[i] = (boardRgb[i] & 0xFFFFFF) == Palette.All[target.Index[i]].Rgb;
            var keep = new DrawPlan(-1, Greedy(target, correct));
            if (!allowClear || keep.IsEmpty) return keep;
            var full = PlanFull(target);
            return full.Cost < keep.Cost ? full : keep;
        }

        /// <summary>Pixels of <paramref name="boardRgb"/> that differ from the target.</summary>
        public static int CountWrong(IndexedImage target, int[] boardRgb)
        {
            int n = 0;
            for (int i = 0; i < boardRgb.Length; i++)
                if ((boardRgb[i] & 0xFFFFFF) != Palette.All[target.Index[i]].Rgb) n++;
            return n;
        }

        static List<ColorPass> Greedy(IndexedImage target, bool[] correct)
        {
            int w = target.Width, h = target.Height;
            var t = target.Index;
            correct = (bool[])correct.Clone();
            var passes = new List<ColorPass>();
            var remaining = new bool[Palette.Count];
            for (int i = 0; i < t.Length; i++)
                if (!correct[i]) remaining[t[i]] = true;

            while (true)
            {
                int bestC = -1, bestCount = int.MaxValue, bestMust = 0;
                bool bestV = false;
                for (int c = 0; c < Palette.Count; c++)
                {
                    if (!remaining[c]) continue;
                    int must;
                    int ch = Runs(t, correct, w, h, c, false, null, out must);
                    int cv = Runs(t, correct, w, h, c, true, null, out _);
                    bool v = cv < ch;
                    int cnt = v ? cv : ch;
                    // fewest strokes first; ties: the colour covering more pixels
                    if (cnt < bestCount || (cnt == bestCount && must > bestMust))
                    {
                        bestC = c; bestCount = cnt; bestMust = must; bestV = v;
                    }
                }
                if (bestC < 0) break;
                var strokes = new List<Stroke>(bestCount);
                Runs(t, correct, w, h, bestC, bestV, strokes, out int mustPx);
                passes.Add(new ColorPass(bestC, bestV, strokes, mustPx));
                for (int i = 0; i < t.Length; i++)
                    if (t[i] == bestC) correct[i] = true;
                remaining[bestC] = false;
            }
            return passes;
        }

        /// <summary>
        /// Counts (and optionally emits) the strokes for colour <paramref name="c"/>. Per pixel: must = wrong and target c;
        /// forbidden = correct and target not c (final already); everything else may be painted.
        /// </summary>
        static int Runs(byte[] t, bool[] correct, int w, int h, int c, bool vertical, List<Stroke> outStrokes, out int must)
        {
            int count = 0;
            must = 0;
            int lines = vertical ? w : h, len = vertical ? h : w;
            for (int a = 0; a < lines; a++)
            {
                int first = -1, last = -1;
                for (int b = 0; b <= len; b++)
                {
                    bool forbidden = true, isMust = false;
                    if (b < len)
                    {
                        int i = vertical ? b * w + a : a * w + b;
                        bool ok = correct[i];
                        bool mine = t[i] == c;
                        forbidden = ok && !mine;
                        isMust = !ok && mine;
                    }
                    if (isMust)
                    {
                        must++;
                        if (first < 0) first = b;
                        last = b;
                    }
                    else if (forbidden && first >= 0)
                    {
                        count++;
                        if (outStrokes != null)
                            outStrokes.Add(vertical ? new Stroke(a, first, a, last) : new Stroke(first, a, last, a));
                        first = -1;
                    }
                }
            }
            return count;
        }

        /// <summary>Paints a plan onto a board (0xRRGGBB per pixel) the way the program would. Used by tests and previews.</summary>
        public static void Apply(DrawPlan plan, int[] board, int w)
        {
            if (plan.ClearColor >= 0)
                for (int i = 0; i < board.Length; i++) board[i] = Palette.All[plan.ClearColor].Rgb;
            foreach (var pass in plan.Passes)
            {
                int rgb = Palette.All[pass.Color].Rgb;
                foreach (var s in pass.Strokes)
                    ApplyStroke(board, w, s, rgb);
            }
        }

        public static void ApplyStroke(int[] board, int w, Stroke s, int rgb)
        {
            int dx = Math.Sign(s.X1 - s.X0), dy = Math.Sign(s.Y1 - s.Y0);
            int x = s.X0, y = s.Y0;
            while (true)
            {
                board[y * w + x] = rgb;
                if (x == s.X1 && y == s.Y1) break;
                x += dx; y += dy;
            }
        }
    }
}
