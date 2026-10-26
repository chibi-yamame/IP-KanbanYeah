# 設計書

領域ごとに 1 ファイル。共通の節: 目的 / 入口 / クラス / 原作側の根拠 / 不変条件と安全規則（守らせているテスト）/
落とし穴・経緯 / 未テスト。

| 設計書 | 範囲 | 読むとき |
|---|---|---|
| [features.md](features.md) | 編集機能: Feature 構成（`IFeature` / `EditorHost` / `FeatureRegistry`）、層（下地 + オブジェクト〔画像・スタンプ・文字〕）、固定画素つき減色、ライブラリ、`EditorView` | タブを足す・消す、スタンプ / 文字 / フレームを変える |
| [image.md](image.md) | 画像の読み込み・配置（320x240）・調整・減色・ディザ | 減色の見た目や速度を変える |
| [strokes.md](strokes.md) | ストローク計画: CLR 色の選択、色順の貪欲法、重ね塗りによるラン延長、縦横の選択 | 描く本数を減らす・順序を変える |
| [locate.md](locate.md) | 看板の自動検出、ボタン位置（原作定数）、押下状態の判定 | 検出が失敗する・原作の配置が違う |
| [drawing.md](drawing.md) | 描画セッション、入力方式（PostMessage / SendInput）、フェンス、読み戻し検証、中断 | 描画の取りこぼし・速度・安全装置 |
| [ui.md](ui.md) | 画面構成、設定保存、進捗表示、CLI | 画面や CLI を変える |

その他: [../SPEC.md](../SPEC.md)（仕様）、[../../README.md](../../README.md)（使い方・ビルド）。
各節の「不変条件とテスト」に出てくる単体テスト（`…Tests`）は [../../tests/KanbanDrawer.Tests/](../../tests/KanbanDrawer.Tests/)
（CI で実行）。ゲーム画面のスクリーンショットを使う検出テスト・モック看板の E2E などの検証ツール（`tools/…`）・計画や進捗の記録は
開発環境側で管理しており、このリポジトリには含めない。

## 全体の流れ
```
各タブ（Feature）─▶ BoardDoc（下地 + 画像・スタンプ・文字）  ─ Compositor ─▶ Composite（RGB + 固定画素）
                                                   ─ Quantizer ─▶ IndexedImage ─ StrokePlanner ─▶ DrawPlan
                                                                                                │
画面 ─ ScreenCapture ─ BoardLocator ─▶ キャンバス原点 ──────────────────────────────▶ DrawSession ◀┘
                                                                                     │  ▲
                                                InputDriver (PostMessage / SendInput) ┘  └ 読み戻し（ScreenCapture）
```
`Core/` は Win32 / WinForms に依存しない（`IBoardIO` 越しに入出力）→ `tests/` の `FakeEditor` で全体を単体テストできる。
