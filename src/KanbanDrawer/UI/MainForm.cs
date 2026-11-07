using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using KanbanDrawer.Core;
using KanbanDrawer.UI.Features;
using KanbanDrawer.UI.Widgets;
using KanbanDrawer.Win;

namespace KanbanDrawer.UI
{
    /// <summary>
    /// Top: the strip (file, edit, PNG, 使い方). Left: the board (1:1 editor), the draft and the drawing controls (描く操作).
    /// Right: one tab per feature (FeatureRegistry). The drawing part is not a feature: it always works on whatever the
    /// features produced. Opening / saving a board is <see cref="BoardFileCommands"/> (docs/design/boardfile.md).
    /// </summary>
    internal sealed class MainForm : Form
    {
        // input modes and the cost per stroke measured on the mock board (docs/design/drawing.md)
        // the third mode does not draw with the pen: the game's agent writes the composite (docs/design/fullcolor.md)
        static readonly (string Name, InputMode? Mode, double StrokeMs)[] Modes =
        {
            ("バックグラウンド（推奨）", InputMode.Post, 2.3),
            ("マウス操作（互換・遅い）", InputMode.Send, 18),
            ("フルカラー（直接書き込み・実験的）", null, 0),
        };

        const string AppTitle = "看板屋";
        const int CountdownSeconds = 2;
        const string DrawTip = "看板の編集画面を開いて押します。2 秒後に始まり、Esc でいつでも中止できます";
        const string FullColorDrawTip = "フルカラーで書き込みます（10 色に減色しません。下書きは使いません）";
        // 描画開始 is green (the app icon's), the same button is a red 中止 while drawing (docs/design/ui.md)
        static readonly Color Green = ColorTranslator.FromHtml("#1F8C3A"), GreenOver = ColorTranslator.FromHtml("#197530"), GreenDown = ColorTranslator.FromHtml("#14602A");
        static readonly Color Red = ColorTranslator.FromHtml("#C0392B"), RedOver = ColorTranslator.FromHtml("#A93226"), RedDown = ColorTranslator.FromHtml("#922B21");

