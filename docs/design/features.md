# 編集機能（Feature 構成・合成・減色）

## 目的
看板の絵を「下地 + オブジェクト（画像・スタンプ・文字）」の重ね合わせで作り、10 色に変換する。各機能はモジュール（Feature）として
独立させ、追加・削除が「登録 1 行 + ファイルの追加・削除」で済むようにする。

## 層の重ね順（下から）
| 層 | 中身 | 色の扱い |
|---|---|---|
| 下地 | 背景色（パレット色）+ 登録フレーム（320x240、フルカラー ARGB、透明あり）。フレームは背景色の上に `Blend`（透明の画素は背景色、半透明は混ざる） | 背景色 = **固定**（パレット番号のまま）、フレーム = 自由 |
| オブジェクト | 画像・スタンプ・文字を z 順に（後ろほど前面）。選択・移動・回転・拡縮。新しい画像は先頭（最背面）に入る | 画像・スタンプ = 自由、文字 = **固定**（入力方式がフルカラーのときの「フォントを選ぶ」の文字だけ**自由**〔下記〕） |

- 「固定」画素は減色しない。ディザの誤差を受け取らず、渡しもしない → 背景色と文字がにじまない。
- 自由な画素（画像・スタンプ、半透明の縁を含む）だけを明るさ等で調整して減色・ディザする。半透明の縁は下の色と混ぜてから減色。
- 画像は独立した層ではなくオブジェクト。余白・透明部分は下にあるもの（他のオブジェクト、なければ下地）が見える。
- 文字はアンチエイリアスなしで描く（1 画素単位でパレット色、縁取りもパレット色）。**例外**: 入力方式が「フルカラー（直接書き込み）」のあいだ（`EditorHost.FullColor`）は、
  「フォントを選ぶ」の文字と縁取りをアンチエイリアスで描き、**自由な画素**として合成する（下の「フルカラーモードの文字」）。「公式と同じ（ドット文字）」は
  ゲームの字形なので常に 2 値・固定。色は今まで通りパレットの 10 色（任意の色は選べない）。

## 入口・流れ（編集 = フルカラー、下書き = 10 色）
- **編集中**: `Doc.Changed` → `EditorHost.Invalidate()`。再描画はメッセージループ 1 周に 1 回だけ（`BeginInvoke` でまとめる）。
  `CompositeCache.Build`（下地の層をキャッシュし、その複製にオブジェクトだけ描く）→ `Rendered` → `EditorView` が
  フルカラーで表示。減色も本数計算もしない（1 フレーム数 ms）。
- **下書き**（左の「下書きを作る」ボタン / 「下書き（10 色）」表示 / 描画開始 / 上の帯の「PNG に書き出す」）: `MakeDraft()` = `Quantizer.Quantize(composite)` →
  `Draft`、250ms 後にバックグラウンドで `StrokePlanner.PlanFull` → `Plan`（統計表示）。`DraftChanged` で通知。
- 下書きの後に編集すると `DraftStale`（表示はカラーに戻り、統計は「作り直してください」）。描画開始は必ず最新の下書きを描く
  （古ければその場で作る）。下書き表示中に「減色」タブを変えると、その場で下書きを作り直す（`SetQuantize` → `QuantizeChanged`）。
- 経緯: 初版は変更のたびに合成 + 減色を同期実行（約 32ms/回、マウス移動ごと）していて、ドラッグがカクついた（ユーザー指摘）。
  計測（`Bench.FrameCost`）: 合成 20ms → 6.7ms（スタンプを縮小済みコピー + 1 点の双線形に）、減色 11〜15ms は下書き時だけ。

