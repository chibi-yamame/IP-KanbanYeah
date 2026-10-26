# 編集機能（Feature 構成・合成・減色）

## 目的
看板の絵を「下地 + オブジェクト（画像・スタンプ・文字）」の重ね合わせで作り、10 色に変換する。各機能はモジュール（Feature）として
独立させ、追加・削除が「登録 1 行 + ファイルの追加・削除」で済むようにする。

## 層の重ね順（下から）
| 層 | 中身 | 色の扱い |
|---|---|---|
| 下地 | 背景色（パレット色）+ 登録フレーム（320x240、登録時に減色済み、透明あり）。フレームの透明の画素は背景色 | **固定**（パレット番号のまま） |
| オブジェクト | 画像・スタンプ・文字を z 順に（後ろほど前面）。選択・移動・回転・拡縮。新しい画像は先頭（最背面）に入る | 画像・スタンプ = 自由、文字 = **固定** |

- 「固定」画素は減色しない。ディザの誤差を受け取らず、渡しもしない → フレームと文字がにじまない。
- 自由な画素（画像・スタンプ、半透明の縁を含む）だけを明るさ等で調整して減色・ディザする。半透明の縁は下の色と混ぜてから減色。
- 画像は独立した層ではなくオブジェクト。余白・透明部分は下にあるもの（他のオブジェクト、なければ下地）が見える。
- 文字はアンチエイリアスなしで描く（1 画素単位でパレット色、縁取りもパレット色）。

## 入口・流れ（編集 = フルカラー、下書き = 10 色）
- **編集中**: `Doc.Changed` → `EditorHost.Invalidate()`。再描画はメッセージループ 1 周に 1 回だけ（`BeginInvoke` でまとめる）。
  `CompositeCache.Build`（下地の層をキャッシュし、その複製にオブジェクトだけ描く）→ `Rendered` → `EditorView` が
  フルカラーで表示。減色も本数計算もしない（1 フレーム数 ms）。
- **下書き**（左の「下書き」ボタン / 「下書き（10 色）」表示 / 描画開始 / PNG 保存）: `MakeDraft()` = `Quantizer.Quantize(composite)` →
  `Draft`、250ms 後にバックグラウンドで `StrokePlanner.PlanFull` → `Plan`（統計表示）。`DraftChanged` で通知。
- 下書きの後に編集すると `DraftStale`（表示はカラーに戻り、統計は「作り直してください」）。描画開始は必ず最新の下書きを描く
  （古ければその場で作る）。下書き表示中に「減色」タブを変えると、その場で下書きを作り直す（`SetQuantize` → `QuantizeChanged`）。
- 経緯: 初版は変更のたびに合成 + 減色を同期実行（約 32ms/回、マウス移動ごと）していて、ドラッグがカクついた（ユーザー指摘）。
  計測（`Bench.FrameCost`）: 合成 20ms → 6.7ms（スタンプを縮小済みコピー + 1 点の双線形に）、減色 11〜15ms は下書き時だけ。

## クラス
### Core（`Core/Board/`、純粋 C#）
| クラス | 役割 |
|---|---|
| `BoardDoc` | 下地（`BackgroundColor` / `Frame` + `FrameId`。背景色とフレームは独立）、`Objects`（z 順。`Picture` と画像の層はない）、`Selected`、スナップショット undo/redo（50）、`Changed` / `SelectionChanged`。`Add` / `Remove` / `Restack` / `HitTest`、`Compositor` |
| `BoardObject` | 中心・倍率（縦横共通）・回転（ラジアン、時計回り）、ローカル⇔ページ変換、角、当たり判定、倍率の制限（長辺 4〜2000px） |
| `ImageObject`（基底） | 画像を表すオブジェクトの共通部分。画素（ARGB、複製間で共有）。縮小時は表示サイズに面積平均で縮めたコピー（サイズごとにキャッシュ）から 1 点の双線形補間で自由層に合成。透明部分はクリックで拾わない |
| `StampObject` | `ImageObject` の派生。ライブラリ id を持つ。画素はライブラリから読む |
| `PictureObject` | `ImageObject` の派生。元ファイル名を持つ（ライブラリには登録しない）。長辺 1280px 超は読み込み時に面積平均で縮める。置き方（収める / 埋める / 等倍、中央）で初期の倍率・位置を決め、新規は最背面に入れて選択 |
| `TextObject` | 文字列・フォント名・サイズ(px)・太字・色・縁取り色（-1 = なし）/幅。寸法は `Measure(ITextRasterizer)`、描画は固定層 |
| `ITextRasterizer` / `IRenderServices` | 文字のマスク化（Win: `GdiTextRasterizer`、テスト: `BlockText`） |
| `Composite` / `CompositeCache` | RGB(float) + 固定番号（-1 = 自由）。`SetFixed` / `Blend` / `Clone`。キャッシュは背景色・フレームが同じ間は基礎層を使い回す（背景色が変わると作り直す） |
| `Handles` / `Drag` | 角 = 拡縮（中心基準）、上辺の上のつまみ = 回転（Shift で 15°、看板外に出るときは下辺側へ）、本体 = 移動 |
| `StampPrep` | 登録前処理: 背景を消す（縁の主要色から塗りつぶし、許容差 40）、透明な余白の切り詰め（縮小はしない）。`FitsBoard` = 320×240 以内か。`Scale`（面積平均）は描画時の縮小用。参考プロジェクト `ipx.android.Stamp` の移植 |
| `IndexedImage`（`Core/Raster.cs`） | パレット番号の画像。`IndexedImage.Transparent = 255` = 透明（`ToRaster` はアルファ 0）。`Compositor.BuildBase` はフレームの透明の画素を `BackgroundColor` にするので、ストローク計画などの描画側には透明は来ない |
| `Quantizer`（`Core/`） | `Quantize(Composite)`: 固定画素はそのまま。最近色は 64 段 RGB の LUT（使う色の組ごとに遅延構築、パレット色ちょうどは完全一致） |

