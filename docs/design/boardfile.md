# 看板の保存・復元（`.kanban`）

## 目的
編集中の看板（背景色・フレーム・置いたオブジェクト・減色の設定）を 1 つのファイルに保存し、後で開いて続きを編集できるようにする。
ファイルは**それだけで完結**する（ライブラリからスタンプ・フレームを消しても、別の PC に持っていっても開ける）。
ゲームへ描く機能（[drawing.md](drawing.md)）や画像の書き出し（上の帯の「PNG に書き出す」）とは別。

## 保存するもの / しないもの
| 保存する | 保存しない |
|---|---|
| 背景色（パレット番号）、フレーム（ARGB の画像そのもの + `FrameId`）、オブジェクトを重ね順どおり（共通: 位置・拡縮・回転。画像 = 画素 + 元のファイル名、スタンプ = 画素 + `Id`、文字 = 文字・フォント名・サイズ・太字・色・縁取り色・縁の幅・公式と同じか）、減色の設定（ディザ・強さ・明るさ・コントラスト・彩度・使う色） | 元に戻す / やり直しの履歴、選択、下書き（10 色の絵・ストローク計画）、表示（カラー / 下書き）、入力方式・タブ・ウィンドウなど画面の設定 |

- 明るさ・コントラスト・彩度も入れる（`QuantizeOptions` の全項目。入れないと開いたときに下書きの絵が変わる。依頼文の「ディザ・強さ・使う色」に足した）。
- 文字の描画結果（マスク）は持たない。文字は文字列と属性だけを持ち、開いたときに今の PC のフォントで描き直す（公式と同じ文字は Java で描く）。

## ファイル形式
拡張子 `.kanban`。**ZIP**（`System.IO.Compression`、.NET Framework 4.8 標準）。エントリ名は半角英数字（日本語を入れない）:

| エントリ | 中身 | 圧縮 |
|---|---|---|
| `board.txt` | 本体（UTF-8、BOM なし、改行 `\n`）。下の「本体の書式」 | 通常 |
| `frame.png` | フレームの画像（320x240、ARGB。フレームがあるときだけ） | なし（PNG は圧縮済み） |
| `images/<i>.png` | `<i>` 番目のオブジェクト（0 始まり、重ね順の添字）が画像またはスタンプのときの画素（ARGB） | なし |

PNG は透明（アルファ）を保つ 32bit。ZIP の他のエントリ（知らない名前）は無視する。

### 本体の書式（`board.txt`）
JSON は使わず、設定ファイル（[ui.md](ui.md)「設定」）と同じ **1 行 1 つの `キー=値`**。理由: .NET Framework 4.8 に標準の JSON 部品がなく、
キーの数が少なく、手で開いて読める。最初の `=` までがキー、残りが値（値に `=` があってもよい）。空行は無視。同じキーが 2 回あれば後のもの。知らないキーは無視する
（同じ版数のあいだに項目を足しても古い看板屋が開ける）。