        readonly Settings settings = Settings.Load();
        readonly EditorHost host;
        readonly BoardFileCommands files;
        readonly List<IFeature> features = FeatureRegistry.Create();
        readonly Dictionary<IFeature, TabPage> pages = new Dictionary<IFeature, TabPage>();
        readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill };
        readonly EditorView view;
        // the strip
        readonly ToolStrip strip = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, RenderMode = ToolStripRenderMode.System, ShowItemToolTips = true };
        readonly ToolStripButton newBoard = StripButton("新規", "新しい看板を始める（Ctrl+N）");
        readonly ToolStripButton openBoard = StripButton("開く", "保存した看板を開く（Ctrl+O）");
        readonly ToolStripSplitButton saveBoard = new ToolStripSplitButton("保存") { DisplayStyle = ToolStripItemDisplayStyle.Text, AutoToolTip = false, ToolTipText = "看板を保存する（Ctrl+S）" };
        readonly ToolStripButton undo = StripButton("元に戻す", null);
        readonly ToolStripButton redo = StripButton("やり直し", null);
        readonly ToolStripButton paste = StripButton("貼り付け", "クリップボードの画像・テキストを看板に置く（Ctrl+V）");
        readonly ToolStripButton exportPng = StripButton("PNG に書き出す", "描く 10 色の絵を PNG に保存する");
        readonly ToolStripButton usage = StripButton("使い方", "操作のしかた");
        // the left panel
        readonly Button makeDraft = new Button { Text = "下書きを作る", AutoSize = true, Font = new Font("Yu Gothic UI", 9f, FontStyle.Bold) };
        readonly RadioButton colorView = new RadioButton { Text = "カラー", AutoSize = true, Checked = true };
        readonly RadioButton draftView = new RadioButton { Text = "下書き（10 色）", AutoSize = true };
        readonly Control viewRow;
        readonly Label stats = new Label { Height = 22, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        readonly GroupBox drawBox = new GroupBox { Text = "描く操作", Margin = new Padding(0, 2, 0, 2) };
        readonly StackPanel drawRows = new StackPanel { Dock = DockStyle.Fill, Padding = new Padding(4, 0, 4, 0), AutoScroll = false };
        readonly ComboBox inputMode = Ui.DropDown(Modes.Select(m => m.Name).ToArray());
        readonly Button detect = new Button { Text = "看板を検出", AutoSize = true };
        readonly Button draw = new Button { Text = "描画開始", Height = 36, Margin = new Padding(0, 2, 0, 2), Font = new Font("Yu Gothic UI", 10.5f, FontStyle.Bold), FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false };
        readonly Label boardState = new Label { Text = "看板: 未検出", AutoSize = true, Anchor = AnchorStyles.Left };
        readonly Button resume = new Button { Text = "続きから / 手直し", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Height = 24, Anchor = AnchorStyles.Right };
        readonly Control boardRow;
        readonly ProgressBar bar = new ProgressBar { Height = 14 };
        readonly TextBox log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        // full colour: the link's state row
        readonly Label agentDot = new Label { Text = "●", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 2, 0) };
        readonly Label agentState = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
        readonly Button launchGame = new Button { Text = "InterPot を起動", AutoSize = true };
        readonly Button launchMenu = new Button { Text = "▼", Width = 22, Font = new Font("Yu Gothic UI", 7f) };
        readonly ContextMenuStrip launchItems = new ContextMenuStrip();
        readonly Control agentRow;
        readonly AgentLink agent = new AgentLink(AgentLink.DefaultPipeName());
        AgentStatus shownAgentStatus = AgentStatus.Disconnected;
        CancellationTokenSource drawing;
        bool writing; // a full-colour PUT waits for its answer
        ProgressOverlay overlay;
        UsageForm usageForm;
        Action fitLog;

        bool FullColor => Modes[inputMode.SelectedIndex].Mode == null;
        bool Busy => drawing != null || writing;

        static ToolStripButton StripButton(string text, string tip) =>
            new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text, AutoToolTip = false, ToolTipText = tip };

        public MainForm(string picture = null)
        {
            Text = AppTitle;
            using (var icon = typeof(MainForm).Assembly.GetManifestResourceStream("KanbanDrawer.app.ico")) Icon = new Icon(icon);
            Font = new Font("Yu Gothic UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(744, 660);
            MinimumSize = new Size(700, 600);
            AllowDrop = true;
            KeyPreview = true;

            host = new EditorHost(settings, this) { Log = Log };
            view = new EditorView(host);
            viewRow = Ui.Buttons(colorView, draftView, makeDraft);
            colorView.Margin = draftView.Margin = new Padding(0, 4, 8, 0); // level with the button's text
            agentRow = BuildAgentRow();
            boardRow = BuildBoardRow();
            BuildStrip();
            BuildLayout();
            foreach (var f in features)
            {
                var page = new TabPage(f.Title) { UseVisualStyleBackColor = false, BackColor = SystemColors.Window };
                var panel = f.CreatePanel(host);
                panel.Dock = DockStyle.Fill;
                page.Controls.Add(panel);
                tabs.TabPages.Add(page);
                pages[f] = page;
            }
            foreach (var f in features) f.Load(settings);
            inputMode.SelectedIndex = Math.Max(0, Math.Min(Modes.Length - 1, settings.Get("input", 0)));
            // the last tab by its name (the order may change between versions); the first tab otherwise
            string tab = settings.Get("tabName", "");
            tabs.SelectedIndex = Math.Max(0, features.FindIndex(f => f.Title == tab));
            // after every feature's Load (the quantize settings applied at start are not a change)
            files = new BoardFileCommands(this, host, settings, () => Busy);
            files.TitleChanged += () => { if (drawing == null) Text = files.Title; };
            Text = files.Title;

            host.DraftChanged += OnDraftChanged;
            host.QuantizeChanged += () => { if (draftView.Checked) host.MakeDraft(); }; // keep a shown draft current
            host.Rendered += UpdateUndo;
            host.Doc.SelectionChanged += OnSelection;
            makeDraft.Click += (s, e) => ShowDraft();
            draftView.CheckedChanged += (s, e) =>
            {
                if (draftView.Checked && (host.Draft == null || host.DraftStale)) host.MakeDraft();
                view.ShowDraft = draftView.Checked;
            };
            inputMode.SelectedIndexChanged += (s, e) => OnInputMode();
            launchGame.Click += (s, e) => LaunchGame(false);
            launchMenu.Click += (s, e) => launchItems.Show(launchMenu, new Point(0, launchMenu.Height));
            launchItems.Items.Add("ショートカットを作成", null, (s, e) => LaunchGame(true));
            agent.StatusChanged += () => { if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)OnAgentStatus); };
            detect.Click += (s, e) => Detect();
            draw.Click += (s, e) => { if (drawing != null) drawing.Cancel(); else StartDrawing(false); }; // red while drawing = 中止
            resume.Click += (s, e) => StartDrawing(true);
            DragEnter += (s, e) => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            DragDrop += (s, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] f && f.Length > 0) OpenFile(f[0]); };
            KeyDown += OnKey;
            FormClosing += (s, e) =>
            {
                if (!files.ConfirmDiscard()) { e.Cancel = true; return; }
                SaveSettings();
                OfficialText.Shutdown();
                agent.Dispose();
            };

            host.Tips.SetToolTip(makeDraft, "実際に描く 10 色の絵と本数・時間を確認できます。描画開始でも自動で作ります（古ければ作り直します）");
            host.Tips.SetToolTip(detect, "InterPot の看板編集画面を探します（描画開始でも自動で探します）");
            host.Tips.SetToolTip(resume, "中断したところから、画面の今の状態と違う画素だけ描きます");

            host.RenderNow();
            OnInputMode();
            HandleCreated += (s, e) => { agent.Start(); OnAgentStatus(); };
            // the agent jar for the shortcut / launch, rewritten only when it differs (a missing jar is reported on use)
            Task.Run(() => { try { GameLauncher.ExtractAgent(); } catch (Exception) { } });
            Log("画像やスタンプ・文字で看板を作り、InterPot で看板の編集画面を開いて「描画開始」。保存はご自身で。");
            Shown += (s, e) => EnsureLogLines();
            if (picture != null) Shown += (s, e) => OpenFile(picture);
        }

        // ---------- layout ----------

        /// <summary>The strip across the top: 新規 開く 保存▼ | 元に戻す やり直し 貼り付け | PNG に書き出す … 使い方.</summary>
        void BuildStrip()
        {
            strip.Font = Font;
            var saveAs = new ToolStripMenuItem("名前を付けて保存...") { ShortcutKeyDisplayString = "Ctrl+Shift+S" };
            saveAs.Click += (s, e) => files.SaveAs();
            saveBoard.DropDownItems.Add(saveAs);
            usage.Alignment = ToolStripItemAlignment.Right;
            strip.Items.AddRange(new ToolStripItem[]
            {
                newBoard, openBoard, saveBoard, new ToolStripSeparator(),
                undo, redo, paste, new ToolStripSeparator(),
                exportPng, usage,
            });
            newBoard.Click += (s, e) => files.New();
            openBoard.Click += (s, e) => files.Open();
            saveBoard.ButtonClick += (s, e) => files.Save();
            undo.Click += (s, e) => Undo();
            redo.Click += (s, e) => Redo();
            paste.Click += (s, e) => Paste();
            exportPng.Click += (s, e) => ExportPng();
            usage.Click += (s, e) => ShowUsage();
        }

        void BuildLayout()
        {
            var left = new StackPanel { Dock = DockStyle.Left, Width = view.Width + 16, AutoScroll = false };
            view.Margin = new Padding(0);
            left.Controls.Add(view);
            left.Controls.Add(viewRow);
            stats.Margin = new Padding(0);
            left.Controls.Add(stats);
            // 描く操作: input mode (+ 看板を検出), the full-colour state, 描画開始, the board found (+ 続きから / 手直し)
            var modeRow = new TableLayoutPanel { Height = 26, ColumnCount = 3, Margin = new Padding(0, 2, 0, 2) };
            modeRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            modeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            modeRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            modeRow.Controls.Add(new Label { Text = "入力方式", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 4, 0) }, 0, 0);
            inputMode.Dock = DockStyle.Fill;
            inputMode.Margin = new Padding(0, 1, 0, 0);
            modeRow.Controls.Add(inputMode, 1, 0);
            detect.Margin = new Padding(4, 0, 0, 0);
            modeRow.Controls.Add(detect, 2, 0);
            drawRows.Controls.Add(modeRow);
            drawRows.Controls.Add(agentRow);
            drawRows.Controls.Add(draw);
            drawRows.Controls.Add(boardRow);
            drawBox.Controls.Add(drawRows);
            left.Controls.Add(drawBox);
            left.Controls.Add(bar);
            log.Height = 60;
            left.Controls.Add(log);
            // the log takes the height that is left (it is the last row), so it is never cut off at the bottom
            void FitLog()
            {
                left.PerformLayout();
                int h = Math.Max(48, left.ClientSize.Height - left.Padding.Bottom - log.Top - log.Margin.Bottom);
                if (log.Height != h) log.Height = h;
            }
            fitLog = FitLog;
            left.Resize += (s, e) => FitLog();
            left.HandleCreated += (s, e) => BeginInvoke((Action)FitLog);
            Controls.Add(tabs);
            Controls.Add(left);
            Controls.Add(strip); // docked first: across the whole width, above the left panel and the tabs
        }

        /// <summary>
        /// The 描く操作 box as tall as its rows: the full-colour state row replaces the board row. (Counted from the mode, not
        /// from <see cref="Control.Visible"/>, which reads false for every control until the form is shown.)
        /// </summary>
        void FitDrawBox(bool full)
        {
            int rows = new[] { drawRows.Controls[0], draw, full ? agentRow : boardRow }.Sum(c => c.Height + c.Margin.Vertical);
            drawBox.Height = drawBox.Height - drawBox.DisplayRectangle.Height + rows + 3;
            drawRows.PerformLayout();
        }

        /// <summary>
        /// Once at start: the log shows at least 6 lines (5 in full colour, whose state row takes height). 744x660 gives that
        /// with Yu Gothic UI 9pt; a taller font grows the window by what is missing (docs/design/ui.md「ログ欄の高さ」).
        /// </summary>
        void EnsureLogLines()
        {
            if (WindowState != FormWindowState.Normal) return;
            fitLog?.Invoke();
            int need = (FullColor ? 5 : 6) * log.Font.Height + 4;
            if (log.Height < need) Height += need - log.Height;
        }

        /// <summary>Full colour only: a coloured dot and the link's state on the left, starting the game (▼ = its shortcut) on the right.</summary>
        Control BuildAgentRow()
        {
            var r = new TableLayoutPanel { Height = 26, ColumnCount = 4, Margin = new Padding(0, 2, 0, 2), Visible = false };
            r.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            r.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            r.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            r.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            r.Controls.Add(agentDot, 0, 0);
            r.Controls.Add(agentState, 1, 0);
            launchGame.Margin = new Padding(4, 0, 0, 0);
            launchMenu.Margin = new Padding(0);
            launchMenu.Height = launchGame.PreferredSize.Height;
            r.Controls.Add(launchGame, 2, 0);
            r.Controls.Add(launchMenu, 3, 0);
            return r;
        }

        /// <summary>The board found by 看板を検出 on the left, 続きから / 手直し on the right.</summary>
        Control BuildBoardRow()
        {
            var r = new TableLayoutPanel { Height = 26, ColumnCount = 2, Margin = new Padding(0, 2, 0, 2) };
            r.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            r.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            boardState.Margin = new Padding(0);
            resume.Margin = new Padding(4, 0, 0, 0);
            resume.MinimumSize = new Size(0, 24);
            resume.MaximumSize = new Size(0, 24);
            r.Controls.Add(boardState, 0, 0);
            r.Controls.Add(resume, 1, 0);
            return r;
        }

        /// <summary>描画開始 (green), 中止 (Esc) (red) while drawing, the system's grey while it cannot be pressed.</summary>
        void StyleDraw()
        {
            bool stop = drawing != null;
            draw.Text = stop ? "中止 (Esc)" : "描画開始";
            draw.FlatAppearance.BorderSize = 0;
            if (!draw.Enabled)
            {
                draw.BackColor = SystemColors.Control;
                draw.ForeColor = SystemColors.GrayText;
                return;
            }
            draw.ForeColor = Color.White;
            draw.BackColor = stop ? Red : Green;
            draw.FlatAppearance.MouseOverBackColor = stop ? RedOver : GreenOver;
            draw.FlatAppearance.MouseDownBackColor = stop ? RedDown : GreenDown;
        }

        void ShowUsage()
        {
            if (usageForm == null || usageForm.IsDisposed)
            {
                usageForm = new UsageForm(this);
                usageForm.Show(this);
            }
            else
            {
                if (usageForm.WindowState == FormWindowState.Minimized) usageForm.WindowState = FormWindowState.Normal;
                usageForm.Activate();
            }
        }

        // ---------- editing ----------

        void OnSelection()
        {
            var o = host.Doc.Selected;
            foreach (var f in features) f.OnSelection(o);
            if (o == null) return;
            var owner = features.FirstOrDefault(f => f.Owns(o));
            if (owner != null) tabs.SelectedTab = pages[owner];
        }

        void OpenFile(string path)
        {
            if (string.Equals(Path.GetExtension(path), ".kanban", StringComparison.OrdinalIgnoreCase)) { files.OpenPath(path); return; }
            var active = features.FirstOrDefault(f => pages[f] == tabs.SelectedTab) as IAcceptsFiles;
            var target = active ?? features.OfType<IAcceptsFiles>().FirstOrDefault();
            if (target == null) return;
            if (target != active) tabs.SelectedTab = pages[(IFeature)target];
            target.OpenFile(path);
        }

        /// <summary>
        /// 貼り付け (Ctrl+V / the strip): the clipboard is read now. Picture files open like a drop (one undo each);
        /// picture data / text go to the first feature that takes them, the active tab first.
        /// </summary>
        void Paste()
        {
            if (Busy) return;
            ClipboardContent c;
            try { c = ClipboardContent.Read(Clipboard.GetDataObject()); }
            catch (Exception) { c = ClipboardContent.None; } // e.g. held by another application
            switch (c.Kind)
            {
                case ClipboardContent.ContentKind.Files:
                    foreach (var f in c.Files) OpenFile(f);
                    break;
                case ClipboardContent.ContentKind.Image:
                case ClipboardContent.ContentKind.Text:
                    var active = features.FirstOrDefault(f => pages[f] == tabs.SelectedTab);
                    var order = new[] { active }.Concat(features).Where(f => f != null).Distinct().OfType<IAcceptsPaste>();
                    foreach (var p in order)
                        if (c.Kind == ClipboardContent.ContentKind.Image ? p.PasteImage(c.Image, "クリップボード") : p.PasteText(c.Text)) break;
                    break;
                default:
                    Log("貼り付けられるものがありません");
                    break;
            }
        }

        void Undo()
        {
            if (Busy) return;
            host.EndCheckpointBurst();
            if (!host.Doc.Undo()) return;
            UpdateUndo();
        }

        void Redo()
        {
            if (Busy) return;
            host.EndCheckpointBurst();
            if (!host.Doc.Redo()) return;
            UpdateUndo();
        }

        /// <summary>Makes the draft (10 colours + strokes) and shows it.</summary>
        void ShowDraft()
        {
            host.MakeDraft();
            draftView.Checked = true;
            view.ShowDraft = true;
        }

        void OnDraftChanged()
        {
            // an edit made the shown draft stale: back to the colour view (the draft would no longer match)
            if (host.DraftStale && draftView.Checked) colorView.Checked = true;
            UpdateStats();
        }

        /// <summary>
        /// PNG に書き出す: the board as it will be drawn (10 colours, 320x240), the draft made first if needed. The pen modes
        /// show it; full colour hides the view switch, so the draft is only made there.
        /// </summary>
        void ExportPng()
        {
            if (Busy) return;
            if (host.Draft == null || host.DraftStale)
            {
                if (FullColor) host.MakeDraft();
                else ShowDraft();
            }
            if (host.Draft == null) return;
            using (var d = new SaveFileDialog { Filter = "PNG|*.png", FileName = "kanban_320x240.png" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                using (var b = ImageLoader.ToBitmap(host.Draft.ToRaster())) b.Save(d.FileName, System.Drawing.Imaging.ImageFormat.Png);
                Log("保存しました: " + d.FileName);
            }
        }

        void UpdateUndo()
        {
            undo.Enabled = host.Doc.CanUndo && !Busy;
            redo.Enabled = host.Doc.CanRedo && !Busy;
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (!e.Control || e.Alt) return;
            // the file keys work while typing too (a text box has no use for them)
            if (e.KeyCode == Keys.N && !e.Shift) { if (!Busy) files.New(); e.Handled = e.SuppressKeyPress = true; return; }
            if (e.KeyCode == Keys.O && !e.Shift) { if (!Busy) files.Open(); e.Handled = e.SuppressKeyPress = true; return; }
            if (e.KeyCode == Keys.S)
            {
                if (!Busy) { if (e.Shift) files.SaveAs(); else files.Save(); }
                e.Handled = e.SuppressKeyPress = true;
                return;
            }
            bool typing = ActiveControl is TextBoxBase || ActiveControl is ComboBox || ActiveControl is NumericUpDown
                          || (ActiveControl is ContainerControl cc && (cc.ActiveControl is TextBoxBase));
            if (typing) return;
            if (e.KeyCode == Keys.Z && !e.Shift) { Undo(); e.Handled = true; }
            else if (e.KeyCode == Keys.Y || (e.KeyCode == Keys.Z && e.Shift)) { Redo(); e.Handled = true; }
            else if (e.KeyCode == Keys.V && !e.Shift) { Paste(); e.Handled = true; }
        }

        // ---------- stats / settings ----------

        void UpdateStats()
        {
            string tip = "";
            if (FullColor) stats.Text = ""; // hidden
            else if (host.Draft == null) stats.Text = "下書きを作ると、本数と目安時間が出ます";
            else if (host.DraftStale) stats.Text = "下書きのあとに変更があります";
            else if (host.Plan == null) stats.Text = "計算中...";
            else
            {
                var plan = host.Plan;
                var m = Modes[inputMode.SelectedIndex];
                double sec = plan.StrokeCount * m.StrokeMs / 1000.0 + plan.ColorClicks * 0.15 + 2;
                var used = plan.Passes.Select(p => Palette.All[p.Color].Name);
                string clr = plan.ClearColor >= 0 ? Palette.All[plan.ClearColor].Name : "-";
                stats.Text = $"ストローク {plan.StrokeCount:N0} 本 / 色の切替 {plan.ColorClicks} 回 / 目安 {Format(sec)}";
                tip = $"塗りつぶし: {clr} → {string.Join(" ", used)}";
            }
            host.Tips.SetToolTip(stats, tip);
        }

        static string Format(double sec) => sec < 60 ? $"{sec:F0} 秒" : $"{(int)(sec / 60)} 分 {(int)(sec % 60)} 秒";

        void SaveSettings()
        {
            foreach (var f in features) f.Save(settings);
            settings.Set("input", inputMode.SelectedIndex);
            settings.Set("tabName", features[tabs.SelectedIndex].Title);
            settings.Save();
        }

        // ---------- board ----------

        void Detect()
        {
            var b = BoardFinder.Find(out string err);
            if (b == null)
            {
                boardState.Text = "看板: 見つかりません";
                host.Tips.SetToolTip(boardState, "");
                Log(err);
                return;
            }
            ShowBoard(b);
            Log($"看板を検出しました: キャンバス左上 ({b.Origin.X}, {b.Origin.Y}) / ウィンドウ「{b.WindowTitle}」");
        }

        void ShowBoard(BoardTarget b)
        {
            boardState.Text = $"看板: ({b.Origin.X}, {b.Origin.Y})";
            host.Tips.SetToolTip(boardState, $"ウィンドウ「{b.WindowTitle}」");
        }

        async void StartDrawing(bool resumeFromBoard)
        {
            if (Busy) return;
            if (FullColor)
            {
                if (!resumeFromBoard) StartFullColor();
                return;
            }
            var mode = Modes[inputMode.SelectedIndex].Mode.Value;
            drawing = new CancellationTokenSource();
            var token = drawing.Token;
            UpdateButtons();
            bar.Value = 0;
            Log(resumeFromBoard ? "続きから描きます（今の看板と違う画素だけ）" : mode == InputMode.Send
                ? "描画を始めます。マウスに触らないでください。Esc で中止できます。"
                : "描画を始めます。ゲームの看板を隠したり、ゲームをクリックしたりしないでください。Esc で中止できます。");
            var progress = new Progress<DrawProgress>(OnProgress);
            void LogAsync(string s) => BeginInvoke((Action)(() => { Log(s); overlay?.SetText(s); }));
            try
            {
                // what is drawn is always the draft: make it now when missing or stale, and show it
                if (host.Draft == null || host.DraftStale) ShowDraft();
                else { draftView.Checked = true; view.ShowDraft = true; }
                var (t, fullPlan) = await host.EnsureDraftAsync();
                var plan = resumeFromBoard ? null : fullPlan;
                ShowOverlay();
                var result = await Task.Run(() => DrawRunner.Run(t, plan, new DrawSettings(), mode, CountdownSeconds, LogAsync, progress, token,
                    b => BeginInvoke((Action)(() => ShowBoard(b)))));
                if (result.Completed) bar.Value = bar.Maximum;
                if (result.InterruptReason == null)
                    Log(result.Completed ? "看板の「保存」はご自身で押してください。" : "一致しない画素が残っています。「続きから / 手直し」で再度描けます。");
                else if (result.StrokesDrawn > 0)
                    Log("「続きから / 手直し」で中断したところから再開できます。");
            }
            catch (Exception e)
            {
                Log("エラー: " + e.Message);
            }
            finally
            {
                overlay?.Close();
                overlay = null;
                drawing.Dispose();
                drawing = null;
                UpdateButtons();
                Text = files.Title;
                Activate();
            }
        }

        void OnProgress(DrawProgress p)
        {
            if (p.Total > 0)
            {
                bar.Maximum = Math.Max(1, p.Total);
                bar.Value = Math.Min(bar.Maximum, p.Done);
            }
            string text = $"{PhaseName(p.Phase)} {p.Message}" + (p.Total > 0 ? $"  {p.Done * 100 / Math.Max(1, p.Total)}%" : "");
            overlay?.SetText(text + "\nEsc で中止");
            Text = p.Phase == DrawPhase.Done ? files.Title : $"{AppTitle} - {text}";
        }

        static string PhaseName(DrawPhase p)
        {
            switch (p)
            {
                case DrawPhase.Prepare: return "準備";
                case DrawPhase.Clear: return "塗りつぶし";
                case DrawPhase.Strokes: return "描画";
                case DrawPhase.Verify: return "確認";
                case DrawPhase.Retouch: return "手直し";
                default: return "";
            }
        }

        void ShowOverlay()
        {
            var b = BoardFinder.Find(out _);
            if (b == null) return;
            Native.GetWindowRect(b.Window, out var wr);
            var game = Rectangle.FromLTRB(wr.Left, wr.Top, wr.Right, wr.Bottom);
            overlay = new ProgressOverlay();
            var area = Screen.FromRectangle(game).WorkingArea;
            var size = overlay.Size;
            var corners = new[]
            {
                new Point(area.Right - size.Width - 12, area.Bottom - size.Height - 12),
                new Point(area.Left + 12, area.Bottom - size.Height - 12),
                new Point(area.Right - size.Width - 12, area.Top + 12),
                new Point(area.Left + 12, area.Top + 12),
            };
            foreach (var c in corners)
            {
                if (new Rectangle(c, size).IntersectsWith(game)) continue;
                overlay.Location = c;
                overlay.Show();
                return;
            }
            overlay.Dispose();
            overlay = null; // no free corner: the game window would be covered
        }

        /// <summary>While drawing / writing only 描画開始 (the red 中止 while drawing) and 使い方 stay usable.</summary>
        void UpdateButtons()
        {
            bool busy = Busy;
            draw.Enabled = drawing != null || !busy;
            makeDraft.Enabled = !busy;
            resume.Enabled = detect.Enabled = !busy && !FullColor; // full colour neither detects nor resumes
            inputMode.Enabled = !busy;
            newBoard.Enabled = openBoard.Enabled = saveBoard.Enabled = !busy;
            paste.Enabled = exportPng.Enabled = !busy;
            tabs.Enabled = !busy;
            view.Enabled = !busy;
            UpdateUndo();
            StyleDraw();
        }

        // ---------- full colour (docs/design/fullcolor.md) ----------

        const string NotConnected = "ゲームとつながっていません。「InterPot を起動」でフルカラー用に起動してください（ゲームを先に起動済みなら、一度閉じて起動し直し）";

        /// <summary>
        /// Full colour shows the state row and hides what it does not use (看板を検出, the board row, the view switch and
        /// 下書きを作る, the stats): hidden rows take no height, so the log gets it. The view goes back to colour.
        /// </summary>
        void OnInputMode()
        {
            bool full = FullColor;
            host.FullColor = full; // smooth text while writing in full colour
            if (full && draftView.Checked) colorView.Checked = true; // a hidden draft view could not be switched back
            agentRow.Visible = full;
            detect.Visible = boardRow.Visible = viewRow.Visible = stats.Visible = !full;
            host.Tips.SetToolTip(draw, full ? FullColorDrawTip : DrawTip);
            FitDrawBox(full);
            drawBox.Parent?.PerformLayout();
            fitLog?.Invoke();
            UpdateButtons();
            UpdateStats();
        }

        static string StatusText(AgentStatus s)
        {
            switch (s)
            {
                case AgentStatus.Busy: return "未接続（別の看板屋が使用中）";
                case AgentStatus.NoEditor: return "接続中: エディタなし";
                case AgentStatus.Editor: return "接続中: エディタあり";
                case AgentStatus.Mismatch: return "版が合いません";
                default: return "未接続";
            }
        }

        /// <summary>The dot beside the state (the text says it too: colour is not the only sign).</summary>
        static Color StatusColor(AgentStatus s)
        {
            switch (s)
            {
                case AgentStatus.Editor: return ColorTranslator.FromHtml("#2E9E3E");
                case AgentStatus.NoEditor: return ColorTranslator.FromHtml("#E0A000");
                case AgentStatus.Busy:
                case AgentStatus.Mismatch: return ColorTranslator.FromHtml("#D03030");
                default: return SystemColors.GrayText;
            }
        }

        static bool Connected(AgentStatus s) => s == AgentStatus.NoEditor || s == AgentStatus.Editor || s == AgentStatus.Mismatch;

        /// <summary>On the UI thread: shows the link's state and logs connecting, disconnecting and a version mismatch (once).</summary>
        void OnAgentStatus()
        {
            var now = agent.Status;
            var was = shownAgentStatus;
            shownAgentStatus = now;
            agentState.Text = StatusText(now);
            agentDot.ForeColor = StatusColor(now);
            if (now == was) return;
            if (now == AgentStatus.Mismatch)
                Log($"ゲーム側の連携プログラムの版（{agent.AgentVersion}）が看板屋と合いません。「InterPot を起動」で起動し直してください");
            else if (Connected(now) && !Connected(was))
                Log("ゲームの連携プログラムとつながりました");
            else if (!Connected(now) && Connected(was))
                Log("ゲームとの接続が切れました");
        }

        /// <summary>「InterPot を起動」/ ▼「ショートカットを作成」, InterPot3.jar from the setting, the auto-detected install or a dialog (then saved).</summary>
        void LaunchGame(bool shortcut)
        {
            string configured = settings.Get("fullcolor.jar", "");
            string jar = GameLauncher.LocateGame(configured, this);
            if (jar == null) { Log("InterPot3.jar が見つかりません"); return; }
            // only a path picked in the dialog is saved; an auto-detected one is looked up again each time
            if (jar != configured && jar != GameLauncher.FindInstalled()) settings.Set("fullcolor.jar", jar);
            if (shortcut) GameLauncher.CreateShortcut(jar, Log);
            else GameLauncher.Launch(jar, Log);
        }

        /// <summary>
        /// 描画開始 in full colour: the composite's RGB as it is (no draft, no quantization) to the agent. No countdown,
        /// detection or Esc watch; drawing and editing are off while the answer is awaited (at most 10 s).
        /// </summary>
        async void StartFullColor()
        {
            switch (agent.Status)
            {
                case AgentStatus.Editor: break;
                case AgentStatus.NoEditor: Log("InterPot で看板の編集画面を開いてください"); return;
                case AgentStatus.Busy: Log("別の看板屋が使用中です"); return;
                case AgentStatus.Mismatch: Log("連携プログラムの版が合いません"); return;
                default: Log(NotConnected); return;
            }
            writing = true;
            UpdateButtons();
            try
            {
                host.RenderNow(); // the composite as it is now
                var rgb = AgentLink.ToRgb(host.Composite);
                switch (await Task.Run(() => agent.Put(rgb)))
                {
                    case PutResult.Ok: Log("フルカラーで書き込みました。看板を確認して、「保存」はご自身で押してください。"); break;
                    case PutResult.NoEditor: Log("エディタが閉じられたため書き込めませんでした"); break;
                    case PutResult.Error: Log("ゲームがエラーを返しました: " + agent.LastError); break;
                    case PutResult.Timeout: Log("ゲームが応答しません（接続を切りました）"); break;
                    case PutResult.NotReady: Log(NotConnected); break;
                    // Lost: the status change logs 「ゲームとの接続が切れました」
                }
            }
            catch (Exception e)
            {
                Log("エラー: " + e.Message);
            }
            finally
            {
                writing = false;
                UpdateButtons();
            }
        }

        void Log(string s) => log.AppendText(DateTime.Now.ToString("HH:mm:ss ") + s + Environment.NewLine);
    }
}