### Win
| クラス | 役割 |
|---|---|
| `GdiTextRasterizer` | `GraphicsPath.AddString` → 変換 → `SmoothingMode.None` で塗り（縁取りは `Widen`）→ 2 値マスク。フォントはインストール済み一覧（既定: Yu Gothic UI → メイリオ → …） |
| `ImageLibrary` / `StampLibrary` / `FrameLibrary` | `AppPaths.DataDir\stamps|frames\<id>.png` + `index.txt`（1 行 1 id、壊れた行・無いファイル・パス文字を含む行は無視、.tmp 経由で保存）。フレームはパレット番号に戻して読む（PNG のアルファ 0 は透明に戻す。保存も透明はアルファ 0）。削除: `Delete(ids)` は削除の記録を返し、ファイルを `trash\` に移す（最後の 1 回分だけ保持、起動時に空にする。`index.txt` の対象外）。`Restore(記録)` で元の順番に戻す |
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
```
- 登録は `FeatureRegistry.Create()` の 1 か所（タブ順）: `PictureFeature`（画像）, `FrameFeature`（下地）, `StampFeature`（スタンプ）,
  `TextFeature`（文字）, `QuantizeFeature`（減色）。**機能を外す = この行と Feature のファイル（とその `BoardObject` 種類）を消す**。
- Feature 同士は参照しない。共有するものは `EditorHost`: `Doc`, `Quantize`, `Text`, `Stamps`, `Frames`, `Settings`, `Log`, `Owner`,
  `Refresh()`, `AddObject(o)`（中央に置いて選択）, `CheckpointFor(key)`（つまみ等の連続変更を 1.5 秒単位で 1 つの undo にまとめる）。
- 設定キーは Feature ごとに接頭辞（`frame.` `stamp.` `text.` `picture.`〔`picture.fit` = 置き方〕。減色は初版からの `dither` 等）。
- 部品: `ResetSlider`（Windows 標準の TrackBar、1 刻み・吸着なし。上に ▼ = 既定値に戻す（位置は TBM_GETCHANNELRECT /
  TBM_GETTHUMBRECT から計算）、左右に －／＋（1 ずつ、押し続けると連続）、値は右の固定幅欄）、`PaletteSwatches`（10 色、
  「なし」付きも可）、`ThumbnailList`（タイル一覧。Ctrl+クリックで選択の切り替え、Shift+クリックで範囲、Ctrl+A ですべて。Ctrl / Shift 付きは「使う」をしない。選択中は半透明の青 + ✓、使用中の青い枠とは別。右クリック・Delete で削除）、`StackPanel` / `Ui.Row` 等。
- `EditorView`: 1 倍表示 + 周囲 10px の余白（看板の端のハンドルも掴める）。クリック = 最前面を選択、ドラッグ = 移動、角 = 拡縮、
  丸 = 回転、矢印 = 1px（Shift 10px）、Delete = 削除、Esc = 選択解除。表示はフルカラー（編集中）か下書き（10 色、`ShowDraft`。最新の下書きがあるときだけ）。
- 描画（検出・入力方式・開始・続きから・中止・進捗・ログ）、下書き・表示切替、undo/redo・PNG 保存は Feature ではなく `MainForm` 常設。
- `EditorHost` の主な API: `Invalidate()` / `RenderNow()`、`Composite`、`MakeDraft()` / `Draft` / `DraftStale` / `Plan` /
  `EnsureDraftAsync()`、`SetQuantize(opts)`、イベント `Rendered` / `DraftChanged` / `QuantizeChanged`。

