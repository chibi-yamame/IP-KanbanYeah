# 看板屋（InterPot 看板お絵かきツール）

[![CI](https://github.com/chibi-yamame/IP-KanbanYeah/actions/workflows/ci.yml/badge.svg)](https://github.com/chibi-yamame/IP-KanbanYeah/actions/workflows/ci.yml)

<img width="64" height="64" alt="KanbanYeah_icon512" src="https://github.com/user-attachments/assets/8009e881-51c3-4c1e-b0bb-d86bcc496530" />

Windows 版 InterPot3 の看板エディタに、好きな絵を自動で描くツールです。

- `KanbanYeah.exe` 1 つで動きます（Windows 10 / 11、追加インストール不要、管理者権限不要）。
- 画像・スタンプ・文字・フレームを組み合わせて看板を作り、看板の 10 色・1px ペンで描きます。描いたあとの「保存」はご自身で押してください。
- 描画中もマウスは自由に使えます（既定の「バックグラウンド」方式）。

<img width="742" height="628" alt="image" src="https://github.com/user-attachments/assets/cbf093e5-fe57-479b-8f81-7a296252b129" />

## 入手
[Releases](https://github.com/chibi-yamame/IP-KanbanYeah/releases) から `KanbanYeah-<版>.zip` をダウンロードして展開します。
署名のない exe なので、初回に Windows の「PC を保護しました」が出たら「詳細情報」→「実行」で起動できます。

## 使い方
1. `KanbanYeah.exe` を起動し、右のタブで看板を作ります（左はフルカラーで表示）。**「下書き」**を押すと、実際に描く 10 色の絵と
   本数・目安時間が出ます（「カラー / 下書き」で表示を切り替え）。
   - **下地**: 背景色（10 色）と、登録したフレームを別々に選びます。フレームは登録時に看板サイズにして保存し、色は
     下書きのときに画像・スタンプと一緒に 10 色にします（明るさ・ディザも効きます）。「背景を消す」をオンにして登録すると
     縁と同じ色の背景を透明にできます。フレームの透明な部分には背景色が見えます
   - **画像**: 好きな画像を看板に置く（ボタン、またはウィンドウへのドラッグ&ドロップ）。置き方（全体を収める / トリミングして
     埋める / 等倍）を選べ、置いたあとは移動・拡大縮小・回転が自由。何枚でも置けます（登録は不要）
   - **スタンプ**: 登録した画像を等倍（100%）で置く。登録は 320×240 までの画像で、複数まとめて選べます。看板の上でドラッグで移動、
     角で拡大縮小、上の丸で回転（Shift で 15° ずつ）
   - **文字**: 「公式と同じ（ドット文字）」なら InterPot の文字ツールと同じ字形（10〜22px）、「フォントを選ぶ」なら好きなフォント。
     大きさ・色・縁取りを選んで「文字を追加」。文字は 10 色のまま描くのでにじみません
   - **減色**: 下書きの作り方。ディザ（イラスト・文字は「なし」、写真は「誤差拡散」がおすすめ）、明るさなど。つまみの上の ▼ で元の値に戻ります
2. InterPot3 で自分の看板の編集画面（表紙 / 新しいページ）を開く。看板とパレットが隠れないようにします。
3. 「描画開始」（下書きが古ければ自動で作り直します）。2 秒後に始まります。**Esc でいつでも中止**できます。
4. 完了したら看板を確認して、InterPot の「保存」を押します。

途中で止めたときや、違う画素が残ったときは「続きから / 手直し」で、看板の今の状態から違うところだけ描き直します。
ブラウザやペイントでコピーした画像、コピーしたテキストは、Ctrl+V（または左の「貼り付け」ボタン）で看板に置けます。
看板の上でクリックすると置いたものを選べます。矢印キーで 1px 移動、Delete で削除、Ctrl+Z で元に戻します。

登録したスタンプ・フレームの削除: 一覧で Ctrl+クリック（Shift+クリックで範囲、Ctrl+A ですべて）して選び、Delete キー、
右クリックの「削除」、または「選んだものを削除」を押します。確認は出ませんが、「削除を元に戻す」で直前の 1 回分は戻せます。

## 注意
- 描画中は、ゲームの看板を他のウィンドウで隠したり、ゲームをクリックしたりしないでください（中断します）。
- 「ゲームの画面が描画に追いついていません」で止まったら、ゲームを最小化して元に戻す（またはゲームを再起動する）と直ります。
- うまく描けない場合は、入力方式を「マウス操作（互換）」にしてください（遅くなり、描画中はマウスに触れません）。
- 登録したスタンプ・フレームと設定は `%APPDATA%\KanbanDrawer` に保存されます。看板の構成（置いたもの）は終了すると消えます。
- 前の版で登録したフレーム（10 色で保存されたもの）もそのまま使えます。透明にしたいときは「背景を消す」で登録し直してください。

## ビルド
- 必要なもの: .NET SDK（6 以降）。ターゲットは .NET Framework 4.8（参照アセンブリは NuGet から取得）。
  ```
  dotnet build src/KanbanDrawer -c Release      # → src/KanbanDrawer/bin/Release/net48/KanbanYeah.exe（単一ファイル）
  ```
- 「公式と同じ」文字の描画プログラム（`src/KanbanDrawer/Java/KanbanText.java`）を変えたときだけ、JDK で jar を作り直します
  （Java 8 で動くように `--release 8`）:
  ```
  javac --release 8 -encoding UTF-8 -d out src/KanbanDrawer/Java/*.java
  jar cf src/KanbanDrawer/Resources/kanban-text.jar -C out .
  ```
- 単体テスト: `dotnet test tests/KanbanDrawer.Tests`。「公式と同じ」文字を Java の描画と突き合わせるテストは、
  .jar を開く Java がない環境（CI など）では何もせずに通ります。
- CI（GitHub Actions）: `main` への push と pull request でビルドと単体テスト（`.github/workflows/ci.yml`）。
- リリース: `v0.3.0` のようなタグを push すると、その版数でビルドして Releases に zip（exe・README・LICENSE）と SHA-256 を載せます
  （`.github/workflows/release.yml`。Actions の画面から手動実行すると、リリースは作らずに zip だけ作ります）。

## ドキュメント
- [docs/SPEC.md](docs/SPEC.md) — 仕様
- [docs/design/](docs/design/README.md) — 設計（領域ごと）

## ライセンス
[MIT License](LICENSE)（Copyright (c) 2026 chibi-yamame）。無保証です。

非公式のファンメイドツールです。InterPot の運営とは関係ありません。
