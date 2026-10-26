using System;

namespace KanbanDrawer.Core.Board
{
    /// <summary>How a picture is placed on the board (画像 tab, 置き方). Always centred, the same scale on both axes.</summary>
    public enum PictureFit
    {
        /// <summary>全体を収める: the whole picture inside the board.</summary>
        Contain,
        /// <summary>トリミングして埋める: covers the board, the overflow is outside (cut off).</summary>
        Cover,
        /// <summary>等倍: one picture pixel = one board pixel.</summary>
        Actual,
    }

    /// <summary>
    /// A picture opened from a file and placed on the board (not registered in a library). Moved, scaled and rotated
    /// freely like a stamp; new ones go to the very back.
    /// </summary>
    public sealed class PictureObject : ImageObject
    {
        /// <summary>Pictures are shrunk on loading so that their long side is at most this (memory, rendering cost).</summary>
        public const int MaxSide = 1280;

        /// <summary>File name it came from (informational).</summary>
        public string FileName;

        public PictureObject(string fileName, Raster picture) : base(picture)
        {
            FileName = fileName;
        }

        public override BoardObject Clone()
        {
            var o = new PictureObject(FileName, Picture);
            CopyTo(o);
            return o;
        }

        /// <summary>The picture itself when its long side is at most <see cref="MaxSide"/>, else a copy shrunk to it by area averaging.</summary>
        public static Raster Shrink(Raster src)
        {
            int side = Math.Max(src.Width, src.Height);
            if (side <= MaxSide) return src;
            double f = (double)MaxSide / side;
            int w = src.Width >= src.Height ? MaxSide : Math.Max(1, (int)Math.Round(src.Width * f));
            int h = src.Height >= src.Width ? MaxSide : Math.Max(1, (int)Math.Round(src.Height * f));
            return StampPrep.Scale(src, w, h);
        }

        /// <summary>Scale that places a w x h picture on the board in the given way.</summary>
        public static double FitScale(int w, int h, PictureFit mode)
        {
            if (w <= 0 || h <= 0) throw new ArgumentException("empty picture");
            double sx = (double)BoardDoc.W / w, sy = (double)BoardDoc.H / h;
            switch (mode)
            {
                case PictureFit.Cover: return Math.Max(sx, sy);
                case PictureFit.Actual: return 1;
                default: return Math.Min(sx, sy);
            }
        }

        /// <summary>Places the picture again: board centre, upright, scale for the mode.</summary>
        public void FitTo(PictureFit mode)
        {
            Cx = BoardDoc.W / 2.0;
            Cy = BoardDoc.H / 2.0;
            Rot = 0;
            Scale = FitScale(Picture.Width, Picture.Height, mode);
        }
    }
}
