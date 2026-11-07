using System;
using System.IO;
using System.Windows.Forms;
using KanbanDrawer.UI.Features;

namespace KanbanDrawer.UI
{
    /// <summary>
    /// STUB (m12/layout): the signatures of docs/design/boardfile.md only, so MainForm builds before the save branch.
    /// The save branch replaces this whole file (on an add/add conflict, always take its version).
    /// </summary>
    internal sealed class BoardFileCommands
    {
        readonly EditorHost host;
        readonly Func<bool> isBusy;

        public BoardFileCommands(Form owner, EditorHost host, Settings settings, Func<bool> isBusy)
        {
            this.host = host;
            this.isBusy = isBusy;
        }

        public string Title => "看板屋";
#pragma warning disable CS0067 // never raised by the stub
        public event Action TitleChanged;
#pragma warning restore CS0067
        public bool Dirty => false;
        public string CurrentPath => null;

        public void New() { if (!isBusy()) host.Log("未実装"); }
        public void Open() { if (!isBusy()) host.Log("未実装"); }
        public bool OpenPath(string path) { if (!isBusy()) host.Log("未実装"); return false; }
        public bool Save() { if (!isBusy()) host.Log("未実装"); return false; }
        public bool SaveAs() { if (!isBusy()) host.Log("未実装"); return false; }
        public bool ConfirmDiscard() => true;

        public static string TitleOf(string path, bool dirty) =>
            path == null ? (dirty ? "看板屋 - (無題)*" : "看板屋") : "看板屋 - " + Path.GetFileName(path) + (dirty ? "*" : "");
    }
}
