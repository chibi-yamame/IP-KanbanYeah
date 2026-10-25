using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using KanbanDrawer.Core;
using KanbanDrawer.Core.Board;
using KanbanDrawer.UI.Widgets;
using KanbanDrawer.Win;

namespace KanbanDrawer.UI.Features
{
    /// <summary>画像: one picture over the whole board (fit / cover / stretch); margins show the background.</summary>
    internal sealed class PictureFeature : IFeature, IAcceptsFiles
    {
        EditorHost host;
        Bitmap source;
        string folder = "";
        readonly Label name = new Label { Height = 20, AutoEllipsis = true };
        readonly ComboBox fit = Ui.DropDown("全体を収める", "トリミングして埋める", "引き伸ばす");
        Button remove;

        public string Title => "画像";

        public Control CreatePanel(EditorHost h)
        {
            host = h;
            var p = new StackPanel();
            p.Controls.Add(Ui.Note("看板全体に 1 枚の画像を置きます。余白・透明部分には下地が見えます。", 34));
            remove = Ui.Button("画像を外す", Remove);
            p.Controls.Add(Ui.Buttons(Ui.Button("画像を開く...", Open), remove));
            p.Controls.Add(name);
            p.Controls.Add(Ui.Row("合わせ方", fit));
            p.Controls.Add(Ui.Note("ウィンドウへのドラッグ&ドロップでも開けます。", 18));
            fit.SelectedIndexChanged += (s, e) => Place(true);
            Update();
            return p;
        }

        public bool Owns(BoardObject o) => false;
        public void OnSelection(BoardObject o) { }

        public void Load(Settings s)
        {
            fit.SelectedIndex = Math.Max(0, Math.Min(2, s.Get("fit", 0)));
            folder = s.Get("folder", "");
        }

        public void Save(Settings s)
        {
            s.Set("fit", fit.SelectedIndex);
            s.Set("folder", folder);
        }

        void Open()
        {
            using (var d = new OpenFileDialog { Filter = "画像|*.png;*.jpg;*.jpeg;*.bmp;*.gif|すべて|*.*", InitialDirectory = folder })
                if (d.ShowDialog(host.Owner) == DialogResult.OK) OpenFile(d.FileName);
        }

        public bool OpenFile(string path)
        {
            Bitmap bmp;
            try { bmp = ImageLoader.Load(path); }
            catch (Exception e)
            {
                MessageBox.Show(host.Owner, "画像を開けませんでした: " + e.Message, "画像");
                return false;
            }
            source?.Dispose();
            source = bmp;
            folder = Path.GetDirectoryName(path);
            name.Text = $"{Path.GetFileName(path)} ({bmp.Width}×{bmp.Height})";
            host.Log($"画像: {name.Text}");
            Place(true);
            return true;
        }

        void Place(bool undoable)
        {
            if (source == null) return;
            if (undoable) { host.EndCheckpointBurst(); host.Doc.Checkpoint(); }
            host.Doc.Picture = ImageLoader.Fit(source, (FitMode)fit.SelectedIndex);
            Update();
            host.Doc.NotifyChanged();
        }

        void Remove()
        {
            if (host.Doc.Picture == null) return;
            host.EndCheckpointBurst();
            host.Doc.Checkpoint();
            host.Doc.Picture = null;
            source?.Dispose();
            source = null;
            name.Text = "";
            Update();
            host.Doc.NotifyChanged();
        }

        void Update()
        {
            if (remove != null) remove.Enabled = source != null;
        }
    }
}
