using System;
using System.Drawing;
using System.Windows.Forms;
using KanbanDrawer.Core;
using KanbanDrawer.Core.Board;
using KanbanDrawer.UI.Widgets;
using KanbanDrawer.Win;

namespace KanbanDrawer.UI.Features
{
    /// <summary>
    /// 下地: the bottom layer. A background palette colour and, chosen separately, an optional registered frame: a
    /// picture placed on 320x240 when it is registered (background removal if ticked, full colour, transparency kept)
    /// and kept in the frame library. Its colours are quantized with the draft like pictures and stamps. The background
    /// colour shows through the frame's transparent pixels and the picture's margins.
    /// </summary>
    internal sealed class FrameFeature : IFeature
    {
        const string None = "";
        EditorHost host;
        readonly PaletteSwatches color = new PaletteSwatches(false, Palette.White);
        readonly ComboBox fit = Ui.DropDown("トリミングして埋める", "全体を収める", "引き伸ばす");
        readonly CheckBox removeBg = new CheckBox { Text = "背景を消す", AutoSize = true, Checked = false };
        readonly ThumbnailList list = new ThumbnailList(new Size(84, 65)) { Height = 230 };
        string folder = "";

        static readonly FitMode[] FitModes = { FitMode.Cover, FitMode.Contain, FitMode.Stretch };

        public string Title => "下地";

        public Control CreatePanel(EditorHost h)
        {
            host = h;
            var p = new StackPanel();
            p.Controls.Add(Ui.Heading("背景色"));
            p.Controls.Add(Ui.Row("色", color));
            p.Controls.Add(Ui.Note("フレームの透明部分や、何も置いていない所に見える色です。", 18));
            p.Controls.Add(Ui.Heading("フレーム"));
            p.Controls.Add(list);
            p.Controls.Add(Ui.Buttons(Ui.Button("フレームを登録...", Register), removeBg));
            var del = new LibraryDelete(host, host.Frames, list, "フレーム", Reload);
            p.Controls.Add(Ui.Buttons(del.DeleteButton, del.UndoButton));
            p.Controls.Add(Ui.Row("合わせ方", fit));
            p.Controls.Add(Ui.Note("登録時に 320×240 にして保存します。色は下書きのときに、画像・スタンプと一緒に 10 色にします。透明な部分（「全体を収める」の余白も）は透明のまま残ります。\nCtrl / Shift+クリックで選んで、右クリック・Delete キーで削除できます。", 66));
            color.SelectedChanged += (s, e) =>
            {
                host.EndCheckpointBurst();
                host.Doc.Checkpoint();
                host.Doc.BackgroundColor = color.Selected;
                host.Doc.NotifyChanged();
            };
            list.ItemClicked += Select;
            host.Doc.Changed += SyncFromDoc; // undo / redo
            Reload();
            return p;
        }

        public bool Owns(BoardObject o) => false;
        public void OnSelection(BoardObject o) { }

        public void Load(Settings s)
        {
            int c = Math.Max(0, Math.Min(Palette.Count - 1, s.Get("frame.color", Palette.White)));
            color.SetSilently(c);
            host.Doc.BackgroundColor = c;
            fit.SelectedIndex = Math.Max(0, Math.Min(2, s.Get("frame.fit", 0)));
            folder = s.Get("frame.folder", "");
            removeBg.Checked = s.Get("frame.removeBg", 0) != 0;
        }

        public void Save(Settings s)
        {
            s.Set("frame.color", color.Selected);
            s.Set("frame.fit", fit.SelectedIndex);
            s.Set("frame.folder", folder);
            s.Set("frame.removeBg", removeBg.Checked ? 1 : 0);
        }

        void SyncFromDoc()
        {
            color.SetSilently(host.Doc.BackgroundColor);
            list.Selected = host.Doc.FrameId ?? None;
        }

        void Reload()
        {
            list.Clear();
            list.Add(None, null, "フレーム\nなし", false);
            foreach (var id in host.Frames.Ids())
            {
                try
                {
                    var r = host.Frames.Load(id);
                    list.Add(id, ImageLoader.ToBitmap(r), null, true);
                }
                catch (Exception e) { host.Log($"フレーム {id} を読めませんでした: {e.Message}"); }
            }
            list.Selected = host.Doc.FrameId ?? None;
        }

        void Select(string id)
        {
            if ((host.Doc.FrameId ?? None) == id) return;
            Raster frame = null;
            if (id != None)
            {
                try { frame = host.Frames.Load(id); }
                catch (Exception e) { host.Log("フレームを読めませんでした: " + e.Message); return; }
                if (frame.Width != BoardDoc.W || frame.Height != BoardDoc.H)
                {
                    host.Log($"フレームが 320×240 ではありません: {frame.Width}×{frame.Height}");
                    return;
                }
            }
            host.EndCheckpointBurst();
            host.Doc.Checkpoint();
            host.Doc.Frame = frame;
            host.Doc.FrameId = id == None ? null : id;
            list.Selected = id;
            host.Doc.NotifyChanged();
        }

        void Register()
        {
            string path;
            using (var d = new OpenFileDialog { Filter = "画像|*.png;*.jpg;*.jpeg;*.bmp;*.gif|すべて|*.*", InitialDirectory = folder })
            {
                if (d.ShowDialog(host.Owner) != DialogResult.OK) return;
                path = d.FileName;
            }
            try
            {
                string id = host.Frames.Register(path, FitModes[fit.SelectedIndex], removeBg.Checked);
                folder = System.IO.Path.GetDirectoryName(path);
                host.Log($"フレームを登録しました: {System.IO.Path.GetFileName(path)}");
                Reload();
                Select(id);
            }
            catch (Exception e)
            {
                MessageBox.Show(host.Owner, "フレームを登録できませんでした: " + e.Message, "下地");
            }
        }
    }
}