- **文字列の値**（`object.<i>.text` `.font` `.name` `.id`、`frame.id`）は `\` → `\\`、改行 `\n` → `\n`（2 文字）、`\r` → `\r`（2 文字）に置き換えて 1 行にする。読むときに戻す。
- **数値**: 整数はそのまま。小数（位置・拡縮・回転・サイズ）は `CultureInfo.InvariantCulture` の `"R"`（往復で同じ値）。真偽は `0` / `1`。
- **色**はパレット番号（0〜9、`Core/Palette.cs` の順）。縁取りなしは `-1`。

| キー | 意味・範囲 |
|---|---|
| `kanban-board` | **必ず最初の行**。形式の版数（整数、今は `1`）。魔法の文字列を兼ねる |
| `background` | 背景色のパレット番号 0〜9 |
| `frame.entry` | フレームの画像のエントリ名（`frame.png`）。無ければフレームなし |
| `frame.id` | `FrameId`（任意。ライブラリに無くてもよい）。`frame.entry` があるときだけ意味がある |
| `quantize.dither` | `none` / `fs` / `atkinson` / `bayer`（CLI の `--dither` と同じ名前） |
| `quantize.strength` | 0〜100（%） |
| `quantize.brightness` `quantize.contrast` | −100〜100 |
| `quantize.saturation` | 0〜200（%） |
| `quantize.colors` | 10 文字の `0` / `1`（設定 `colors` と同じ。1 つ以上 `1`） |
| `objects` | オブジェクトの数 N（0〜1000） |
| `object.<i>.type` | `picture` / `stamp` / `text`（`i` は 0〜N−1、重ね順の下から。連続していること） |
| `object.<i>.x` `.y` | 中心の位置（`Cx` `Cy`）。有限の数 |
| `object.<i>.scale` | 拡縮（`Scale`、> 0 の有限の数）。読んだ後 `ClampScale` |
| `object.<i>.rot` | 回転（`Rot`、ラジアン、有限の数） |
| `object.<i>.image` | picture / stamp: 画素のエントリ名（`images/<i>.png`） |
| `object.<i>.name` | picture: 元のファイル名（`PictureObject.FileName`。無ければ空） |
| `object.<i>.id` | stamp: ライブラリの id（`StampObject.Id`。ライブラリに無くてもよい） |
| `object.<i>.text` `.font` | text: 文字（空でない、10000 文字まで）、フォント名（公式と同じなら空でもよい） |
| `object.<i>.size` | text: サイズ（px、1〜2000） |
| `object.<i>.bold` | text: 0 / 1 |
| `object.<i>.color` | text: 0〜9 |
| `object.<i>.outline` `.outline.width` | text: 縁取り色（−1〜9）、縁の幅（0〜50） |
| `object.<i>.official` | text: 0 / 1（公式と同じ文字。1 のとき `rot` `scale` は読み込み後に 0 / 1 に正す〔`CanRotate` `CanScale` が偽〕） |

例（背景白・フレームなし・スタンプ 1 つ）:
```
kanban-board=1
background=2
quantize.dither=fs
quantize.strength=100
quantize.brightness=0
quantize.contrast=0
quantize.saturation=100
quantize.colors=1111111111
objects=1
object.0.type=stamp
object.0.x=160
object.0.y=120
object.0.scale=1
object.0.rot=0
object.0.id=ab12cd34
object.0.image=images/0.png
```

### 版数の方針
- 今の版数は `1`。**互換を壊す変更**（キーの意味の変更・必須キーの追加）のときだけ版数を上げる。任意のキーの追加は版数を上げない（知らないキーは無視されるため）。
- `kanban-board` が看板屋の知っている版より大きいファイルは**開かない**（読む前に判定し、他のキーは解釈しない）。古い版数のファイルは、版が増えたときに読み込みで変換する。

## 読み込み時の検証と失敗時の振る舞い
読み込みは**最後まで読んで検証してから**画面に反映する（途中までの内容を今の編集に混ぜない）。失敗したら今の編集は何も変えない（消さない）。

| 場面 | 結果 | 利用者に出すメッセージ（メッセージ欄 + ログ。CLI はログのみ） |
|---|---|---|
| ファイルが無い・開けない（`IOException` など） | 開かない | `ファイルを開けません: <メッセージ>` |
| ZIP でない・`board.txt` が無い・1 行目が `kanban-board=` でない・版数が整数でない | 壊れている | `看板のファイルが壊れているため開けません（<理由>）` |
| 版数が今の版より大きい | 新しい版 | `このファイルは新しい版の看板屋で保存されています（形式 <n>）。看板屋を新しくしてから開いてください` |
| 必須キーが無い・値が範囲外（色・サイズ・非有限の数）・`objects` と実際のオブジェクトの数が合わない・`type` が知らない値 | 壊れている | 同上（理由に該当のキー名を入れる） |
| 画像のエントリが無い / PNG の符号がない / 幅・高さが範囲外（フレームは 320x240 ちょうど、picture・stamp は 1〜1280）/ デコードに失敗 | 壊れている | 同上 |
| 大きすぎる（`board.txt` が 4 MB 超、画像エントリが 16 MB 超、オブジェクトが 1000 超、デコード後の画素の合計が 32,000,000 超） | 壊れている（ZIP 爆弾・メモリ対策。エントリの宣言サイズを信用せず、上限+1 バイトまでしか読まない） | 同上 |
| フォント名がこの PC に無い | **開く**。そのオブジェクトは既定のフォント（`GdiTextRasterizer.DefaultFont()`）に置き換え、警告をログに 1 フォントにつき 1 回 | `フォント「<名前>」がこの PC に無いため、既定のフォントで開きました` |
| ライブラリに無いスタンプ id・フレーム id | **開く**（画素はファイルのものを使う）。スタンプは一覧にいなくても看板にある。ログなし | — |
| 知らないキー・知らない ZIP エントリ | 無視 | — |

PNG のサイズは、デコードする前に PNG の先頭（8 バイトの符号 + `IHDR` の幅・高さ）を `Core` が読んで範囲を確かめる（巨大な画像を確保しない）。

## クラスと公開 API
### Core（`Core/Board/BoardFile.cs`、WinForms・System.Drawing 非依存）
**ZIP も `Core` で扱う**（`System.IO.Compression` は WinForms / System.Drawing ではなく、.NET Framework 標準の純粋なライブラリ。csproj に
`<Reference Include="System.IO.Compression" />` を足す）。こうすると形式の読み書きと検証が全部 `tests/` の単体テストになる。`Core` が持てないのは PNG の符号化・復号だけで、
`IImageCodec` の裏に置く（`Win/`）。

```csharp
namespace KanbanDrawer.Core.Board
{
    public interface IImageCodec                       // PNG の符号化・復号（Win/PngCodec が実装。テストは生の画素を詰める代役）
    {
        byte[] EncodePng(Raster image);                // ARGB、アルファを保つ
        Raster DecodePng(byte[] png);                  // 壊れていれば例外（呼び出し側が「壊れている」にする）
    }

