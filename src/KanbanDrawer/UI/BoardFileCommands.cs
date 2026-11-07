using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using KanbanDrawer.Core.Board;
using KanbanDrawer.UI.Features;
using KanbanDrawer.Win;

namespace KanbanDrawer.UI
{
    /// <summary>
    /// 新規 / 開く / 保存 / 名前を付けて保存 of the edited board (.kanban, docs/design/boardfile.md): the unsaved-changes mark,
    /// the confirmation, the file dialogs, reading / writing with error reports, the last folder, the window title.
    /// MainForm only calls the methods and shows <see cref="Title"/>.
    /// </summary>
    internal sealed class BoardFileCommands
    {
        const string AppTitle = "看板屋";
        const string Filter = "看板屋の看板 (*.kanban)|*.kanban";
        const string FolderKey = "board.folder";

        readonly Form owner;
        readonly EditorHost host;
        readonly Settings settings;
        readonly Func<bool> isBusy;
        bool dirty, loading;

        public BoardFileCommands(Form owner, EditorHost host, Settings settings, Func<bool> isBusy)
        {
            this.owner = owner;
            this.host = host;
            this.settings = settings;
            this.isBusy = isBusy ?? (() => false);
            host.Doc.Changed += MarkDirty;
            host.QuantizeChanged += MarkDirty;
            dirty = false; // settings applied while starting up are not changes
        }

        /// <summary>Window title (<see cref="TitleOf"/>).</summary>
        public string Title => TitleOf(CurrentPath, dirty);
        /// <summary>The title changed (MainForm sets its Text).</summary>
        public event Action TitleChanged;
        /// <summary>There are changes not saved.</summary>
        public bool Dirty => dirty;
        /// <summary>The file opened or saved last, or null.</summary>
        public string CurrentPath { get; private set; }

        public static string TitleOf(string path, bool dirty)
        {
            if (path == null) return dirty ? AppTitle + " - (無題)*" : AppTitle;
            return AppTitle + " - " + Path.GetFileName(path) + (dirty ? "*" : "");
        }

        void MarkDirty()
        {
            if (loading || dirty) return;
            dirty = true;
            TitleChanged?.Invoke();
        }

        void SetState(string path, bool isDirty)
        {
            CurrentPath = path;
            dirty = isDirty;
            TitleChanged?.Invoke();
        }

        /// <summary>新規: after the confirmation, an empty board (the quantize settings stay).</summary>
        public void New()
        {
            if (isBusy() || !ConfirmDiscard()) return;
            loading = true;
            try { host.Doc.Load(BoardContent.Empty()); }
            finally { loading = false; }
            SetState(null, false);
            host.Log("新しい看板にしました");
        }

        /// <summary>開く: after the confirmation, choose a file and read it.</summary>
        public void Open()
        {
            if (isBusy() || !ConfirmDiscard()) return;
            using (var d = new OpenFileDialog { Filter = Filter, InitialDirectory = InitialFolder() })
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return;
                Load(d.FileName);
            }
        }

        /// <summary>A dropped .kanban file / the exe's argument: confirmation, then read. True when it was opened.</summary>
        public bool OpenPath(string path)
        {
            if (isBusy() || !ConfirmDiscard()) return false;
            return Load(path);
        }

        /// <summary>保存: overwrites the current file, or asks for a name when there is none. True when saved.</summary>
        public bool Save()
        {
            if (isBusy()) return false;
            return CurrentPath == null ? SaveAs() : Write(CurrentPath);
        }

        /// <summary>名前を付けて保存. True when saved.</summary>
        public bool SaveAs()
        {
            if (isBusy()) return false;
            using (var d = new SaveFileDialog
            {
                Filter = Filter,
                DefaultExt = BoardFile.Extension.TrimStart('.'),
                AddExtension = true,
                FileName = CurrentPath != null ? Path.GetFileName(CurrentPath) : "看板" + BoardFile.Extension,
                InitialDirectory = CurrentPath != null ? Path.GetDirectoryName(CurrentPath) : InitialFolder(),
            })
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return false;
                return Write(d.FileName);
            }
        }

        /// <summary>Before closing / replacing the board: true = go on (nothing to save, saved, or discarded).</summary>
        public bool ConfirmDiscard()
        {
            if (!dirty) return true;
            string name = CurrentPath != null ? Path.GetFileName(CurrentPath) : "(無題)";
            var answer = MessageBox.Show(owner, $"「{name}」は変更されています。保存しますか？", AppTitle,
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Yes) return Save();
            return answer == DialogResult.No;
        }

        bool Load(string path)
        {
            BoardContent content;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    content = BoardFile.Read(fs, new PngCodec(), host.Text, FontResolver());
            }
            catch (BoardFileException e) { return Fail(e.Message); }
            catch (InvalidDataException e) { return Fail(BoardFileException.Corrupt(e.Message).Message); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException
                                      || e is NotSupportedException)
            {
                return Fail("ファイルを開けません: " + e.Message);
            }

            loading = true;
            try
            {
                host.Doc.Load(content);
                if (content.Quantize != null) host.ApplyQuantize(content.Quantize);
            }
            finally { loading = false; }
            RememberFolder(path);
            host.Log("開きました: " + path);
            foreach (var w in content.Warnings) host.Log(w);
            SetState(path, false);
            return true;
        }

        bool Fail(string message)
        {
            host.Log(message);
            MessageBox.Show(owner, message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        /// <summary>Writes everything to &lt;path&gt;.tmp first, then replaces the file (a failure leaves the old file whole).</summary>
        bool Write(string path)
        {
            string tmp = path + ".tmp";
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                    BoardFile.Write(host.Doc, host.Quantize, new PngCodec(), fs);
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch (Exception e)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
                string message = "保存できませんでした: " + e.Message;
                host.Log(message);
                MessageBox.Show(owner, message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            RememberFolder(path);
            host.Log("保存しました: " + path);
            SetState(path, false);
            return true;
        }

        /// <summary>Font name → the one to use: installed as it is, else the default font (the list is read once per file).</summary>
        static Func<string, string> FontResolver()
        {
            HashSet<string> installed = null;
            string fallback = null;
            return n =>
            {
                if (installed == null) installed = new HashSet<string>(GdiTextRasterizer.InstalledFonts());
                if (installed.Contains(n)) return n;
                return fallback ?? (fallback = GdiTextRasterizer.DefaultFont());
            };
        }

        string InitialFolder()
        {
            string dir = settings.Get(FolderKey, "");
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        void RememberFolder(string path)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) settings.Set(FolderKey, dir);
        }
    }
}
