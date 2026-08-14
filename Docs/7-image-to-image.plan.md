# 7.ImageToImage 実装プラン

## 要点（サマリー）

- **何をするか**: **カメラに映っている1フレーム＋短い指示**を送り、変換後の絵を受け取る。6 の出口（画像バイト）はそのまま、入口に WebCam 画像を足す。
- **パイプライン**: WebCam JPEG ＋ テキスト → REST `generateContent`（`IMAGE`）→ After 表示。通信は 6 と同じ。**Live API は使わない**（画像出力非対応）。
- **入力源**: **WebCam の今の1フレーム**（変換ボタンでシャッター）。連続送信・同梱サンプル・描画パッド・ファイル選択は使わない。
- **UX**: 左にライブ映像 / After。指示を書いて変換。応答は数秒かかる。待ち中は再送信不可。プレビューは動き続ける。
- **コピー派生**: 共通基底は作らない。`TextToImage.cs` の POST／3分割と、`VisionToSpeech.cs` の WebCam／JPEG 化をコピーする。
- **触らないもの**: 1A〜6 の挙動。ローカル推論エンジン。4/5 の Live セッション。
- **完了条件**: シーン・スクリプト・README・本プランの UI イメージ。クラウドのため Editor 検証は省略。
- **一言**: 4 が「カメラを見て声で返す」なら、7 は「カメラを見て絵で返す」。往復は REST 1回。
- **適用状況**: プランのみ。**実装は 6 の完了後**（6 は実装済み）。

| | 4.VisionToSpeech | 6.TextToImage | 7.ImageToImage（本プラン） |
|---|---|---|---|
| 通信 | Live API（WebSocket） | REST `generateContent` | **6 と同じ REST** |
| 入力 | WebCam JPEG | テキストだけ | **WebCam JPEG ＋ 指示** |
| 出力 | 声 | 生成画像 | **変換後画像** |
| 送り方 | Space / 約1 FPS | 送信1回 | **変換1回（シャッター）** |

---

## 学習上の位置づけ

```text
[4]  Camera ═════════► Live API ══════════════════► Audio
[6]  Text ──────────────► Image Gen ────────────────► Image
[7]  Camera ＋ Text ────► Image Edit ───────────────► Image
```

- 6 で「`IMAGE` が返る」を見たあと、7 は **送る側にも画像を載せる**。
- 4 と同じ WebCam だが、プロトコルも出口も違う（Live→声 ではなく REST→絵）。
- 学生が追う山場は、Request の `parts` に `text` と `inlineData`（今のカメラ）が並ぶこと。
- カメラの起動自体は 4 で済んでいる。7 の新しさは **参照画像として REST に載せる**こと。

---

## 入力源の判断

前プランは同梱サンプルだった。カメラ映像を画像化する、に切り替える。

| 候補 | 採用 | 理由 |
|------|------|------|
| **WebCam 1フレーム** | **採用** | 「映っているものを絵にする」体験。4 の JPEG 化を流用できる |
| 連続変換（毎秒） | しない | 画像生成は1枚 数秒。Live も画像を返さない。キューと回数だけが増える |
| 用意済みテクスチャ | しない | カメラ映像を画像化する目的とずれる |
| 描画パッド | しない | 5 で済んでいる |
| OS のファイル選択 | しない | Editor / ビルド差が大きく、教材が長くなる |
| ローカル推論 | しない | このシリーズの範囲外 |

リアルタイムに見えるのは **左のプレビューだけ**（端末ローカル）。After はボタン1回・数秒待ち。

---

## 処理の骨格

```text
Play
  → APIキー読込
  → WebCam 起動（左の Camera にライブ表示）

[変換] / Enter
  → いまのフレームを長辺768前後へ縮小 → JPEG → Base64
  → contents.parts = [ { text: 指示 }, { inlineData: カメラJPEG } ]
  → generationConfig.responseModalities = ["TEXT", "IMAGE"]
  → 6 と同じ POST
  → 返ってきた画像を After に出す
```

モデル・認証・エンドポイントは 6 と同じ（既定 `gemini-3.1-flash-image`）。  
`flash-lite-image` は参照画像が不得意なので、7 の既定にはしない。

---

## UX 詳細（判断の固定）

| 操作 | 動作 |
|------|------|
| 変換 / Enter | いまのカメラ1フレーム＋指示で1回変換。待ち中は再送信不可 |
| 再変換 | いまのフレームを撮り直して送る。After を上書き |
| Space | **使わない**（4 の Live シャッターと混ぜない。入力欄とも衝突する） |
| 連続送信トグル | **置かない** |
| 履歴 | **送らない**（毎回「この1枚＋いまの指示」） |
| プレビュー | 待ち中もライブのまま。照準用。送信バイトだけ縮小 |