    public sealed class BoardContent                   // ファイルの中身 = 反映前の看板
    {
        public int BackgroundColor;
        public Raster Frame;                           // null = なし
        public string FrameId;
        public List<BoardObject> Objects = new List<BoardObject>();
        public QuantizeOptions Quantize;               // null = 変えない（「新規」のとき）
        public List<string> Warnings = new List<string>(); // 利用者に見せる警告（フォントの置き換えなど）
        public static BoardContent Empty();            // 白・フレームなし・オブジェクトなし・Quantize = null
    }

    public enum BoardFileProblem { Corrupt, Newer }
    public sealed class BoardFileException : Exception // Message は利用者に見せる文（上の表）
    {
        public BoardFileProblem Problem { get; }
    }

    public static class BoardFile
    {
        public const int Version = 1;
        public const string Extension = ".kanban";

        // doc の今の内容と減色の設定を書く（stream は書き込み可・シーク可。閉じない）
        public static void Write(BoardDoc doc, QuantizeOptions quantize, IImageCodec codec, Stream output);

        // 読んで検証する。doc には触らない。失敗は BoardFileException。
        //   text: 文字の寸法を測る（TextObject.Measure）。fontResolver: フォント名 → 使う名前
        //   （この PC に無ければ既定のフォント。同じ名前を返せば警告なし。null なら置き換えない）
        public static BoardContent Read(Stream input, IImageCodec codec, ITextRasterizer text,
                                        Func<string, string> fontResolver);
    }
}
```
- `BoardDoc` に `public void Load(BoardContent c)` を足す（`Core/Board/BoardDoc.cs`）: `BackgroundColor` `Frame` `FrameId` を置き換え、`Objects` を入れ替え、undo / redo の履歴を空にし、
  選択を外して（`SelectionChanged`）`Changed` を 1 回だけ起こす。`Quantize` は扱わない（`BoardDoc` は減色の設定を持たない）。`BoardContent.Empty()` を渡すと「新しく始める」になる。
- `Write` は `Objects` の添字で `images/<i>.png` を名付ける。`PictureObject` / `StampObject` の `Picture`（`Raster`）をそのまま `EncodePng`。`Frame` が 320x240 でなければフレームなしとして書く
  （`Compositor.BuildBase` と同じ扱い）。`BoardDoc` の `Selected` や履歴は見ない。
- `Read` の文字は `TextObject` を作り、`fontResolver` で `Font` を置き換え（`Official` は置き換えない）、`Measure(text)` する。

### Win（`Win/PngCodec.cs`）
`PngCodec : IImageCodec`。`EncodePng` = `ImageLoader.ToBitmap(r)` → `Bitmap.Save(MemoryStream, ImageFormat.Png)`。`DecodePng` = `MemoryStream` から `Bitmap` →
`Format32bppArgb` の複製 → `ImageLoader.ToRaster`（ストリームを閉じても使える形にする）。アルファが 0 の画素の RGB は保証しない（合成に影響しない）。

### UI（`UI/BoardFileCommands.cs`、新規。開く・保存・確認・変更の印・タイトルを**全部ここに持つ**）
```csharp
internal sealed class BoardFileCommands
{
    public BoardFileCommands(Form owner, EditorHost host, Settings settings, Func<bool> isBusy);

