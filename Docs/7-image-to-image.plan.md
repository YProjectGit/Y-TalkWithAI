# 7.ImageToImage 実装プラン

## 要点（サマリー）

- **何をするか**: **元画像＋短い指示**を送り、変換後の絵を受け取る。6 の出口（画像バイト）はそのまま、入口に画像を足す。
- **パイプライン**: 参照画像 ＋ テキスト → REST `generateContent`（`IMAGE`）→ After 表示。通信は 6 と同じ。
- **入力源**: **同梱のサンプル画像1枚**（`Resource/`）。WebCam・描画パッド・ファイル選択は使わない。
- **UX**: 左に Before / After。指示を書いて変換。再変換で After を上書き。
- **コピー派生**: 共通基底は作らない。`TextToImage.cs` をコピーし、`contents.parts` に `inlineData` を足す。
- **触らないもの**: 1A〜6 の挙動。6 は実装済み。本プランの実装は 6 のあと。
- **完了条件**: シーン・スクリプト・README・本プランの UI イメージ。クラウドのため Editor 検証は省略。
- **一言**: 6 が「言葉から絵」なら、7 は「この絵を、こう変えて」。
- **適用状況**: プランのみ。**実装は 6 の完了後**。

| | 6.TextToImage | 7.ImageToImage（本プラン） |
|---|---------------|---------------------------|
| 通信 | REST `generateContent` ×1 | **同じ** |
| 入力 | テキストだけ | **サンプル画像 ＋ 指示テキスト** |
| 出力 | 生成画像 | **変換後画像** |
| 左ペイン | 結果1枚 | **Before / After** |

---

## 学習上の位置づけ

```text
[6]  Text ──────────────► Image Gen ────────────────► Image
[7]  Image ＋ Text ─────► Image Edit ───────────────► Image
```

- 6 で「`IMAGE` が返る」を見たあと、7 は **送る側にも画像を載せる**。
- 学生が追う山場は、Request の `parts` に `text` と `inlineData`（元画像）が並ぶこと。
- 入力の取り方（カメラ／描画／ファイルダイアログ）は、このデモの学習点ではない。

---

## 入力源の判断

概要 README にあった候補と、採用しない理由。

| 候補 | 採用 | 理由 |
|------|------|------|
| **用意済みテクスチャ** | **採用** | 学習点が「画像を parts に載せる」に絞れる。カメラ権限も描画実装も不要 |
| WebCam 1フレーム | しない | 4 で済んでいる。権限・デバイス差がノイズになる |
| 描画パッド | しない | 5 で済んでいる。7 の山場がぼける |
| OS のファイル選択 | しない | Editor / ビルド差が大きく、教材が長くなる |

サンプルは **1枚固定**（切替 UI は置かない）。猫や簡単なイラストなど、変換の差が見えるもの。

---

## 処理の骨格

```text
Play
  → APIキー読込
  → Resource のサンプルを Before に出す

[変換]
  → サンプルを JPEG/PNG バイト化 → Base64
  → contents.parts = [ { text: 指示 }, { inlineData: 元画像 } ]
  → generationConfig.responseModalities = ["TEXT", "IMAGE"]
  → 6 と同じ POST
  → 返ってきた画像を After に出す
```

モデル・認証・エンドポイントは 6 と同じ（既定 `gemini-3.1-flash-image`）。

---

## UX 詳細（判断の固定）

| 操作 | 動作 |
|------|------|
| 変換 / Enter | いまの指示で1回変換。待ち中は再送信不可 |
| 再変換 | After を上書き。Before はサンプルのまま |
| サンプル変更 | **しない**（インスペクタで差し替え可） |
| 履歴 | **送らない**（毎回「この1枚＋いまの指示」） |

---

## UI 構成

6 の3分割を踏襲し、左だけ Before / After にする。

```text
┌──────────────────────────────────────────────────────────────────────────┐
│  7.ImageToImage                                                          │
├────────────────┬────────────────────────────┬────────────────────────────┤
│ Left           │ Center  Request            │ Right  Response            │
│ Before | After │ POST / マスク済みキー      │ HTTP / mime / バイト数     │
│ [指示テキスト] │ parts: text + inlineData   │ 短縮した JSON              │
│ [変換] Status  │ responseModalities         │                            │
└────────────────┴────────────────────────────┴────────────────────────────┘
```

Request で、元画像の Base64 は 6/3A と同じく **表示だけ短縮**する（「画像が載っている」ことが分かればよい）。

構成イメージ: [`7-image-to-image-ui.png`](7-image-to-image-ui.png)

---

## 設計の骨格（コード）

### ファイル

| パス | 由来 |
|------|------|
| `Assets/7.ImageToImage/ImageToImage.unity` | 新規（6 の3分割を流用） |
| `Assets/7.ImageToImage/Script/ImageToImage.cs` | 6 をコピーし、入力画像 part を足す |
| `Assets/7.ImageToImage/Resource/` | サンプル画像1枚（実装時に追加） |
| `Assets/7.ImageToImage/README.md` | WorkshopMaterial 準拠で本文化（実装時） |

### 6 から流用／足すもの

| 流用 | 足す |
|------|------|
| POST、`responseModalities`、`inlineData` 抽出、Base64 短縮、3分割 UI | Before 表示、リクエストへの画像 part、After への書き込み |

---

## README（実装時）

- **学べること**: 画像変換／参照画像／Before と After
- **動かし方**: サンプルを見る → 指示を送る → After と Request の `inlineData` を見比べる
- **概念節**: 参照画像とは？、同じ `generateContent` に画像を載せる、Before / After
- **主要クラス**: `ImageToImage` のみ

---

## 実装タスク順（6 のあと）

1. 6 のスクリプト／シーンを 7 へコピー
2. サンプル画像を `Resource/` に置き、Before に出す
3. リクエスト `parts` に画像を載せる
4. After 表示と Request の短縮表示
5. README・overview 更新

---

## 判断の固定

- **6 と同じ REST**（Live には戻らない）
- 入力は **同梱サンプル1枚**（カメラ／描画／ファイル選択はしない）
- **Before / After** を左に並べる
- 履歴なし、単発
- 共通基底は作らない
- **実装は 6 が動いてから**

プラン段階。クラウド環境のため Editor 検証は対象外。
