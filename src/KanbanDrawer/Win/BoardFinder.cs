using System;
using System.Collections.Generic;
using System.Text;
using KanbanDrawer.Core;

namespace KanbanDrawer.Win
{
    public sealed class BoardTarget
    {
        /// <summary>Canvas top-left in screen coordinates (physical pixels).</summary>
        public Pt Origin;
        /// <summary>Top-level window under the canvas (the game frame).</summary>
        public IntPtr Window;
        public string WindowTitle;
    }

    /// <summary>Finds the signboard editor on screen.</summary>
    public static class BoardFinder
    {
        /// <summary>Returns the editor, or null with <paramref name="error"/> set (none / several found).</summary>
        public static BoardTarget Find(out string error)
        {
            var shot = ScreenCapture.CaptureAll(out int left, out int top);
            List<Pt> hits = BoardLocator.Find(shot);
            if (hits.Count == 0)
            {
                error = "看板エディタが見つかりません。InterPot で看板の編集画面を開き、パレットとキャンバスが隠れないようにしてください。";
                return null;
            }
            if (hits.Count > 1)
            {
                error = $"看板エディタらしきものが {hits.Count} 個見つかりました。1 つだけ表示してください。";
                return null;
            }
            var o = hits[0].Offset(left, top);
            var root = InputDriver.RootAt(o.X + BoardGeometry.Width / 2, o.Y + BoardGeometry.Height / 2);
            var sb = new StringBuilder(256);
            Native.GetWindowText(root, sb, sb.Capacity);
            error = null;
            return new BoardTarget { Origin = o, Window = root, WindowTitle = sb.ToString() };
        }
    }
}