    public string Title { get; }          // ウィンドウタイトル（下の規則）
    public event Action TitleChanged;     // タイトルが変わった（MainForm が Text に反映する）
    public bool Dirty { get; }            // 保存していない変更がある
    public string CurrentPath { get; }    // 開いた / 保存したファイル。無ければ null

    public void New();                    // 「新規」: 変更があれば確認 → 空の看板に
    public void Open();                   // 「開く」: 変更があれば確認 → ファイル選択 → 読み込み
    public bool OpenPath(string path);    // ドロップ・exe の引数: 確認 → 読み込み。開けたら true
    public bool Save();                   // 「保存」: CurrentPath があれば上書き、無ければ SaveAs。保存したら true
    public bool SaveAs();                 // 「名前を付けて保存」: ファイル選択 → 保存。保存したら true
    public bool ConfirmDiscard();         // 閉じる前: 変更が無ければ true、あれば確認（はい = 保存。保存できなければ false）

    public static string TitleOf(string path, bool dirty); // 純粋関数（単体テスト用）
}
```

#### layout と save の境目（2 人の Opus が別ブランチで並行して作る）
| 担当 | 触るファイル | 触らないファイル |
|---|---|---|
| **save**（`m12/save`） | `Core/Board/BoardFile.cs`（新規）、`Core/Board/BoardDoc.cs`（`Load` を足すだけ）、`Win/PngCodec.cs`（新規）、`UI/BoardFileCommands.cs`（新規）、`Program.cs`（`--board` `--no-draw`）、`KanbanDrawer.csproj`（`System.IO.Compression`）、`UI/Features/EditorHost.cs`（`ApplyQuantize` とイベント `QuantizeLoadRequested` を足すだけ）、`UI/Features/QuantizeFeature.cs`（`QuantizeLoadRequested` の購読と `ApplyOptions` を足すだけ）、テスト `BoardFileTests.cs`（新規） | **`UI/MainForm.cs` には触らない**、`UI/Features/` の他、`UI/Widgets/` |
| **layout**（`m12/layout`） | `UI/MainForm.cs`、`UI/Features/*.cs`（説明文・ToolTip・ライブラリの行）、`UI/Features/LibraryDelete.cs`、`UI/Features/EditorHost.cs`（`Tips` を足すだけ）、`UI/Widgets/*`、`UI/UsageForm.cs`（新規） | `Core/`、`Win/`、`Program.cs`、`BoardFileCommands.cs` の中身 |

- **`MainForm` が `BoardFileCommands` を使う箇所は次の 7 つだけ**（layout が書く。save は書かない）:
  1. フィールド `readonly BoardFileCommands files;` を、全 Feature の `Load` が終わった後（コンストラクターの `host.RenderNow()` の前後どちらでもよいが、起動時の減色設定の反映が終わってから）で
     `files = new BoardFileCommands(this, host, settings, () => Busy);` と作る。
  2. 上の帯の ボタン 5 つ: 新規 → `files.New()`、開く → `files.Open()`、保存 → `files.Save()`、保存の ▼「名前を付けて保存」 → `files.SaveAs()`。
  3. キー（`OnKey`）: Ctrl+N / Ctrl+O / Ctrl+S / Ctrl+Shift+S → 同じメソッド（描画中は何もしない）。
  4. `FormClosing`: `if (!files.ConfirmDiscard()) { e.Cancel = true; return; }` を `SaveSettings` の前に 1 行。
  5. `OpenFile(path)`（ドロップ・exe の引数・貼り付けのファイル）の先頭で、拡張子が `.kanban`（大文字小文字を区別しない）なら `files.OpenPath(path); return;`。
  6. タイトル: `files.TitleChanged += () => { if (drawing == null) Text = files.Title; };` と、描画の終わり（`finally`）で `Text = files.Title;`。
     描画中の進捗は今まで通り `看板屋 - 描画 12%` を `Text` に出す（`AppTitle` のまま）。
  7. 描画中の無効化: `UpdateButtons` で 新規・開く・保存・名前を付けて保存を `!Busy` にする。`BoardFileCommands` の各メソッドも `isBusy()` なら何もしない（二重の守り）。
- **`BoardFileCommands` の責任**: 変更の印の管理（下の規則）、確認ダイアログ、ファイル選択ダイアログ、読み込み・書き込み・エラー表示、ログ、前回のフォルダの設定、**タイトルの文字列の計算**
  （`Title` と `TitleChanged`。`Form.Text` への代入は MainForm）。MainForm は変更の印を持たず、`files.Dirty` も見る必要はない。
- **スタブ**: layout は save の完成を待たずにビルドできるよう、`UI/BoardFileCommands.cs` に上の API と同じ**署名だけのスタブ**（本体は `host.Log("未実装")` と `Title => "看板屋"`、
  `ConfirmDiscard` は `true`）をコミットしてよい。save は同じパスのファイルを**丸ごと置き換える**。統合（`m12/integrate`）で add/add の衝突になったら**常に save の版を採る**。
  署名を変えたくなったら、どちらも変えずに報告する（メインが調整）。
- 衝突しうる共有ファイルは `EditorHost.cs`（layout = `Tips` のフィールド 1 つ、save = `ApplyQuantize` とイベント）と `QuantizeFeature.cs`（layout = `CreatePanel` の説明文、save = 購読と `ApplyOptions`）。
  どちらも別の場所への追加なので、衝突は統合で手で解く。

#### 変更の印とタイトル
- `Dirty` は `host.Doc.Changed`（元に戻す / やり直しも含む。オブジェクト・背景・フレームの変更）と `host.QuantizeChanged`（減色の設定）で真になる。
  選択・表示の切り替え・入力方式・タブは変更ではない。**元に戻して保存した状態に戻っても「*」は残る**（履歴の位置は追わない。単純さを優先）。
- 偽になるのは、保存に成功したとき、読み込みに成功したとき、「新規」のとき、起動直後（コンストラクターの最後で偽にする。起動時に減色の設定を `Load` が反映するだけで「*」が付かないように、MainForm は `files` を全 Feature の `Load` の後に作る）。
  読み込み中に起きる `Changed` / `QuantizeChanged` は `loading` の間は無視して、最後に偽にする。
- タイトル（`TitleOf(path, dirty)`）:
  | `path` | `dirty` | タイトル |
  |---|---|---|
  | なし | 偽 | `看板屋` |
  | なし | 真 | `看板屋 - (無題)*` |
  | あり | 偽 | `看板屋 - <ファイル名>` （`Path.GetFileName`、拡張子つき） |
  | あり | 真 | `看板屋 - <ファイル名>*` |
- 確認（`New` / `Open` / `OpenPath` / `ConfirmDiscard` で `Dirty` のとき）: `MessageBox.Show(owner, "「<ファイル名または (無題)>」は変更されています。保存しますか？", "看板屋", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question)`
  — はい = `Save()`（名前を付ける画面で取り消したら、または保存に失敗したら、元の操作も取り消し）、いいえ = 変更を捨てて続ける、キャンセル = 元の操作を取り消す。

#### 開く・保存の手順
- **開く**（`Open` → ファイル選択〔`OpenFileDialog`、フィルター `看板屋の看板 (*.kanban)|*.kanban`、初期フォルダ = 設定 `board.folder`〕→ `OpenPath`）:
  1. 確認（`Dirty` のとき）→ 2. ファイルを `FileStream`（読み取り、共有読み取り可）で開き `BoardFile.Read(stream, new PngCodec(), host.Text, FontResolver)`
  → 3. 例外は `BoardFileException` / `IOException` / `InvalidDataException` を捕まえ、`MessageBox`（エラーアイコン）とログに出して**何も変えずに**戻る
  → 4. 成功したら `loading = true`、`host.Doc.Load(content)`、`content.Quantize != null` なら `host.ApplyQuantize(content.Quantize)`、`loading = false`、`CurrentPath = path`、`Dirty = false`、`board.folder` を更新、
  ログ `開きました: <パス>`、`Warnings` を 1 件ずつログ → `TitleChanged`。
  `FontResolver` = `n => GdiTextRasterizer.InstalledFonts().Contains(n) ? n : GdiTextRasterizer.DefaultFont()`（呼び出しごとに一覧を取らず、1 回の読み込みの間は一覧を使い回す）。
- **新規**（`New`）: 確認 → `host.Doc.Load(BoardContent.Empty())`、減色の設定は**そのまま**、`CurrentPath = null`、`Dirty = false`、ログ `新しい看板にしました`。
- **保存**（`Save`）: `CurrentPath` が無ければ `SaveAs`。あれば書く。`SaveAs`: `SaveFileDialog`（同じフィルター、既定名 = 開いたファイル名または `看板.kanban`、`OverwritePrompt` 既定のまま）→ 書く。
  書き方: **同じフォルダの `<パス>.tmp` に全部書いてから**、既存があれば `File.Replace(tmp, path, null)`、無ければ `File.Move`（書き込みの途中で失敗しても元のファイルは壊れない）。失敗したら `.tmp` を消し、
  `MessageBox`（エラー）と `保存できませんでした: <メッセージ>` をログに出し、`Dirty` は偽にしない。成功したら `CurrentPath = path`、`Dirty = false`、`board.folder` を更新、ログ `保存しました: <パス>`。
- 減色の設定の反映（`host.ApplyQuantize`）: `EditorHost` に `public void ApplyQuantize(QuantizeOptions o)` を足す。イベント `public event Action<QuantizeOptions> QuantizeLoadRequested` に購読者がいればそれを呼び、
  いなければ `SetQuantize(o)`（減色タブを持たない構成でも動く）。`QuantizeFeature` は `CreatePanel` で購読し、`ApplyOptions(o)` を呼ぶ:
  `loading = true` の間にディザ・強さ・明るさ・コントラスト・彩度・使う色のチェックを全部 `o` の値に合わせ（スライダーは `Value` 設定、`ResetSlider.ValueChanged` が起きても `loading` で無視）、
  `loading = false` にして `Apply()` を 1 回呼ぶ（`host.SetQuantize` → `QuantizeChanged`）。
  下書き表示中なら `MainForm` の `QuantizeChanged` の購読が下書きを作り直す。`QuantizeFeature.Load`（設定 `settings.txt` 由来）と同じ経路なので、開いた後の設定は終了時に `settings.txt` にも保存される
  （開いた看板の減色が次回の既定になる。よくある使い方では望ましいと判断。ファイルごとの設定をアプリの既定にしたくなければ、将来 `ApplyOptions` で保存対象から外す）。

## CLI（`--board`）
`Program.Cli`（[ui.md](ui.md) の CLI の表）:
```
KanbanYeah.exe --board file.kanban [--no-draw] [--input post|send] [--delay ms] [--click-delay ms] [--fence n] [--retouch n]
                 [--countdown s] [--save-quantized out.png] [--log file] [--debug-shot out.png]
```
- 保存した看板を読み、合成（`Compositor.Build`、`GdiTextRasterizer` の `Smooth = false` = ペンの文字）→ **ファイルの減色の設定で**減色 → ストローク計画 → 描画（`--draw 画像` の看板版。E2E 用）。
  `--fit` `--dither` `--strength` `--bg` は効かない（ファイルの設定を使う。`--register-frame` の減色オプションと同じ扱い）。
- `--no-draw`: 計画までで終わる（描かない。`--save-quantized` で書き出した PNG が期待画像になる）。終了コード 0。
- 読み込みの失敗（ファイルが無い・壊れている・新しい版）は上の表のメッセージを `error: <メッセージ>` でログに出して**終了コード 3**（他の「エラー / 見つからない」と同じ）。
  フォントが無い警告は `warning: ...` として出し、続ける。
- 終了時に公式文字の補助プロセス（`OfficialText.Shutdown()`）を止める。
- 画面の起動時に引数 1 つが `.kanban` ファイルなら、画面で開く（`MainForm.OpenFile` の分岐。`Program.Main` の判定は今まで通り「`--` で始まらず存在するファイル 1 つ」）。

## 設定（`UI/Settings`）
- `board.folder`: 開く / 保存のダイアログの前回のフォルダ（開いた / 保存したファイルのフォルダ）。既定は空 = マイドキュメント。存在しなければ無視。
- 開いているファイルは設定に覚えない（起動すると空の看板から）。

## 不変条件とテスト
単体テスト `BoardFileTests`（`tests/KanbanDrawer.Tests`、CI で実行。`IImageCodec` の代役 `RawCodec` = 幅・高さ・生の画素を詰める。PNG の符号を持たないので、
画像の検証は「PNG の先頭」を作る小さなヘルパーでも確かめる）:
- **往復で合成が同じ**: 背景・フレーム・画像・スタンプ・文字（縁取りあり、複数行、`=` `\` を含む文字列）・回転・拡縮を持つ看板を `Write` → `Read` → `BoardDoc.Load` し、
  `Compositor.Build` の `Composite`（RGB と固定画素）が元と一致する。オブジェクトの順序・種類・属性、`FrameId`、`StampObject.Id`、`PictureObject.FileName` が一致。
  減色の設定（ディザ 4 種・強さ・明るさ等・使う色）が一致。空の看板の往復。
- **ライブラリに無いスタンプ**・**ライブラリに無いフレーム id**: ライブラリを使わずに `Read` でき、合成が一致する。
- **新しい版**（`kanban-board=2`）: `BoardFileException`（`Newer`）。他のキーが壊れていても `Newer` が優先。**古すぎる / 版数が整数でない**: `Corrupt`。
- **壊れたファイル**（それぞれ `Corrupt`、`BoardDoc` は無変更）: ZIP でない・`board.txt` が無い・1 行目が違う・必須キー欠落・`objects` と実数の不一致・色が範囲外・`scale` が 0 / NaN・
  画像エントリ欠落・PNG の符号なし・幅が 0 / 範囲外・フレームが 320x240 でない・オブジェクト数 1000 超・巨大な宣言サイズ。
- **知らないキー・知らない ZIP エントリは無視**して読める。
- **フォント**: `fontResolver` が別の名前を返す文字は置き換わり、警告が 1 回。公式文字・同じ名前は警告なし。
- `BoardDoc.Load`: 履歴（`CanUndo` / `CanRedo`）が空、選択なし、`Changed` が 1 回。
- `PngCodec`（Win、CI 可）: アルファ付きの画素の往復（アルファ 0 以外の画素が一致）。
- `BoardFileCommands.TitleOf` の 4 通り。画面の確認・保存先・変更の印は単体テストにしない（開発環境の画面確認 `tools/gui-e2e.sh` で、保存 → 開くを通す）。
- CLI の `--board` は開発環境の E2E（保存した看板をモック看板に描いて `pixels` と期待画像を比べる）。

## 落とし穴・経緯
- 初めは本体を JSON にする案だったが、標準で使える JSON の部品が .NET Framework 4.8 に無く（`DataContractJsonSerializer` は型が膨らむ）、設定ファイルと同じ `key=value` にした。
- ZIP を `Core` に置いてよいと判断した理由: 依存先が System.Drawing でも WinForms でも Win32 でもない。逆に `Core` から PNG を作るには System.Drawing が要るので、そこだけ `IImageCodec` にした。
- 保存の中身にライブラリの id だけを入れる案（小さい）は、ライブラリから消すと開けなくなるので採らず、画素を入れた（1 つの看板は数 MB 以下。PNG なので小さい）。

## 未テスト
- 実際の巨大な看板（画像 1280px を数十枚）の保存・読み込みの時間。
- 他人の PC（フォント違い）で開いたときの見た目。