## 各 Feature
| タブ | できること |
|---|---|
| 画像 | 開く（ボタン / ドロップ / exe への引数）→ `PictureObject` を最背面に置いて選択、置き方（全体を収める〔既定〕/ トリミングして埋める / 等倍）。選択中: 大きさ 1〜400%（100% = 読み込んだ画素の等倍）・回転 ±180°・看板に合わせる（今の置き方で置き直す）・前面へ・背面へ・看板から削除。画像を選ぶとこのタブに切り替わる（`Owns`） |
| 下地 | 背景色（10 色、常に有効）とフレーム一覧（「なし」+ 登録済み）を別々に選ぶ。登録（ファイル → 合わせ方（既定: トリミングして埋める）→ アルファ < 128 は透明、他はアルファを無視して「減色」設定で 10 色化 → 保存。「全体を収める」の余白は透明）。削除は複数選択・Delete・右クリック・「選んだものを削除」・「削除を元に戻す」（スタンプと共通） |
| スタンプ | 一覧（クリックで中央に等倍で置く。複数選択・削除・「削除を元に戻す」は下地と共通、元画像の 1 画素 = 看板の 1 画素 = 100%）、登録（複数選択可、背景を消す〔既定オフ〕、切り詰め後 320×240 を超えるものは登録せず一覧で知らせる）、選択中: 大きさ 5〜400%・回転 ±180°・前面へ・背面へ・看板から削除 |
| 文字 | 文字の形: **公式と同じ（ドット文字）** / フォントを選ぶ。文字（複数行可）・サイズ（公式 10〜22px / 自由 6〜200px、モードごとに記憶）・太字（自由のみ）・色・縁取り（なし / 10 色）・縁の幅 1〜5・回転（自由のみ）・文字を追加。選択中の文字には即反映 |
| 減色 | ディザ・強さ・明るさ・コントラスト・彩度（▼ で戻す）・使う色 |

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
- フレームは登録時のまま出る、画像のディザがフレームに漏れない、透明は下地、文字は固定色 + 縁取り: `CompositorTests`
- 画像オブジェクト（置き方 3 種の倍率と位置、長辺 1280px への縮小、最背面に入る・undo で戻る、透明部分は拾わない、スタンプは従来通り）: `PictureTests`
- フレームの透明（ライブラリを往復しても透明のまま、合成で透明の画素だけが背景色になり背景色の変更でそこだけ変わる、登録の減色が背景色の影響を受けない）: `FrameTests`
- ライブラリの削除（まとめて削除 → 元に戻すで順番ごと戻る、次の削除で前の分は戻せない、壊れた・無いファイルでも落ちない、起動時に trash を空にする）: `LibraryDeleteTests`
- スタンプの拡縮・回転・当たり判定（透明部分は拾わない）: `CompositorTests.StampsAreTransformed`, `StampTransparencyIsHonouredForPicking`
- ハンドル位置・つまみの退避・移動/拡縮/回転/15° 吸着・倍率制限: `EditingTests`
- undo/redo（オブジェクト・下地・選択）、z 順: `EditingTests.UndoRedoRestoresObjectsBackgroundAndSelection`, `RestackAndHitTestUseTheZOrder`
- 背景除去と切り詰め、320×240 の判定: `StampPrepTests`。ライブラリ（壊れた行を無視、フレームが番号に戻る）: `LibraryTests`
- GDI+ の文字が 2 値・縁取りあり・測った箱の中、再描画速度: `GdiTextTests`
- 公式文字: Java の描画と全サイズで画素一致、近似でも日本語は一致、回転・拡縮しない・整数座標、縁取り: `OfficialTextTests`
- 画面込みの通し: `tools/gui-e2e.sh`（開発環境のテスト）

## 落とし穴
- WinForms の `Control.Handle` / `Control.Margin` と名前が衝突する（`EditorView` では `Part` 別名、`BoardMargin`）。
- フレームは「その時点の減色設定」で固定される。後から減色設定を変えてもフレームは変わらない（仕様）。
- 透明を持つのは新しく登録したフレームだけ。前の版（v0.1）で登録したフレームは不透明（背景色で塗りつぶし済み）のままで、透明は戻せない（登録し直す）。
- 文字の縁取りは `GraphicsPath.Widen`。細い縁（1px）は字形によって途切れることがある。

## 未テスト
- 高 DPI での `EditorView`（1 倍表示は物理ピクセル前提。`AutoScaleMode.Dpi` で周りだけ拡大される想定）。
- 編集中の状態の保存・復元（未実装: 終了すると看板の構成は消える。ライブラリと設定は残る）。
