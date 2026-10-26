using System;
using System.IO;
using System.Windows.Forms;
using KanbanDrawer.Core;
using KanbanDrawer.Core.Board;
using KanbanDrawer.UI.Widgets;
using KanbanDrawer.Win;

namespace KanbanDrawer.UI.Features
{
    /// <summary>
    /// 画像: pictures opened from files (not registered) and placed on the board as objects, behind stamps and text.
    /// Placed by 置き方 (fit / cover / dot by dot), then moved, rotated and scaled like stamps.
    /// </summary>
    internal sealed class PictureFeature : IFeature, IAcceptsFiles, IAcceptsPaste
    {
        static readonly PictureFit[] Fits = { PictureFit.Contain, PictureFit.Cover, PictureFit.Actual };

        EditorHost host;
        string folder = "";
        readonly ComboBox fit = Ui.DropDown("全体を収める", "トリミングして埋める", "等倍");
        readonly Label name = new Label { Height = 20, AutoEllipsis = true };
        readonly ResetSlider size = new ResetSlider(1, 400, 100) { Unit = "%" };
        readonly ResetSlider angle = new ResetSlider(-180, 180, 0) { Unit = "°" };
        Control selectedBox;
        PictureObject current;

        public string Title => "画像";

        public Control CreatePanel(EditorHost h)
        {
            host = h;
            var p = new StackPanel();
            p.Controls.Add(Ui.Note("画像を看板に置きます（スタンプ・文字の後ろ）。何枚でも置けます。余白・透明部分には下地が見えます。", 34));
            p.Controls.Add(Ui.Buttons(Ui.Button("画像を開く...", Open)));
            p.Controls.Add(Ui.Row("置き方", fit));
            p.Controls.Add(Ui.Note("ウィンドウへのドラッグ&ドロップでも開けます。", 18));

            var box = new StackPanel { Height = 224, Padding = new Padding(0), AutoScroll = false };
            box.Controls.Add(Ui.Heading("選択中の画像"));
            box.Controls.Add(name);
            box.Controls.Add(Ui.Row("大きさ", size));
            box.Controls.Add(Ui.Row("回転", angle));
            box.Controls.Add(Ui.Buttons(Ui.Button("看板に合わせる", Refit)));
            box.Controls.Add(Ui.Buttons(
                Ui.Button("前面へ", () => host.Doc.Restack(current, +1)),
                Ui.Button("背面へ", () => host.Doc.Restack(current, -1)),
                Ui.Button("看板から削除", () => host.Doc.Remove(current))));
            box.Controls.Add(Ui.Note("プレビュー上: ドラッグで移動、角で拡縮、上の丸で回転（Shift で 15° ずつ）。", 34));
            selectedBox = box;
            p.Controls.Add(box);

            size.ValueChanged += (s, e) => Edit(() => current.Scale = size.Value / 100.0);
            angle.ValueChanged += (s, e) => Edit(() => current.Rot = angle.Value * Math.PI / 180);
            host.Doc.Changed += () => { if (current != null) Show(current); };
            OnSelection(null);
            return p;
        }

        public bool Owns(BoardObject o) => o is PictureObject;

        public void OnSelection(BoardObject o)
        {
            current = o as PictureObject;
            selectedBox.Enabled = current != null;
            if (current != null) Show(current);
            else name.Text = "";
        }

        public void Load(Settings s)
        {
            fit.SelectedIndex = Math.Max(0, Math.Min(Fits.Length - 1, s.Get("picture.fit", 0)));
            folder = s.Get("folder", "");
        }

        public void Save(Settings s)
        {
            s.Set("picture.fit", fit.SelectedIndex);
            s.Set("folder", folder);
        }

        /// <summary>100% = the loaded picture's own pixels (dot by dot).</summary>
        void Show(PictureObject o)
        {
            name.Text = $"{o.FileName} ({o.Picture.Width}×{o.Picture.Height})";
            size.SetSilently((int)Math.Round(o.Scale * 100));
            angle.SetSilently((int)Math.Round(o.Rot * 180 / Math.PI));
        }

        void Edit(Action change)
        {
            if (current == null) return;
            host.CheckpointFor(current);
            change();
            current.ClampScale();
            host.Doc.NotifyChanged();
        }

        void Open()
        {
            using (var d = new OpenFileDialog { Filter = "画像|*.png;*.jpg;*.jpeg;*.bmp;*.gif|すべて|*.*", InitialDirectory = folder })
                if (d.ShowDialog(host.Owner) == DialogResult.OK) OpenFile(d.FileName);
        }

        /// <summary>Loads a picture (long side shrunk to 1280 if larger) and places it at the very back, selected.</summary>
        public bool OpenFile(string path)
        {
            Raster loaded;
            try
            {
                using (var bmp = ImageLoader.Load(path)) loaded = ImageLoader.ToRaster(bmp);
            }
            catch (Exception e)
            {
                MessageBox.Show(host.Owner, "画像を開けませんでした: " + e.Message, "画像");
                return false;
            }
            folder = Path.GetDirectoryName(path);
            Place(loaded, Path.GetFileName(path));
            return true;
        }

        /// <summary>Pasted picture data: placed like an opened file; the open folder stays as it is.</summary>
        public bool PasteImage(Raster image, string name)
        {
            if (image == null) return false;
            Place(image, name);
            return true;
        }

        public bool PasteText(string text) => false;

        /// <summary>Shrinks (long side 1280), places by 置き方 and inserts at the very back, selected: one undo step.</summary>
        void Place(Raster loaded, string file)
        {
            var pic = PictureObject.Shrink(loaded);
            host.Log(pic == loaded
                ? $"画像: {file} ({pic.Width}×{pic.Height})"
                : $"画像: {file} ({loaded.Width}×{loaded.Height} → {pic.Width}×{pic.Height} に縮小)");
            var o = new PictureObject(file, pic);
            o.FitTo(Fits[fit.SelectedIndex]);
            o.ClampScale();
            host.EndCheckpointBurst();
            host.Doc.Insert(0, o); // behind stamps and text
        }

        /// <summary>看板に合わせる: places the selected picture again with the current 置き方.</summary>
        void Refit()
        {
            if (current == null) return;
            host.EndCheckpointBurst();
            host.Doc.Checkpoint();
            current.FitTo(Fits[fit.SelectedIndex]);
            current.ClampScale();
            host.Doc.NotifyChanged();
        }
    }
}
