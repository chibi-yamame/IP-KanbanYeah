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
    /// スタンプ: pictures registered once (background removal, trimmed, long side ≤ 512) and placed on the board as
    /// objects that can be moved, rotated and scaled (on the preview or with the sliders).
    /// </summary>
    internal sealed class StampFeature : IFeature
    {
        EditorHost host;
        readonly ThumbnailList list = new ThumbnailList(new Size(64, 64)) { Height = 150 };
        readonly CheckBox removeBg = new CheckBox { Text = "背景を消す", AutoSize = true, Checked = false };
        readonly ResetSlider size = new ResetSlider(5, 400, 100) { Unit = "%" };
        readonly ResetSlider angle = new ResetSlider(-180, 180, 0) { Unit = "°" };
        Control selectedBox;
        StampObject current;
        string folder = "";

        public string Title => "スタンプ";

        public Control CreatePanel(EditorHost h)
        {
            host = h;
            var p = new StackPanel();
            p.Controls.Add(Ui.Note("クリックで看板の中央に等倍（100%）で置きます。\nCtrl / Shift+クリックで選んで、右クリック・Delete キーで削除。", 34));
            p.Controls.Add(list);
            p.Controls.Add(Ui.Buttons(Ui.Button("スタンプを登録...", Register), removeBg));
            var del = new LibraryDelete(host, host.Stamps, list, "スタンプ", Reload);
            p.Controls.Add(Ui.Buttons(del.DeleteButton, del.UndoButton));
            p.Controls.Add(Ui.Note("複数の画像をまとめて選べます。320×240 より大きい画像は登録しません。", 18));

            var box = new StackPanel { Height = 170, Padding = new Padding(0), AutoScroll = false };
            box.Controls.Add(Ui.Heading("選択中のスタンプ"));
            box.Controls.Add(Ui.Row("大きさ", size));
            box.Controls.Add(Ui.Row("回転", angle));
            box.Controls.Add(Ui.Buttons(
                Ui.Button("前面へ", () => host.Doc.Restack(current, +1)),
                Ui.Button("背面へ", () => host.Doc.Restack(current, -1)),
                Ui.Button("看板から削除", () => host.Doc.Remove(current))));
            box.Controls.Add(Ui.Note("プレビュー上: ドラッグで移動、角で拡縮、上の丸で回転（Shift で 15° ずつ）。", 34));
            selectedBox = box;
            p.Controls.Add(box);

            list.ItemClicked += Place;
            size.ValueChanged += (s, e) => Edit(() => current.Scale = size.Value / 100.0);
            angle.ValueChanged += (s, e) => Edit(() => current.Rot = angle.Value * Math.PI / 180);
            host.Doc.Changed += () => { if (current != null) Show(current); };
            Reload();
            OnSelection(null);
            return p;
        }

        public bool Owns(BoardObject o) => o is StampObject;

        public void OnSelection(BoardObject o)
        {
            current = o as StampObject;
            selectedBox.Enabled = current != null;
            if (current != null) Show(current);
        }

        public void Load(Settings s)
        {
            removeBg.Checked = s.Get("stamp.removeBg2", 0) != 0; // new key: the default changed to off
            folder = s.Get("stamp.folder", "");
        }

        public void Save(Settings s)
        {
            s.Set("stamp.removeBg2", removeBg.Checked ? 1 : 0);
            s.Set("stamp.folder", folder);
        }

        /// <summary>100% = the picture's own pixels (dot by dot).</summary>
        void Show(StampObject o)
        {
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

        void Reload()
        {
            list.Clear();
            foreach (var id in host.Stamps.Ids())
            {
                try { list.Add(id, ImageLoader.ToBitmap(host.Stamps.Load(id)), null, true); }
                catch (Exception e) { host.Log($"スタンプ {id} を読めませんでした: {e.Message}"); }
            }
        }

        void Place(string id)
        {
            Raster pic;
            try { pic = host.Stamps.Load(id); }
            catch (Exception e) { host.Log("スタンプを読めませんでした: " + e.Message); return; }
            host.AddObject(new StampObject(id, pic) { Scale = 1 }); // dot by dot
        }

        /// <summary>
        /// Registers one or more pictures (multi-select). Each is prepared (background removal if ticked, trimmed); those
        /// larger than the board are skipped and reported. A single new stamp is placed right away.
        /// </summary>
        void Register()
        {
            string[] paths;
            using (var d = new OpenFileDialog { Filter = "画像|*.png;*.jpg;*.jpeg;*.bmp;*.gif|すべて|*.*", InitialDirectory = folder, Multiselect = true })
            {
                if (d.ShowDialog(host.Owner) != DialogResult.OK) return;
                paths = d.FileNames;
            }
            var added = new System.Collections.Generic.List<string>();
            var skipped = new System.Collections.Generic.List<string>();
            foreach (var path in paths)
            {
                string name = System.IO.Path.GetFileName(path);
                try
                {
                    Raster src;
                    using (var bmp = ImageLoader.Load(path)) src = ImageLoader.ToRaster(bmp);
                    var prepared = StampPrep.Prepare(src, removeBg.Checked);
                    if (!StampPrep.FitsBoard(prepared))
                    {
                        skipped.Add($"{name}（{prepared.Width}×{prepared.Height}、大きすぎ）");
                        continue;
                    }
                    added.Add(host.Stamps.Add(prepared));
                    host.Log($"スタンプを登録しました: {name} ({prepared.Width}×{prepared.Height})");
                }
                catch (Exception e)
                {
                    skipped.Add($"{name}（{e.Message}）");
                }
            }
            if (paths.Length > 0) folder = System.IO.Path.GetDirectoryName(paths[0]);
            foreach (var s in skipped) host.Log("スタンプにしませんでした: " + s);
            if (added.Count > 0) Reload();
            if (added.Count == 1) Place(added[0]);
            if (skipped.Count > 0)
                MessageBox.Show(host.Owner, $"{added.Count} 個登録しました。次の {skipped.Count} 個は登録していません（320×240 まで）:\n"
                    + string.Join("\n", skipped), "スタンプ");
        }
    }
}
