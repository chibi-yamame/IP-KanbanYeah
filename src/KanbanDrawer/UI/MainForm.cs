using System;
using System.Collections.Generic;
using System.Drawing;
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
    /// Left: the board (1:1 editor) and drawing controls. Right: one tab per feature (FeatureRegistry).
    /// The drawing part is not a feature: it always works on whatever the features produced.
    /// </summary>
    internal sealed class MainForm : Form
    {
        // input modes and the cost per stroke measured on the mock board (docs/design/drawing.md)
        static readonly (string Name, InputMode Mode, double StrokeMs)[] Modes =
        {
            ("バックグラウンド（推奨）", InputMode.Post, 2.3),
            ("マウス操作（互換・遅い）", InputMode.Send, 18),
        };

        const string AppTitle = "看板屋";
        const int CountdownSeconds = 2;
        readonly Settings settings = Settings.Load();
        readonly EditorHost host;
        readonly List<IFeature> features = FeatureRegistry.Create();
        readonly Dictionary<IFeature, TabPage> pages = new Dictionary<IFeature, TabPage>();
        readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill };
        readonly EditorView view;
        readonly Button makeDraft = new Button { Text = "下書き", AutoSize = true, Font = new Font("Yu Gothic UI", 9f, FontStyle.Bold) };
        readonly RadioButton colorView = new RadioButton { Text = "カラー", AutoSize = true, Checked = true };
        readonly RadioButton draftView = new RadioButton { Text = "下書き（10 色）", AutoSize = true };
        readonly Button undo = new Button { Text = "元に戻す", AutoSize = true };
        readonly Button redo = new Button { Text = "やり直し", AutoSize = true };
        readonly Button savePng = new Button { Text = "PNG 保存", AutoSize = true };
        readonly Label stats = new Label { Height = 50, AutoEllipsis = true };
        readonly ComboBox inputMode = Ui.DropDown(Modes.Select(m => m.Name).ToArray());
        readonly Button detect = new Button { Text = "看板を検出", AutoSize = true };
        readonly Button draw = new Button { Text = "描画開始", Height = 34, Font = new Font("Yu Gothic UI", 10.5f, FontStyle.Bold) };
        readonly Button resume = new Button { Text = "続きから / 手直し", Height = 28 };
        readonly Button stop = new Button { Text = "中止 (Esc)", Height = 28, Enabled = false };
        readonly ProgressBar bar = new ProgressBar { Height = 14 };
        readonly TextBox log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        CancellationTokenSource drawing;
        ProgressOverlay overlay;

        public MainForm(string picture = null)
        {
            Text = AppTitle;
            using (var icon = typeof(MainForm).Assembly.GetManifestResourceStream("KanbanDrawer.app.ico")) Icon = new Icon(icon);
            Font = new Font("Yu Gothic UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(744, 600);
            MinimumSize = new Size(700, 540);
            AllowDrop = true;
            KeyPreview = true;

            host = new EditorHost(settings, this) { Log = Log };
            view = new EditorView(host);
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
            tabs.SelectedIndex = Math.Max(0, Math.Min(tabs.TabCount - 1, settings.Get("tab", 0)));

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
            undo.Click += (s, e) => Undo();
            redo.Click += (s, e) => Redo();
            savePng.Click += (s, e) => SavePng();
            inputMode.SelectedIndexChanged += (s, e) => UpdateStats();
            detect.Click += (s, e) => Detect();
            draw.Click += (s, e) => StartDrawing(false);
            resume.Click += (s, e) => StartDrawing(true);
            stop.Click += (s, e) => drawing?.Cancel();
            DragEnter += (s, e) => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            DragDrop += (s, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] f && f.Length > 0) OpenFile(f[0]); };
            KeyDown += OnKey;
            FormClosing += (s, e) => { SaveSettings(); OfficialText.Shutdown(); };

            host.RenderNow();
            UpdateButtons();
            UpdateStats();
            Log("画像やスタンプ・文字で看板を作り、InterPot で看板の編集画面を開いて「描画開始」。保存はご自身で。");
            if (picture != null) Shown += (s, e) => OpenFile(picture);
        }

        // ---------- layout ----------

        void BuildLayout()
        {
            var left = new StackPanel { Dock = DockStyle.Left, Width = view.Width + 16, AutoScroll = false };
            view.Margin = new Padding(0);
            left.Controls.Add(view);
            left.Controls.Add(Ui.Buttons(makeDraft, colorView, draftView));
            left.Controls.Add(Ui.Buttons(undo, redo, savePng));
            left.Controls.Add(stats);
            // input mode and 看板を検出 share a row
            var modeRow = new TableLayoutPanel { Height = 28, ColumnCount = 3, Margin = new Padding(0, 2, 0, 2) };
            modeRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            modeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            modeRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            modeRow.Controls.Add(new Label { Text = "入力方式", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 4, 0) }, 0, 0);
            inputMode.Dock = DockStyle.Fill;
            modeRow.Controls.Add(inputMode, 1, 0);
            detect.Margin = new Padding(4, 0, 0, 0);
            modeRow.Controls.Add(detect, 2, 0);
            left.Controls.Add(modeRow);
            left.Controls.Add(draw);
            var row = new TableLayoutPanel { Height = 30, ColumnCount = 2, Margin = new Padding(0, 2, 0, 2) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            resume.Dock = stop.Dock = DockStyle.Fill;
            row.Controls.Add(resume, 0, 0);
            row.Controls.Add(stop, 1, 0);
            left.Controls.Add(row);
            left.Controls.Add(bar);
            log.Height = 60;
            left.Controls.Add(log);
            // the log takes the height that is left (it is the last row), so it is never cut off at the bottom
            void FitLog()
            {
                int h = Math.Max(48, left.ClientSize.Height - left.Padding.Bottom - log.Top - log.Margin.Bottom);
                if (log.Height != h) log.Height = h;
            }
            left.Resize += (s, e) => FitLog();
            left.HandleCreated += (s, e) => BeginInvoke((Action)FitLog);
            Controls.Add(tabs);
            Controls.Add(left);
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
            var active = features.FirstOrDefault(f => pages[f] == tabs.SelectedTab) as IAcceptsFiles;
            var target = active ?? features.OfType<IAcceptsFiles>().FirstOrDefault();
            if (target == null) return;
            if (target != active) tabs.SelectedTab = pages[(IFeature)target];
            target.OpenFile(path);
        }

        void Undo()
        {
            host.EndCheckpointBurst();
            if (!host.Doc.Undo()) return;
            UpdateUndo();
        }

        void Redo()
        {
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

        /// <summary>The board as it will be drawn (10 colours, 320x240): the draft, made first if needed.</summary>
        void SavePng()
        {
            if (host.Draft == null || host.DraftStale) ShowDraft();
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
            undo.Enabled = host.Doc.CanUndo && drawing == null;
            redo.Enabled = host.Doc.CanRedo && drawing == null;
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            bool typing = ActiveControl is TextBoxBase || ActiveControl is ComboBox || ActiveControl is NumericUpDown
                          || (ActiveControl is ContainerControl cc && (cc.ActiveControl is TextBoxBase));
            if (typing || !e.Control) return;
            if (e.KeyCode == Keys.Z && !e.Shift) { Undo(); e.Handled = true; }
            else if (e.KeyCode == Keys.Y || (e.KeyCode == Keys.Z && e.Shift)) { Redo(); e.Handled = true; }
        }

        // ---------- stats / settings ----------

        void UpdateStats()
        {
            if (host.Draft == null) { stats.Text = "「下書き」で、実際に描く 10 色の絵と本数・時間を確認できます\n（描画開始でも自動で作ります）"; return; }
            if (host.DraftStale) { stats.Text = "下書きのあとに変更があります。「下書き」で作り直してください\n（描画開始でも自動で作り直します）"; return; }
            var plan = host.Plan;
            if (plan == null) { stats.Text = "計算中..."; return; }
            var m = Modes[inputMode.SelectedIndex];
            double sec = plan.StrokeCount * m.StrokeMs / 1000.0 + plan.ColorClicks * 0.15 + 2;
            var used = plan.Passes.Select(p => Palette.All[p.Color].Name);
            string clr = plan.ClearColor >= 0 ? Palette.All[plan.ClearColor].Name : "-";
            stats.Text = $"ストローク {plan.StrokeCount:N0} 本 / 色の切替 {plan.ColorClicks} 回 / 目安 {Format(sec)}\n" +
                         $"塗りつぶし: {clr} → {string.Join(" ", used)}";
        }

        static string Format(double sec) => sec < 60 ? $"{sec:F0} 秒" : $"{(int)(sec / 60)} 分 {(int)(sec % 60)} 秒";

        void SaveSettings()
        {
            foreach (var f in features) f.Save(settings);
            settings.Set("input", inputMode.SelectedIndex);
            settings.Set("tab", tabs.SelectedIndex);
            settings.Save();
        }

        // ---------- board ----------

        void Detect()
        {
            var b = BoardFinder.Find(out string err);
            Log(b == null ? err : $"看板を検出しました: キャンバス左上 ({b.Origin.X}, {b.Origin.Y}) / ウィンドウ「{b.WindowTitle}」");
        }

        async void StartDrawing(bool resumeFromBoard)
        {
            if (drawing != null) return;
            var mode = Modes[inputMode.SelectedIndex].Mode;
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
                var result = await Task.Run(() => DrawRunner.Run(t, plan, new DrawSettings(), mode, CountdownSeconds, LogAsync, progress, token));
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
                Text = AppTitle;
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
            Text = p.Phase == DrawPhase.Done ? AppTitle : $"{AppTitle} - {text}";
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

        void UpdateButtons()
        {
            bool busy = drawing != null;
            draw.Enabled = resume.Enabled = makeDraft.Enabled = !busy;
            stop.Enabled = busy;
            detect.Enabled = !busy;
            tabs.Enabled = !busy;
            view.Enabled = !busy;
            UpdateUndo();
        }

        void Log(string s) => log.AppendText(DateTime.Now.ToString("HH:mm:ss ") + s + Environment.NewLine);
    }
}