## クラス
### Core（`Core/Board/`、純粋 C#）
| クラス | 役割 |
|---|---|
| `BoardDoc` | 下地（`BackgroundColor` / `Frame`〔`Raster`、320x240 ARGB〕+ `FrameId`。背景色とフレームは独立）、`Objects`（z 順。`Picture` と画像の層はない）、`Selected`、スナップショット undo/redo（50）、`Changed` / `SelectionChanged`。`Add` / `Remove` / `Restack` / `HitTest`、`Compositor`。`Load(BoardContent)` = 保存した看板（または空の看板）に丸ごと入れ替え、履歴・選択を空にして `Changed` を 1 回（[boardfile.md](boardfile.md)） |
| `BoardFile` / `BoardContent` / `IImageCodec` | 看板の保存・復元（`.kanban` = ZIP + 本体テキスト + PNG）。読み書きと検証は `Core`、PNG の符号化・復号だけ `IImageCodec`（Win の `PngCodec`）。詳細は [boardfile.md](boardfile.md) |
| `BoardObject` | 中心・倍率（縦横共通）・回転（ラジアン、時計回り）、ローカル⇔ページ変換、角、当たり判定、倍率の制限（長辺 4〜2000px） |
| `ImageObject`（基底） | 画像を表すオブジェクトの共通部分。画素（ARGB、複製間で共有）。縮小時は表示サイズに面積平均で縮めたコピー（サイズごとにキャッシュ）から 1 点の双線形補間で自由層に合成。透明部分はクリックで拾わない |
| `StampObject` | `ImageObject` の派生。ライブラリ id を持つ。画素はライブラリから読む |
| `PictureObject` | `ImageObject` の派生。元ファイル名を持つ（ライブラリには登録しない）。長辺 1280px 超は読み込み時に面積平均で縮める。置き方（収める / 埋める / 等倍、中央）で初期の倍率・位置を決め、新規は最背面に入れて選択 |
| `TextObject` | 文字列・フォント名・サイズ(px)・太字・色・縁取り色（-1 = なし）/幅。寸法は `Measure(ITextRasterizer)`。描画は固定層（2 値）、`services.Smooth` のときだけ（公式文字を除く）カバレッジで自由層に `Blend` |
| `ITextRasterizer` / `IRenderServices` | 文字のマスク化（Win: `GdiTextRasterizer`、テスト: `BlockText`）。`ITextRasterizer.Rasterize` = 2 値のマスク、`RasterizeCoverage` = アンチエイリアスのカバレッジ。`IRenderServices.Smooth` = 文字をなめらかに描くか（下の「フルカラーモードの文字」） |
| `Composite` / `CompositeCache` | RGB(float) + 固定番号（-1 = 自由）。`SetFixed` / `Blend` / `Clone`。キャッシュは背景色・フレームが同じ間は基礎層を使い回す（背景色が変わると作り直す） |
| `Handles` / `Drag` | 角 = 拡縮（中心基準）、上辺の上のつまみ = 回転（Shift で 15°、看板外に出るときは下辺側へ）、本体 = 移動 |
| `StampPrep` | 登録前処理: 背景を消す（縁の主要色から塗りつぶし、許容差 40）、透明な余白の切り詰め（縮小はしない）。`FitsBoard` = 320×240 以内か。`Scale`（面積平均）は描画時の縮小用。参考プロジェクト `ipx.android.Stamp` の移植 |
| `IndexedImage`（`Core/Raster.cs`） | パレット番号の画像（減色結果。透明は持たない）。フレームはフルカラーの `Raster` なので、ストローク計画などの描画側には透明は来ない |
| `Quantizer`（`Core/`） | `Quantize(Composite)`: 固定画素はそのまま。最近色は 64 段 RGB の LUT（使う色の組ごとに遅延構築、パレット色ちょうどは完全一致） |

