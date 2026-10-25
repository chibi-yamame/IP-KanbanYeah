using System;
using System.Collections.Generic;
using System.Linq;

namespace KanbanDrawer.Core.Board
{
    /// <summary>
    /// Preparing a picture for the stamp library: background removal, trimming, size cap. Ported from the sibling
    /// project's ipx.android.Stamp (removeBackground / borderColours / trim / scale).
    /// </summary>
    public static class StampPrep
    {
        public const int DefaultTolerance = 40;

        /// <summary>
        /// Background removal (optional) → trim transparent margins. Pictures are kept at their own pixels (placed dot by
        /// dot = 100%); whether the result may be registered is <see cref="FitsBoard"/>.
        /// </summary>
        public static Raster Prepare(Raster src, bool removeBackground, int tolerance = DefaultTolerance)
        {
            var r = removeBackground ? RemoveBackground(src, tolerance) : src;
            return Trim(r);
        }

        /// <summary>Stamps may be at most as large as the board (320x240), checked after <see cref="Prepare"/>.</summary>
        public static bool FitsBoard(Raster r) => r.Width <= BoardGeometry.Width && r.Height <= BoardGeometry.Height;

        /// <summary>
        /// The colours that make up much of the border (each at least a tenth of it, the most common always) are
        /// flood-filled away from the border, so areas of those colours inside the picture stay. More than one colour
        /// counts, so a bar along one edge (a screenshot's status bar) does not stop the fill.
        /// </summary>
        public static Raster RemoveBackground(Raster src, int tolerance)
        {
            int w = src.Width, h = src.Height;
            var outPx = (int[])src.Pixels.Clone();
            var seen = new bool[w * h];
            var stack = new int[w * h];
            foreach (int bg in BorderColours(src))
            {
                Array.Clear(seen, 0, seen.Length);
                int top = 0;
                for (int x = 0; x < w; x++)
                {
                    top = Push(stack, seen, top, x);
                    top = Push(stack, seen, top, (h - 1) * w + x);
                }
                for (int y = 0; y < h; y++)
                {
                    top = Push(stack, seen, top, y * w);
                    top = Push(stack, seen, top, y * w + w - 1);
                }
                while (top > 0)
                {
                    int i = stack[--top];
                    if (!Close(src.Pixels[i], bg, tolerance)) continue;
                    outPx[i] = 0;
                    int x = i % w, y = i / w;
                    if (x > 0) top = Push(stack, seen, top, i - 1);
                    if (x < w - 1) top = Push(stack, seen, top, i + 1);
                    if (y > 0) top = Push(stack, seen, top, i - w);
                    if (y < h - 1) top = Push(stack, seen, top, i + w);
                }
            }
            return new Raster(w, h, outPx);
        }

        /// <summary>Opaque border pixels grouped by colour (16 levels per channel): mean colours of the big groups (at most 4).</summary>
        internal static int[] BorderColours(Raster src)
        {
            int w = src.Width, h = src.Height;
            var groups = new Dictionary<int, long[]>(); // key -> count, r, g, b
            int n = 0;
            void Take(int c)
            {
                if (((c >> 24) & 255) < 128) return;
                int r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
                int key = (r >> 4) << 8 | (g >> 4) << 4 | (b >> 4);
                if (!groups.TryGetValue(key, out var a)) groups[key] = a = new long[4];
                a[0]++; a[1] += r; a[2] += g; a[3] += b;
                n++;
            }
            for (int x = 0; x < w; x++) { Take(src[x, 0]); if (h > 1) Take(src[x, h - 1]); }
            for (int y = 1; y < h - 1; y++) { Take(src[0, y]); if (w > 1) Take(src[w - 1, y]); }
            var list = groups.Values.OrderByDescending(a => a[0]).ToList();
            var result = new List<int>();
            foreach (var a in list)
            {
                if (result.Count >= 4 || (result.Count > 0 && a[0] * 10 < n)) break;
                result.Add(unchecked((int)0xFF000000) | (int)(a[1] / a[0]) << 16 | (int)(a[2] / a[0]) << 8 | (int)(a[3] / a[0]));
            }
            return result.ToArray();
        }

        static int Push(int[] stack, bool[] seen, int top, int i)
        {
            if (seen[i]) return top;
            seen[i] = true;
            stack[top] = i;
            return top + 1;
        }

        static bool Close(int a, int b, int tol)
        {
            if (((a >> 24) & 255) < 128) return true; // already transparent
            return Math.Abs(((a >> 16) & 255) - ((b >> 16) & 255)) <= tol
                && Math.Abs(((a >> 8) & 255) - ((b >> 8) & 255)) <= tol
                && Math.Abs((a & 255) - (b & 255)) <= tol;
        }

        /// <summary>The picture cut to its non-transparent pixels (an empty picture becomes 1x1 transparent).</summary>
        public static Raster Trim(Raster src)
        {
            int w = src.Width, h = src.Height, l = w, t = h, r = -1, b = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (((src[x, y] >> 24) & 255) == 0) continue;
                    if (x < l) l = x;
                    if (x > r) r = x;
                    if (y < t) t = y;
                    if (y > b) b = y;
                }
            if (r < 0) return new Raster(1, 1);
            return src.Crop(l, t, r - l + 1, b - t + 1);
        }

        /// <summary>Area-average scaling, alpha weighted so edges do not turn dark.</summary>
        public static Raster Scale(Raster src, int dw, int dh)
        {
            int sw = src.Width, sh = src.Height;
            var dst = new Raster(dw, dh);
            double fx = (double)sw / dw, fy = (double)sh / dh;
            for (int y = 0; y < dh; y++)
            {
                int y0 = (int)Math.Floor(y * fy), y1 = Math.Max(y0 + 1, Math.Min(sh, (int)Math.Ceiling((y + 1) * fy)));
                for (int x = 0; x < dw; x++)
                {
                    int x0 = (int)Math.Floor(x * fx), x1 = Math.Max(x0 + 1, Math.Min(sw, (int)Math.Ceiling((x + 1) * fx)));
                    long a = 0, r = 0, g = 0, b = 0;
                    int n = 0;
                    for (int sy = y0; sy < y1 && sy < sh; sy++)
                        for (int sx = x0; sx < x1 && sx < sw; sx++)
                        {
                            int p = src[sx, sy];
                            int pa = (p >> 24) & 255;
                            a += pa; r += ((p >> 16) & 255) * pa; g += ((p >> 8) & 255) * pa; b += (p & 255) * pa;
                            n++;
                        }
                    if (a == 0 || n == 0) continue;
                    dst[x, y] = (int)(a / n) << 24 | (int)(r / a) << 16 | (int)(g / a) << 8 | (int)(b / a);
                }
            }
            return dst;
        }
    }
}