待ちの目安は **4〜12秒**（1K）。Status は 6 と同じ点滅。画面に「リアルタイム」とは書かない。

初期の指示文（入力欄の初期値、編集可）:

```text
この写真を、はっきりしたイラストにしてください
```

カメラが無い／権限が無いときは 4 と同様にエラーを出し、送信しない。

---

## UI 構成

6 の3分割を踏襲し、左だけ Camera / After にする。

```text
┌──────────────────────────────────────────────────────────────────────────┐
│  7.ImageToImage                                                          │
├────────────────┬────────────────────────────┬────────────────────────────┤
│ Left           │ Center  Request            │ Right  Response            │
│ Camera | After │ POST / マスク済みキー      │ HTTP / mime / バイト数     │
│ [指示テキスト] │ parts: text + inlineData   │ 短縮した JSON              │
│ [変換] Status  │ responseModalities         │                            │
└────────────────┴────────────────────────────┴────────────────────────────┘
```

- **Camera** … 常時ライブ。送信した静止画で止めない（次の照準ができないため）
- **After** … 直近の変換結果。未送信時は案内文
- Request の元画像 Base64 は 6/3A と同じく **表示だけ短縮**する

構成イメージ: [`7-image-to-image-ui.png`](7-image-to-image-ui.png)

---

## 設計の骨格（コード）

### ファイル

| パス | 由来 |
|------|------|
| `Assets/7.ImageToImage/ImageToImage.unity` | 新規（6 の3分割を流用） |
| `Assets/7.ImageToImage/Script/ImageToImage.cs` | 6 をコピーし、WebCam 取得と画像 part を足す |
| `Assets/7.ImageToImage/README.md` | WorkshopMaterial 準拠で本文化（実装時） |

`Resource/` は作らない（同梱サンプルを置かない）。Prefab も、実ファイルが必要になるまで作らない。

### `ImageToImage.cs` の流れ（読む順）

1. `Start` — キー、UI、**WebCam 起動**
2. `OnConvertClicked` → フレーム JPEG 化 → `StartCoroutine(SendImageCoroutine)`
3. `BuildRequestJson` — `text` ＋ `inlineData`（JPEG）＋ `responseModalities`
4. `UnityWebRequest` POST — 6 と同じ待ち方
5. `TryExtractInlineImage` → After の `RawImage`
6. `OnDestroy` — WebCam 停止

### 6 / 4 から流用／捨てるもの

| 流用 | 捨てる／置き換える |
|------|-------------------|
| 6 の POST、`responseModalities`、抽出、Base64 短縮、3分割 | テキストだけ contents → **カメラ JPEG を parts に足す** |
| 4 の `WebCamTexture`、長辺制限、`EncodeToJPG` | Live / Space / Stream / 音声再生 |

---

## README（実装時）

- **学べること**: 画像変換／参照画像（カメラ1フレーム）／同じ `generateContent` に画像を載せる
- **事前準備**: APIキー。カメラがつながっていること（4 と同じ）
- **動かし方**: プレビューを見る → 指示を送る → After と Request の `inlineData` を見比べる
- **概念節**: 参照画像とは？、シャッター1枚と連続変換の違い（後者はやらない）、Before / After
- **主要クラス**: `ImageToImage` のみ

書かないもの: 改変ヒント、つまずき集、次デモ誘導、Status の点滅、ローカルエンジン。

---

## 実装タスク順

1. 6 のスクリプト／3分割 UI を 7 へコピー
2. 4 から WebCam 起動と JPEG 化をコピーし、左 Camera にライブを出す
3. リクエスト `parts` に指示＋JPEG を載せる
4. After 表示と Request の短縮表示
5. カメラなし時のエラー
6. README・overview の「実装済み」更新

---

## リスク・注意

| 点 | 扱い |
|----|------|
| 1枚 数秒 | 仕様。連続変換は置かない。Status で待つ |
| 無料枠 | 画像モデルは Lite テキストより少ない。429 は pricing 案内へ |
| カメラ権限 | 4 と同じ。無いときは送らない |
| 巨大な Base64 | 送信は全文。画面表示だけ短縮 |
| 縦横比 | 既定は 6 と同じ 1:1 / 1K。カメラは 16:9 が多い。インスペクタで変更可 |
| セキュリティ | 教材は APIキー直結。README で ephemeral token に一言 |

---

## 判断の固定

- **6 と同じ REST**（Live には戻らない。画像出力が無い）
- 入力は **WebCam のシャッター1枚**（連続変換・サンプル・描画・ファイル選択・ローカル推論はしない）
- 左は **ライブ Camera / After**
- 履歴なし、単発。Space なし
- 共通基底は作らない
- **実装は 6 が動いてから**（6 は実装済み）

プラン段階。クラウド環境のため Editor 検証は対象外。