### Win
| クラス | 役割 |
|---|---|
| `GdiTextRasterizer` | `GraphicsPath.AddString` → 変換 → `SmoothingMode.None` で塗り（縁取りは `Widen`）→ 2 値マスク（`Rasterize`）。`Smooth`（`IRenderServices`、設定可能、既定 false）と `RasterizeCoverage`（`SmoothingMode.AntiAlias` で塗ったカバレッジ）。フォントはインストール済み一覧（既定: Yu Gothic UI → メイリオ → …） |
| `ImageLibrary` / `StampLibrary` / `FrameLibrary` | `AppPaths.DataDir\stamps|frames\<id>.png` + `index.txt`（1 行 1 id、壊れた行・無いファイル・パス文字を含む行は無視、.tmp 経由で保存）。どれも ARGB で読み書きする（`Load`。フレームも同じ。前の版の 10 色フレームもそのまま読める）。削除: `Delete(ids)` は削除の記録を返し、ファイルを `trash\` に移す（最後の 1 回分だけ保持、`index.txt` の対象外。画面の起動時に `EmptyTrash`、CLI では空にしない＝画面を開いたまま CLI で登録しても「元に戻す」が消えない）。`Restore(記録)` で元の順番に戻す（削除後に一覧が変わっていれば、残っている直前の項目の後ろ） |
| `ClipboardContent` | `Read(IDataObject)`: ファイル > PNG（アルファ）> Bitmap > テキストの優先順位で 1 種類を返す（下の「クリップボードから貼り付け」） |
| `AppPaths` | `%APPDATA%\KanbanDrawer`、または環境変数 `KANBANDRAWER_DATA`（テスト・持ち運び用） |

### UI（`UI/Features/`, `UI/Widgets/`, `UI/EditorView.cs`）
```csharp
interface IFeature
{
    string Title { get; }                 // タブ名
    Control CreatePanel(EditorHost host); // タブの中身（1 回）
    bool Owns(BoardObject o);             // このタブが扱うオブジェクトか（選択するとタブが切り替わる）
    void OnSelection(BoardObject o);      // 選択が変わった（null = なし）
    void Load(Settings s); void Save(Settings s);
}
interface IAcceptsFiles { bool OpenFile(string path); } // ドロップされた画像（表示中のタブ優先、なければ最初の 1 つ）
interface IAcceptsPaste                                  // 貼り付け（クリップボード）。受け取れない種類は false を返す
{
    bool PasteImage(Raster image, string name);          // 画像データ（アルファつき ARGB）と名前
    bool PasteText(string text);                         // 前後の空白・空行を除いた、空でないテキスト
}
```
- 登録は `FeatureRegistry.Create()` の 1 か所（タブ順 = 重ね順の下から。起動時は先頭の下地を開く）: `FrameFeature`（下地）, `PictureFeature`（画像）, `StampFeature`（スタンプ）,
  `TextFeature`（文字）, `QuantizeFeature`（減色）。**機能を外す = この行と Feature のファイル（とその `BoardObject` 種類）を消す**。
- Feature 同士は参照しない。共有するものは `EditorHost`: `Doc`, `Quantize`, `Text`, `FullColor`, `Stamps`, `Frames`, `Settings`, `Log`, `Owner`,
  `Refresh()`, `AddObject(o)`（中央に置いて選択）, `CheckpointFor(key)`（つまみ等の連続変更を 1.5 秒単位で 1 つの undo にまとめる）。
- 設定キーは Feature ごとに接頭辞（`frame.` `stamp.` `text.` `picture.`〔`picture.fit` = 置き方〕。減色は初版からの `dither` 等）。
- 部品: `ResetSlider`（Windows 標準の TrackBar、1 刻み・吸着なし。上に ▼ = 既定値に戻す（位置は TBM_GETCHANNELRECT /
  TBM_GETTHUMBRECT から計算）、左右に －／＋（1 ずつ、押し続けると連続）、値は右の固定幅欄）、`PaletteSwatches`（10 色、
  「なし」付きも可）、`ThumbnailList`（タイル一覧。Ctrl+クリックで選択の切り替え、Shift+クリックで範囲、Ctrl+A ですべて。Ctrl / Shift 付きは「使う」をしない。選択中は半透明の青 + ✓、使用中の青い枠とは別。Delete で削除。**右クリックでメニュー**: 「削除」〔選択が複数なら「削除（n 個）」〕と「削除を元に戻す」〔戻せるものが無いときは無効〕。タイルの無い所の右クリックは「削除を元に戻す」だけ。
  イベント `DeleteRequested` と `RestoreRequested`）、`LibraryDelete`（スタンプ・下地共通。「選んだものを削除」ボタン〔何も選んでいなければ無効、選ぶと `選んだものを削除（n）`〕と、右クリックの「削除を元に戻す」〔`ThumbnailList.RestoreRequested`〕とログ。
  「削除を元に戻す」ボタンは無い〔M12 で右クリックのメニューへ〕。確認ダイアログは出さない。ボタンは各タブの「登録...」の行の右端に置く）、`StackPanel` / `Ui.Row` 等。
- `EditorView`: 1 倍表示 + 周囲 10px の余白（看板の端のハンドルも掴める）。クリック = 最前面を選択、ドラッグ = 移動、角 = 拡縮、
  丸 = 回転、矢印 = 1px（Shift 10px）、Delete = 削除、Esc = 選択解除。表示はフルカラー（編集中）か下書き（10 色、`ShowDraft`。最新の下書きがあるときだけ）。
- 描画（検出・入力方式・開始〔描画中は赤い中止〕・続きから・進捗・ログ）、下書きを作る・表示切替、上の帯（新規・開く・保存・元に戻す・やり直し・貼り付け・PNG に書き出す・使い方）は Feature ではなく
  `MainForm` 常設（[ui.md](ui.md)）。看板の保存・復元の処理は `UI/BoardFileCommands`（[boardfile.md](boardfile.md)）。
- 各タブの説明文は 1 行まで。共有の `ToolTip`（`EditorHost.Tips`）に詳しい説明を付け、操作方法は「使い方」ウィンドウへ（下の「各タブの説明文」）。
- `EditorHost` の主な API: `Invalidate()` / `RenderNow()`、`Composite`、`MakeDraft()` / `Draft` / `DraftStale` / `Plan` /
  `EnsureDraftAsync()`、`SetQuantize(opts)`、`FullColor`（入力方式がフルカラーか。`MainForm` が入力方式の変更ごとに設定する。下の「フルカラーモードの文字」）、イベント `Rendered` / `DraftChanged` / `QuantizeChanged`。
  M12 で足すもの: `Tips`（共有の `ToolTip`。各 Feature が `host.Tips.SetToolTip(control, 文)` で説明を付ける）、`ApplyQuantize(QuantizeOptions)` と
  イベント `QuantizeLoadRequested`（保存した看板の減色の設定を減色タブに反映する。下の「減色」。[boardfile.md](boardfile.md)）。

## 各 Feature
| タブ | できること |
|---|---|
| 画像 | 開く（ボタン / ドロップ / 貼り付け / exe への引数。貼り付けは `IAcceptsPaste.PasteImage`、名前「クリップボード」）→ `PictureObject` を最背面に置いて選択、置き方（全体を収める〔既定〕/ トリミングして埋める / 等倍）。選択中: 大きさ 1〜400%（100% = 読み込んだ画素の等倍）・回転 ±180°・看板に合わせる（今の置き方で置き直す）・前面へ・背面へ・看板から削除。画像を選ぶとこのタブに切り替わる（`Owns`） |
| 下地 | 背景色（10 色、常に有効）とフレーム一覧（「なし」+ 登録済み）を別々に選ぶ。登録（ファイル →「背景を消す」〔既定オフ、設定キー `frame.removeBg`、`StampPrep.RemoveBackground` でスタンプと同じ処理、切り詰めなし〕→ 合わせ方（既定: トリミングして埋める）で 320x240 に配置（アルファはそのまま）→ フルカラーのまま保存。減色は下書きで。「全体を収める」の余白は透明）。削除は複数選択・Delete・右クリックの「削除」・「選んだものを削除」ボタン（「登録...」の行の右端）、右クリックの「削除を元に戻す」（スタンプと共通） |
| スタンプ | 一覧（クリックで中央に等倍で置く。複数選択・削除・右クリックの「削除を元に戻す」は下地と共通、元画像の 1 画素 = 看板の 1 画素 = 100%）、登録（複数選択可、背景を消す〔既定オフ〕、切り詰め後 320×240 を超えるものは登録せず一覧で知らせる）、選択中: 大きさ 5〜400%・回転 ±180°・前面へ・背面へ・看板から削除 |
| 文字 | 文字の形: **公式と同じ（ドット文字）** / フォントを選ぶ（入力方式がフルカラーのあいだはなめらか〔アンチエイリアス〕に描く）。文字（複数行可）・サイズ（公式 10〜22px / 自由 6〜200px、モードごとに記憶）・太字（自由のみ）・色・縁取り（なし / 10 色）・縁の幅 1〜5・回転（自由のみ）・文字を追加。選択中の文字には即反映。貼り付け（`IAcceptsPaste.PasteText`）= 貼ったテキストを今の設定で追加 |
| 減色 | ディザ・強さ・明るさ・コントラスト・彩度（▼ で戻す）・使う色。保存した看板を開くと、その減色の設定が反映される（`ApplyOptions`、下） |

**減色タブへの反映**（看板を開く、[boardfile.md](boardfile.md)）: `QuantizeFeature` は `CreatePanel` で `host.QuantizeLoadRequested` を購読し、`ApplyOptions(QuantizeOptions o)` を呼ぶ。
`loading = true` の間にディザ（`DitherKind` → コンボの番号）・強さ（`Strength × 100`）・明るさ・コントラスト・彩度・使う色（`Enabled`）のコントロールを `o` に合わせ、`loading = false` にして `Apply()` を 1 回呼ぶ
（= `host.SetQuantize`、`QuantizeChanged`）。`Load(Settings)` と同じ「コントロールに入れてから 1 回 `Apply`」の形。`o.Enabled` が全部偽のときは無視する（`Apply` と同じ「1 つ以上選ぶ」の規則）。

## 各タブの説明文（M12）
各タブに残す説明文は**1 行まで**（約 32 文字以内。タブの幅 約 370px）。それ以上は共有の `ToolTip`（`host.Tips`）に付けるか、「使い方」ウィンドウ（[ui.md](ui.md)）に書く。
操作方法（ドラッグで移動・角で拡縮・Shift で 15° など）は「使い方」に集める。**今までの文は次のどこかに必ず残る**:

| タブ | 1 行に残す文 | 今までの文 → 行き先 |
|---|---|---|
| 下地 | フレームの下（「合わせ方」の下）に `登録時に 320×240 にして保存します。` | 「フレームの透明部分や、何も置いていない所に見える色です。」→ 背景色の見出し・色の ToolTip。「色は下書きのときに、画像・スタンプと一緒に 10 色にします。透明な部分（『全体を収める』の余白も）は透明のまま残ります。」→ フレームの一覧と「合わせ方」の ToolTip、「使い方」のライブラリ。「Ctrl / Shift+クリックで選んで、右クリック・Delete キーで削除できます。」→ `ThumbnailList` の ToolTip（既存）、「使い方」のライブラリ |
| 画像 | `画像を看板に置きます（スタンプ・文字の後ろ、何枚でも）。`（先頭の 1 行。「画像を開く...」の上） | 「余白・透明部分には下地が見えます。」→ 「置き方」の ToolTip、「使い方」。「ウィンドウへのドラッグ&ドロップでも開けます。」→ 「画像を開く...」の ToolTip、「使い方」。「プレビュー上: ドラッグで移動、角で拡縮、上の丸で回転（Shift で 15° ずつ）。」→ 「選択中の画像」の見出しの ToolTip、「使い方」の「看板の上の操作」 |
| スタンプ | `クリックで看板の中央に等倍（100%）で置きます。`（先頭の 1 行） | 「Ctrl / Shift+クリックで選んで、右クリック・Delete キーで削除。」→ `ThumbnailList` の ToolTip（既存の「クリック: 使う / Ctrl・Shift+クリック: 選ぶ / 右クリック: 削除」に「右クリック: 削除・削除を元に戻す」の形で更新）、「使い方」。「複数の画像をまとめて選べます。320×240 より大きい画像は登録しません。」→ 「スタンプを登録...」の ToolTip、「使い方」。選択中の「プレビュー上: ドラッグで移動…」→ 「選択中のスタンプ」の見出しの ToolTip、「使い方」 |
| 文字 | `10 色でにじまず描きます（フルカラーではなめらか）。`（先頭の 1 行） | 「選択中の文字は、ここでの変更がすぐ反映されます。」→ 文字の入力欄の ToolTip、「使い方」。「公式と同じ: InterPot の文字ツールと同じ字形（10〜22px・既定 12px）。」→ 「公式と同じ（ドット文字）」の選択肢の ToolTip |
| 減色 | `「下書き」で使います（背景色・文字は対象外）。`（先頭の 1 行） | 「対象はフレーム・画像・スタンプ」→ 見出し代わりのディザ行の ToolTip。「▼ を押すと元の値に戻ります。」→ 既存の ▼ の ToolTip（`ResetSlider`）があるので文は消す。「イラスト・文字はディザ『なし』、写真は誤差拡散がおすすめです。」→ ディザのコンボの ToolTip、「使い方」 |
- 説明文の行（`Ui.Note`）は 1 行の高さ（18）にそろえる。消えた分の高さは、そのタブの下側の余白になる（タブの中身は上から積む。見た目の隙間のための調整はしない）。
- 「使い方」の文章は [ui.md](ui.md)「使い方」ウィンドウに全文がある。

**ライブラリの行**（下地・スタンプ）: 「登録...」のボタン、「背景を消す」のチェック、右端に「選んだものを削除」（`LibraryDelete.DeleteButton`。右寄せ。`TableLayoutPanel` の 4 列目、3 列目が伸びる）を同じ 1 行に置く。
今までの「削除を元に戻す」ボタンの行はなくなる（その行の高さ分、一覧の下が詰まる）。

## クリップボードから貼り付け
Ctrl+V・上の帯の「貼り付け」（[ui.md](ui.md)）で、クリップボードの画像・テキストを看板に置く。読み取りは `MainForm`、置くのは各 Feature。

- **読み取り**（`Win/ClipboardContent`）: `ClipboardContent.Read(IDataObject)` が、上から順に最初に見つかった 1 種類だけを返す
  （`Kind` = `None` / `Files` / `Image` / `Text`）。`Clipboard.GetDataObject()` は `MainForm` が呼び、`Read` は `DataObject` を渡せば
  動く（単体テストできる。`Clipboard` 自体は触らない）。
  1. `FileDrop`（エクスプローラーでコピーしたファイル）: 拡張子が画像（png / jpg / jpeg / bmp / gif、「画像」タブの開くダイアログと同じ）で
     実在するものを順に `Files` へ。1 つもなければこの段階は無かったものとして次へ。
  2. `PNG` 形式（`MemoryStream`。アルファを保つ。ブラウザ・Office・画像編集ソフトが入れる）→ なければ `Bitmap`（`DataFormats.Bitmap`、
    DIB を含む。不透明）。どちらも `ImageLoader.ToRaster` で `Raster`（ARGB）にして `Image` へ。読めない・壊れている形式は無かったものとして次へ。
  3. `UnicodeText`（`DataFormats.UnicodeText`）: 前後の空白・空行（`Trim()`、改行を含む）を除いて、空でなければ `Text` へ。途中の
     改行・空行はそのまま（複数行の文字になる）。長さは切らない。
  - 画像とテキストが両方あるとき（ブラウザ・Office のコピー）は画像を優先する。どれも無ければ `None`。
  - 例外（クリップボードが他のアプリに掴まれている等）は `None` 扱い（落とさない）。
- **受け取り**: 画像・文字の Feature が `IAcceptsPaste` を実装する（`IAcceptsFiles` と同じく `IFeature` とは別の任意のインターフェース。
  Feature 同士は参照せず、`MainForm` が仲立ち）。受け取れない種類（`PictureFeature.PasteText`、`TextFeature.PasteImage`）は何もせず
  `false`。`MainForm.Paste()` は読んだ種類で選ぶ:
  - `Files`: 各ファイルをドロップと同じ（`IAcceptsFiles.OpenFile`。表示中のタブが受け取れればそこ、なければ「画像」タブ）。ファイルごとに
    1 回の undo（開けなかったファイルは `OpenFile` の通りエラー表示して次へ）。
  - `Image` / `Text`: 全 Feature から `IAcceptsPaste` を持つものを、表示中のタブ → 登録順の順に試し、最初に `true` を返したものに任せる
    （実際には画像 = 「画像」タブ、テキスト = 「文字」タブ）。タブは自動では先に切り替えない: 置いたオブジェクトが選択されて
    `Owns` の仕組み（`OnSelectionChanged`）でそのタブに切り替わる。
  - `None`: 何もせず、ログ「貼り付けられるものがありません」。
- **画像**（`PictureFeature.PasteImage`）: 「画像」タブで開いたときと同じ処理（`OpenFile` の読み込み以降を共通のメソッドにして使う）:
  長辺 1280px に縮小（`PictureObject.Shrink`）、今の「置き方」で配置、`Doc.Insert(0, …)` で最背面に入れて選択。名前（`PictureObject.FileName`）は
  「クリップボード」。ログは「画像: クリップボード (幅×高さ)」（縮小したときは `OpenFile` と同じ書式）。アルファは保たれる（透明部分は下が見える）。
  読み込み元フォルダ（`picture.folder`）は変えない。
- **文字**（`TextFeature.PasteText`）: 「文字を追加」と同じ — 今の 文字の形（公式 / フォント）・フォント・サイズ・太字・色・縁取り・縁の幅・
  回転の設定で `TextObject` を作り（`Text` だけ貼ったテキスト）、`Measure` して `host.AddObject`（看板の中央・選択）。文字欄（`text`）も貼った
  テキストに入れ替える。このとき欄の変更で今選択中の文字が書き換わらないよう、`Show` と同じ `showing` の守りの中で欄を書き換えてから
  追加する（追加で新しい文字が選択され、`OnSelection` → `Show` が欄・設定を新しい文字の値にそろえる）。空（空白・空行だけ）なら何もせず
  `false`（`ClipboardContent` が先にはじくので通常は来ない）。
- **undo**: 貼り付け 1 回 = 画像・文字ともオブジェクトを 1 つ入れるだけ = undo 1 回（`EndCheckpointBurst` を先に呼んで連続変更と
  混ざらないようにする。`OpenFile` / `Add` と同じ）。
- 経緯: 画面右のタブの「開く」「追加」を使わずに、ブラウザ・ペイント・Office・スクリーンショットなどから直接置けるようにした
  （`Clipboard` の変化は監視しない。ボタンを押したときに読むだけ）。

## フルカラーモードの文字（`IRenderServices.Smooth`、`EditorHost.FullColor`）
入力方式が「フルカラー（直接書き込み）」のときは減色しないので、文字を 1 値（パレットの 1 色・2 値のマスク）に丸める理由がない。
「フォントを選ぶ」の文字と縁取りを**アンチエイリアスで描き、自由な画素として合成**する（にじみ・ぎざぎざのない書き込みになる）。色はパレットの 10 色のまま。
- **API（実装はこのとおり）**:
  | 部品 | 内容 |
  |---|---|
  | `IRenderServices.Smooth` | `bool Smooth { get; }`。true = 文字をなめらかに描く。`GdiTextRasterizer` は設定可能なプロパティ（既定 false）。テストの代役（`BlockText` 等）は `false` を返す（net48 は既定インターフェース実装が使えないので、実装側に 1 行足す） |
  | `ITextRasterizer.RasterizeCoverage` | `void RasterizeCoverage(TextObject t, byte[] letter, byte[] outline, int pageW, int pageH)`。ページ大（行優先）の 2 つの配列にカバレッジ 0〜255（0 = 触れない、255 = 全部覆う）を**書き込む**（呼び出し側が 0 で初期化して渡す）。`letter` = 字の面、`outline` = 縁取りを含む領域（字の面の下も 255。縁取りなし / 幅 0 なら全部 0）。変換（倍率・回転・中心）は `Rasterize` と同じ。`t.Official` のときは 2 値と同じ内容（0 / 255）を返す |
  | `EditorHost.FullColor` | `bool FullColor { get; set; }`。設定で `Text.Smooth` を同じ値にし、変わったときだけ `Invalidate()`（再描画 + 下書きを古くする）。`MainForm.OnInputMode` が `host.FullColor = (入力方式 == フルカラー)` を呼ぶ（起動時の設定の復元も同じ経路）。下書き表示中でも古くなるだけで、自動では作り直さない |
- **`TextObject.Render`**: `Text` が空か `services?.Text == null` なら何もしない（今まで通り）。`!services.Smooth` または `Official` なら今までの 2 値の道（`Rasterize` → `SetFixed`）。
  それ以外は `RasterizeCoverage` で `letter` / `outline` を得て、画素ごとに **縁取り（`Outline >= 0` で `outline[i] > 0`）を先に**
  `target.Blend(i, 縁取り色の R, G, B, outline[i] / 255.0)`、**続けて字**（`letter[i] > 0`）を `Blend(i, 色の R, G, B, letter[i] / 255.0)`
  で重ねる（縁取りの上に字。端の画素は下の絵・縁取り・字がカバレッジに応じて混ざる）。`Blend` は画素を自由にするので減色・ディザの対象になる
  （フルカラーの書き込みでは減色しないので影響しない）。パレット色は `Palette.All[色番号]`。
- **`GdiTextRasterizer.RasterizeCoverage`**: `Rasterize` と同じ経路（`LocalPath`、同じ変換）を `SmoothingMode.AntiAlias`・`PixelOffsetMode.Half` で塗る。
  2 回に分ける（重なる縁を 1 枚の画像で混ぜない）: ① 縁取りの領域（`Widen` した輪郭 + 字の面）を白で塗った黒地の画像 → R チャンネル = `outline`、
  ② 字の面だけ → R チャンネル = `letter`。`Official` は 2 値の `RasterizeOfficial` の結果から作る。測る箱（`Measure`）は変えない（AA の薄い縁は箱の外に 1 画素出うる。ページの範囲で切るだけ）。
- **2 値の道は残す**: `Smooth` が false（テスト・ペンの入力方式・`Bench`・既定）なら今までと画素単位で同じ（固定・2 値・下書きは 10 色にそのまま）。
  ペンの入力方式に戻したら `FullColor = false` で再描画され、文字は固定・2 値に戻る。下書き（10 色）の文字がにじまないのはこの道の性質。
- フルカラーモード中は、下書きの作成と表示の切り替えを画面から隠す（書き込みには使わない。[ui.md](ui.md)）。「PNG に書き出す」で作る下書きは、なめらかな文字を減色したもの（ペンの下書きと同じ見た目ではない）。
- 文字の見た目の設定（太さ・縁の幅・色）は同じ。AA の縁取り（幅 1）は 2 値より細く見えるので、必要なら縁の幅を上げる。

## 公式と同じ文字（`TextObject.Official`、`Win/OfficialText`）
- 公式の文字ツール（`TextTool.drawText`）は Java の論理フォント `SansSerif`（12pt、普通の太さ）で `drawString`。JRE の fontconfig
  （`sequence.sansserif.windows-31j=alphabetic,japanese`）で英数字は **Arial**、それ以外は **MS ゴシック**（`exclusion.alphabetic`
  の範囲も MS ゴシック）。MS ゴシックには 7〜22px の手作りビットマップが埋め込まれていて（`msgothic.ttc` の EBLC）、AA なし・
  回転なしならそれが使われる。公式ツールに太字はない。
- 調査（`tools/fontcheck`）: ゲームの JRE で描いた字と GDI（`TextOut`、NONANTIALIASED_QUALITY）を比較 → MS ゴシックの字形は
  10〜22px で一致（文字送りは Java = サイズ、GDI は奇数サイズで +1 → `DotFont.JavaAdvance` で合わせる）、Arial は画素が違う、
  7〜9px は MS ゴシックでも一部違う、太字は Java が輪郭から太らせていて再現不可。
- **描画は公式と同じ Java で行う**（2026-10-26。一時期の「英数字の表を exe に埋め込む」方式は、Arial の字形データの再配布に
  当たるおそれがあるため廃止）:
  - InterPot3 のショートカットは `InterPot3.jar` を指すので、使われる Java は「.jar を開くプログラム」。`JavaHelper.FindJava` が
    `AssocQueryString(".jar", open)` でエクスプローラーと同じ規則（ユーザーの選択 → 既定）で引く（java.exe を優先、javaw.exe も可。
    .jar が圧縮ソフトに関連付けられていれば使わない）。
  - その Java で自作の描画プログラム `KanbanText`（`src/KanbanDrawer/Java/KanbanText.java` → `Resources/kanban-text.jar`
    〔作り直し方は README〕、exe に内蔵、`%LOCALAPPDATA%\KanbanDrawer\kanban-text.jar` に書き出す）を 1 つだけ常駐させ、
    標準入出力で「サイズ 文字列」→「幅 高さ 画素」。描き方は TextTool と同じ（SansSerif、行の高さ = `getHeight()`）。結果はキャッシュ。
  - 補助プロセスは Windows のジョブ（KILL_ON_JOB_CLOSE）に入れるので、看板屋が閉じても強制終了されても残らない。
  - Java が見つからない・起動できない場合は GDI で近似（`OfficialText.Approximate`: 日本語は MS ゴシックのビットマップで一致、英数字は
    1〜数画素違う）。そのときだけ「文字」タブに注意書き。Java の場所は画面に出さない。
- サイズは 10〜22px（既定 12px）。オブジェクトは回転・拡縮・太字なし（ハンドルは移動のみ、`CanRotate` / `CanScale` = false）、
  整数座標に置く、縁取りは字の画素を縁の幅だけ四方に広げる。
- 検証: `OfficialTextTests.SameAsTheProgramsTextToolAtEverySize`（看板屋の出力 = 内蔵 jar を .jar の Java で動かした公式と同じ描画、
  日本語・半角カナ・全角英数・記号・ASCII・欧文・ギリシャ・キリルの 3 行を 10〜22px 全サイズ）、`ApproximationMatchesJapaneseExactly`
  （Java なしの近似でも日本語は一致）。Java が無い PC では飛ばす。近似の文字ごとの差: `OfficialTextDiag`。

## 不変条件とテスト
- 透明は下地、文字は固定色 + 縁取り: `CompositorTests`
- 画像オブジェクト（置き方 3 種の倍率と位置、長辺 1280px への縮小、最背面に入る・undo で戻る、透明部分は拾わない、スタンプは従来通り）: `PictureTests`
- フレーム（ライブラリを往復してもフルカラー・アルファのまま、合成で透明の所は背景色・半透明は背景色と混ざる、画素は自由で下書きで減色・ディザされる、背景色の変更で基礎層が作り直される、前の版の 10 色フレームも読める、背景を消すと縁の色の領域が透明になる）: `FrameTests`
- ライブラリの削除（まとめて削除 → 元に戻すで順番ごと戻る、次の削除で前の分は戻せない、壊れた・無いファイルでも落ちない、起動時に trash を空にする）: `LibraryDeleteTests`（M12: 「選んだものを削除」ボタンは選択 0 で無効・選ぶと `（n）` つき、右クリックの「削除を元に戻す」は `CanRestore` が偽のとき無効。ボタンの見た目は画面確認）
- 看板の保存・復元（往復で合成が同じ、新しい版・壊れたファイル、ライブラリに無いスタンプ、`BoardDoc.Load`、減色の設定）: `BoardFileTests`（[boardfile.md](boardfile.md)）
- スタンプの拡縮・回転・当たり判定（透明部分は拾わない）: `CompositorTests.StampsAreTransformed`, `StampTransparencyIsHonouredForPicking`
- ハンドル位置・つまみの退避・移動/拡縮/回転/15° 吸着・倍率制限: `EditingTests`
- undo/redo（オブジェクト・下地・選択）、z 順: `EditingTests.UndoRedoRestoresObjectsBackgroundAndSelection`, `RestackAndHitTestUseTheZOrder`
- 背景除去と切り詰め、320×240 の判定: `StampPrepTests`。ライブラリ（壊れた行を無視）: `LibraryTests`
- GDI+ の文字が 2 値・縁取りあり・測った箱の中、再描画速度: `GdiTextTests`
- フルカラーモードの文字: `SmoothTextTests`（代役のラスタライザが 0 / 128 / 255 のカバレッジを返す）— `Smooth = false` は今までと同じ（固定・2 値）、`Smooth = true` で 128 の画素が自由（`Fixed = -1`）で背景と文字色の中間の色になる・255 はちょうど文字色（自由）・縁取りは字の下（字の画素は字の色、縁取りだけの画素は縁取り色）・縁取りなしなら `outline` は使わない・`Official` は `Smooth` でも 2 値で固定・空の文字は何もしない。GDI 実物: `Smooth` の `RasterizeCoverage` に 0 と 255 以外の値がある、`Rasterize`（2 値）と覆う範囲がほぼ同じ、`Smooth` が false のとき `Rasterize` は変わらない
- 公式文字: Java の描画と全サイズで画素一致、近似でも日本語は一致、回転・拡縮しない・整数座標、縁取り: `OfficialTextTests`
- 貼り付け（`ClipboardContent.Read` を `DataObject` で）: 形式の優先順位（ファイル > PNG > Bitmap > テキスト、画像とテキストが両方なら画像、画像でないファイルは飛ばして次へ）、PNG のアルファが残る（Bitmap は不透明）、テキストの前後の空白・空行除去（途中の改行は残す）、空・空白だけ・何も無いなら `None`（何も置かない）: `PasteTests`。画像・文字の追加そのものは既存の処理（`PictureTests` など）
- 画面込みの通し: `tools/gui-e2e.sh`（開発環境のテスト）

## 落とし穴
- WinForms の `Control.Handle` / `Control.Margin` と名前が衝突する（`EditorView` では `Part` 別名、`BoardMargin`）。
- 前の版（v0.1）で登録したフレームは不透明（背景色で塗りつぶし・10 色化済み）のまま。今は不透明のフルカラー画像として扱うので下書きでは減色・ディザされるが、透明は戻せない（登録し直す）。
- 文字の縁取りは `GraphicsPath.Widen`。細い縁（1px）は字形によって途切れることがある。

## 未テスト
- 高 DPI での `EditorView`（1 倍表示は物理ピクセル前提。`AutoScaleMode.Dpi` で周りだけ拡大される想定）。
- 編集中の看板の保存・復元は `.kanban` ファイル（[boardfile.md](boardfile.md)）。開いているファイルは終了すると覚えない（保存していない変更は、閉じるときの確認で保存できる）。
- ライブラリの右クリックのメニュー・ToolTip の文言の見え方（画面確認）。
