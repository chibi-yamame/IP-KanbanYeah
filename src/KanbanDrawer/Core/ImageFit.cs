using System;

namespace KanbanDrawer.Core
{
    public enum FitMode
    {
        /// <summary>Whole picture visible, margins filled with the background colour.</summary>
        Contain,
        /// <summary>Fills the board, the overflowing part is cut off (centred).</summary>
        Cover,
        /// <summary>Stretched to the board, aspect ratio not kept.</summary>
        Stretch,
    }

    /// <summary>Where a picture goes on the board: source rectangle → destination rectangle (both in pixels).</summary>
    public readonly struct FitRects
    {
        public readonly double SrcX, SrcY, SrcW, SrcH;
        public readonly int DstX, DstY, DstW, DstH;

        public FitRects(double sx, double sy, double sw, double sh, int dx, int dy, int dw, int dh)
        {
            SrcX = sx; SrcY = sy; SrcW = sw; SrcH = sh;
            DstX = dx; DstY = dy; DstW = dw; DstH = dh;
        }

        public override string ToString() => $"src({SrcX},{SrcY},{SrcW},{SrcH}) -> dst({DstX},{DstY},{DstW},{DstH})";
    }

    public static class ImageFit
    {
        public static FitRects Compute(int srcW, int srcH, int boardW, int boardH, FitMode mode)
        {
            if (srcW <= 0 || srcH <= 0) throw new ArgumentException("empty picture");
            switch (mode)
            {
                case FitMode.Stretch:
                    return new FitRects(0, 0, srcW, srcH, 0, 0, boardW, boardH);
                case FitMode.Cover:
                {
                    double s = Math.Max((double)boardW / srcW, (double)boardH / srcH);
                    double sw = boardW / s, sh = boardH / s;
                    return new FitRects((srcW - sw) / 2, (srcH - sh) / 2, sw, sh, 0, 0, boardW, boardH);
                }
                default:
                {
                    double s = Math.Min((double)boardW / srcW, (double)boardH / srcH);
                    int dw = Math.Max(1, Math.Min(boardW, (int)Math.Round(srcW * s)));
                    int dh = Math.Max(1, Math.Min(boardH, (int)Math.Round(srcH * s)));
                    return new FitRects(0, 0, srcW, srcH, (boardW - dw) / 2, (boardH - dh) / 2, dw, dh);
                }
            }
        }
    }
}
