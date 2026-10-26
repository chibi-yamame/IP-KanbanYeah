using System;
using System.Collections.Generic;
using System.Windows.Forms;
using KanbanDrawer.UI.Widgets;
using KanbanDrawer.Win;

namespace KanbanDrawer.UI.Features
{
    /// <summary>
    /// Deleting from a library list, shared by スタンプ and 下地: no confirmation, 「選んだものを削除」 (with the count) and
    /// 「削除を元に戻す」 (the last deletion). Right click 削除 and the Delete key come from <see cref="ThumbnailList"/>.
    /// Empties the library's trash when created (startup), so only deletions of this session can be restored.
    /// </summary>
    internal sealed class LibraryDelete
    {
        readonly EditorHost host;
        readonly ImageLibrary library;
        readonly ThumbnailList list;
        readonly string noun;
        readonly Action reload;
        DeletionRecord last;

        public readonly Button DeleteButton;
        public readonly Button UndoButton;

        /// <param name="noun">「スタンプ」 / 「フレーム」 for the log</param>
        /// <param name="reload">refills the list after a change</param>
        public LibraryDelete(EditorHost host, ImageLibrary library, ThumbnailList list, string noun, Action reload)
        {
            this.host = host; this.library = library; this.list = list; this.noun = noun; this.reload = reload;
            library.EmptyTrash();
            DeleteButton = Ui.Button("選んだものを削除", () => Delete(list.CheckedIds));
            UndoButton = Ui.Button("削除を元に戻す", Undo);
            list.DeleteRequested += Delete;
            list.CheckedChanged += Sync;
            Sync();
        }

        void Sync()
        {
            int n = list.CheckedIds.Count;
            DeleteButton.Text = n > 0 ? $"選んだものを削除（{n}）" : "選んだものを削除";
            DeleteButton.Enabled = n > 0;
            UndoButton.Enabled = library.CanRestore(last);
        }

        void Delete(IReadOnlyList<string> ids)
        {
            if (ids.Count == 0) return;
            last = null;
            try { last = library.Delete(ids); }
            catch (Exception e) { host.Log($"{noun}を削除できませんでした: {e.Message}"); }
            reload();
            if (last != null && last.Count > 0) host.Log($"{noun}を {last.Count} 個削除しました（看板に置いたものは残ります）");
            Sync();
        }

        void Undo()
        {
            List<string> back;
            try { back = library.Restore(last); }
            catch (Exception e) { host.Log($"{noun}を元に戻せませんでした: {e.Message}"); back = new List<string>(); }
            last = null;
            reload();
            host.Log(back.Count > 0 ? $"{noun}を {back.Count} 個元に戻しました" : $"{noun}を元に戻せませんでした");
            Sync();
        }
    }
}
