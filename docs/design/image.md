# 画像・減色

## 目的
任意の画像を看板の 320x240・10 色に変換する。見た目と「描く本数（＝時間）」のバランスをユーザーが選べるようにする。

## 入口
- `Win/ImageLoader.Load` → `ImageLoader.Fit(bitmap, FitMode)` → `Core/Quantizer.Quantize(raster, QuantizeOptions)`
- UI: `MainForm.Convert`（設定変更から 150ms デバウンス、減色と計画はバックグラウンド）。CLI: `--quantize` / `--draw`

## クラス
| クラス | 役割 |
|---|---|
| `Core/Palette` | 10 色（原作の値そのもの、黒は `#060606`）、列・段 |
| `Core/ColorSpace` | sRGB → CIELAB (D65) |
| `Core/ImageFit` | 配置計算（Contain / Cover / Stretch）: 元画像の矩形 → 看板上の矩形 |
| `Win/ImageLoader` | GDI+ でデコード・高品質縮小（HighQualityBicubic、端は TileFlipXY）・`Raster` 変換 |
| `Core/Quantizer` | 透明の合成 → 明るさ/コントラスト/彩度 → 最近色（ΔE76, Lab）→ ディザ |
| `Core/Raster`, `Core/IndexedImage` | ARGB 画素 / パレット番号の画像 |

## 減色の詳細
- 最近色は CIELAB のユークリッド距離（ΔE76）。使わない色（`Enabled[i] = false`）は候補から外す。
- ディザ: なし / Floyd–Steinberg（蛇行走査）/ Atkinson（誤差 6/8 のみ拡散、明るめでコントラスト高）/ Bayer 4x4
  （閾値 ±48 × 強さ を RGB に加算）。誤差拡散は sRGB 空間で行い、強さ（0–1）を誤差に掛ける。
- 透明部分は「余白・透明色」（パレットのどれか）に合成。Contain の余白も透明 → 同じ色になる。

## 描く本数への影響（スクショ画像 608x469 → Contain、実測）
| ディザ | ストローク数 | 目安 (PostMessage) |
|---|---|---|
| なし | 5,387 | 約 15 秒 |
| Floyd–Steinberg | 20,227 | 約 45 秒 |
| Atkinson | 14,830 | 約 35 秒 |
| Bayer | 15,691 | 約 37 秒 |

イラスト・ロゴ・文字は「なし」がきれいで速い。写真は誤差拡散。薄い背景（クリーム色など）に誤差拡散を掛けると点が散るので、
強さを下げるか「なし」にする。

## 不変条件とテスト
- パレット色そのものの画像は（なし / FS / Atkinson で）そのままの番号になる: `QuantizerTests.PaletteColoursMapToThemselves`
- 使わない色は出ない: `DisabledColoursAreNeverUsed`。透明は背景色: `TransparentPixelsTakeTheBackground`
- 配置計算: `ImageFitTests`。パレット値: `PaletteTests`

## 落とし穴
- GDI+ の `DrawImage` は既定で端が暗くにじむ → `ImageAttributes.SetWrapMode(TileFlipXY)`。
- `Image.FromFile` はファイルをロックする → `new Bitmap(img)` でコピーしてすぐ閉じる。

## 未テスト
- GDI+ 縮小の画質（目視のみ）。
