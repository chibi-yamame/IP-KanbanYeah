using System;
using System.Drawing;
using System.Windows.Forms;

namespace KanbanDrawer.UI
{
    /// <summary>
    /// 「使い方」: a small non-modal window with the how-to text only (docs/design/ui.md). One at a time (MainForm brings an
    /// open one to the front), Esc closes it, it closes with its owner, nothing is remembered.
    /// </summary>
    internal sealed class UsageForm : Form
    {
        /// <summary>The whole text. What moved from the screen to a ToolTip is always written here too (ToolTips are hard to find).</summary>
        public const string UsageText =
            "看板を作る\r\n" +
            "・タブは下から重ねる順です: 下地 → 画像 → スタンプ → 文字。減色は「下書き」の作り方です。\r\n" +
            "・画像: ボタン、またはウィンドウへのドラッグ&ドロップで開きます。何枚でも置けます。余白・透明部分には下地が見えます。\r\n" +
            "・貼り付け: Ctrl+V（または上の帯の「貼り付け」）で、クリップボードの画像・文字を置きます。\r\n" +
            "\r\n" +
            "看板の上の操作\r\n" +
            "・クリックで選択。ドラッグで移動、角で拡大縮小、上の丸で回転（Shift で 15° ずつ）。\r\n" +
            "・矢印キーで 1px（Shift で 10px）移動、Delete で削除、Ctrl+Z / Ctrl+Y で元に戻す / やり直し。\r\n" +
            "・公式と同じ文字は、移動だけできます。\r\n" +
            "\r\n" +
            "ライブラリ（スタンプ・フレーム）\r\n" +
            "・クリックで使います。Ctrl+クリックで選びを切り替え、Shift+クリックで範囲、Ctrl+A ですべて選びます。\r\n" +
            "・Delete キー、または右クリックの「削除」で消します。右クリックの「削除を元に戻す」で、直前の 1 回分を戻せます（確認は出ません）。\r\n" +
            "・スタンプは 320×240 より大きい画像は登録できません。複数まとめて選べます。\r\n" +
            "・フレームは登録時に 320×240 にして保存します。色は下書きのときに 10 色にします。\r\n" +
            "\r\n" +
            "描く\r\n" +
            "・「下書きを作る」で、実際に描く 10 色の絵と本数・目安時間を確認できます。描画開始でも自動で作ります。\r\n" +
            "・InterPot で自分の看板の編集画面を開き、「描画開始」。2 秒後に始まります。Esc でいつでも中止できます。\r\n" +
            "・中断したときや違う画素が残ったときは「続きから / 手直し」。\r\n" +
            "・描いたあとの「保存」は InterPot 側でご自身で押してください。\r\n" +
            "\r\n" +
            "保存と開く\r\n" +
            "・看板屋の「保存」は、いま編集している看板（.kanban）を保存します。「開く」で続きから編集できます。\r\n" +
            "・.kanban ファイルをウィンドウにドロップしても開けます。\r\n" +
            "\r\n" +
            "フルカラー（実験的）\r\n" +
            "・入力方式を「フルカラー」にして「InterPot を起動」から起動すると、10 色以外の色も含めてそのまま書き込みます。\r\n";

        public UsageForm(Form owner)
        {
            Text = "使い方";
            Font = owner.Font;
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false;
            ClientSize = new Size(460, 420);
            MinimumSize = new Size(320, 240);
            KeyPreview = true;
            Owner = owner;
            // CenterParent only centres a modal dialog: centred on the owner by hand (kept on its screen)
            StartPosition = FormStartPosition.Manual;
            var area = Screen.FromControl(owner).WorkingArea;
            var at = new Point(owner.Left + (owner.Width - Width) / 2, owner.Top + (owner.Height - Height) / 2);
            Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, at.X)), Math.Max(area.Top, Math.Min(area.Bottom - Height, at.Y)));

            var body = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true,
                BackColor = SystemColors.Window, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill,
                Text = UsageText, TabStop = false,
            };
            var frame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8), BackColor = SystemColors.Window };
            frame.Controls.Add(body);
            Controls.Add(frame);
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; } };
            // a read-only box selects all its text when it first gets focus: nothing should look selected
            Shown += (s, e) => { body.SelectionStart = 0; body.SelectionLength = 0; };
        }
    }
}
