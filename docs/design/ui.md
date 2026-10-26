# 画面・CLI

## 画面（`UI/MainForm`、ウィンドウ名「看板屋」、クライアント 744x600）
- 左（常設）: `EditorView`（看板 1 倍 + 余白）、**下書き** ボタン + 表示切替（カラー / 下書き（10 色））、元に戻す / やり直し /
  PNG 保存 / **貼り付け**（同じ行。クリップボードの画像・テキストを看板に置く。描画中は無効。押したときにクリップボードを読む
  ので、中身に応じた有効・無効の切り替えはしない）、統計（下書きのストローク数・色切替・目安・塗りつぶし色 → 色順。下書きがない / 古いときは案内文）、入力方式 +
  看板を検出（1 行）、描画開始、続きから / 手直し、中止、進捗バー、ログ（残りの高さいっぱい。左パネルの Resize で合わせる）。
- 描画開始から実際の描画までのカウントダウンは 2 秒（`MainForm.CountdownSeconds`、CLI `--countdown` の既定も 2）。
- 編集はフルカラー表示、「下書き」で 10 色の描く絵を作って表示（[features.md](features.md)「入口・流れ」）。描画開始・PNG 保存は
  下書きが古ければ作り直してから使う。
- 右: Feature のタブ（`FeatureRegistry`。下地 → 画像 → スタンプ → 文字 → 減色、起動時は下地）。各タブの中身は [features.md](features.md)。
- オブジェクトを選ぶと、それを扱う Feature のタブに切り替わる（`IFeature.Owns`）。
- キー: Ctrl+Z / Ctrl+Y（Ctrl+Shift+Z）= 元に戻す / やり直し（文字入力中を除く）。Ctrl+V = 貼り付け（同じ規則で、TextBox / ComboBox / NumericUpDown にフォーカスがあるときはその欄の
  普通の貼り付けのまま。描画中は何もしない）。看板上の操作は `EditorView`。
- 貼り付け（Ctrl+V / ボタン、`MainForm.Paste`）: `Clipboard.GetDataObject()` を `ClipboardContent.Read` で読み、画像ファイル →
  画像データ（PNG のアルファは保つ）→ テキストの順で最初に見つかったものを、画像は「画像」タブ・テキストは「文字」タブの Feature
  （`IAcceptsPaste`）に渡す。何もなければログに「貼り付けられるものがありません」。詳細は [features.md](features.md)「クリップボードから貼り付け」。
- 画像ファイルのドロップ: 表示中のタブが `IAcceptsFiles` ならそこへ、違えば「画像」タブへ。exe への引数 1 個も同じ。
- 描画中はタブ・看板・undo を無効化。ゲームと重ならない画面の隅に `ProgressOverlay`（最前面・非アクティブ化・クリック透過）。
- 目安時間 = ストローク数 × 方式別の実測値（Post 2.3ms、Send 18ms）+ 色切替 × 0.15 秒 + 2 秒。

## 設定（`UI/Settings`）
`AppPaths.DataDir\settings.txt`（key=value）。全体: `input`, `tabName`（前回のタブをタブ名で。順番が変わってもずれない。
旧版の `tab`〔番号〕は読まない）。Feature ごと: `picture.fit`, `folder`（画像）、`dither`, `strength`, `colors`（減色）、
`frame.*`, `stamp.*`, `text.*`。壊れていても既定値で起動する。

## CLI（`Program.Cli`）
```
KanbanYeah.exe 画像                 … 画面で開く
KanbanYeah.exe --draw 画像 [--fit contain|cover|stretch] [--dither none|fs|atkinson|bayer] [--strength 0-100]
                 [--bg RRGGBB] [--input post|send] [--delay ms] [--click-delay ms] [--fence n] [--retouch n]
                 [--countdown s] [--save-quantized out.png] [--log file] [--debug-shot out.png]
KanbanYeah.exe --quantize 画像 --save-quantized out.png [オプション]
KanbanYeah.exe --detect
KanbanYeah.exe --compare a.png b.png
KanbanYeah.exe --register-stamp 画像 [--remove-bg]          … 320x240 を超えると終了コード 1
KanbanYeah.exe [--fit ...] --register-frame 画像 [--remove-bg]   … 配置して保存するだけ（減色オプションは効かない）
KanbanYeah.exe --dotfont-dump dir 10-22 chars.txt                         … 公式文字の調査用（GDI の字形を書き出す）
```
終了コード: 0 = 完成（`--compare` は一致）、1 = 一致しない画素が残った（不一致）、2 = 中断、3 = エラー / 見つからない。
親コンソールがあれば出力（UTF-8）、`--log` でファイルにも。CLI の `--draw` は画像 1 枚だけ（スタンプ・文字は画面から）。

## 環境変数
| 変数 | 意味 |
|---|---|
| `KANBANDRAWER_DATA` | 設定・ライブラリの置き場所（既定 `%APPDATA%\KanbanDrawer`）。テストはここを差し替えてユーザーのライブラリを汚さない |
| `KANBANDRAWER_TRACE` | 描画のトレース（パス・フェンスの待ち時間/タイムアウト・ボタン確認・読み戻し）を追記するファイル |

## 未テスト
- 高 DPI でのレイアウト（`AutoScaleMode.Dpi`）。
