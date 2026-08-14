# 6.TextToImage 実装プラン

## 要点（サマリー）

- **何をするか**: テキスト（プロンプト）を送り、**絵を1枚受け取る**。音声・Live・会話履歴は使わない。
- **パイプライン**: テキスト → **REST `generateContent`（`responseModalities: IMAGE`）** → `Texture2D` 表示。1A と同じ HTTP、3A の TTS と同じ「バイト列を返す」形。
- **UX**: プロンプトを書いて送信。左に生成画像、中央に Request、右に Response（Base64 は短縮）。再送信で上書き。
- **UI**: 1A 型の **3分割**。左はチャットではなく **画像プレビュー＋入力**。吹き出し・System Instruction 欄は置かない。
- **コピー派生**: 共通基底は作らない。`TextToText.cs` の POST／可視化と、`SpeechToSpeech.cs` の `inlineData` 取り出しをコピーする。
- **触らないもの**: 1A〜5 の挙動。7 は作らない（次タスク）。
- **完了条件**: シーン・スクリプト・README・本プランの UI イメージ。クラウドのため Editor 検証は省略。
- **一言**: 1A が「文を返す」なら、6 は同じ手紙で **絵を返す**。
- **適用状況**: 実装済み（シーン・スクリプト・README）。クラウドのため Editor 検証は省略。

| | 1A.TextToText | 3A の TTS 段 | 6.TextToImage（本プラン） |
|---|---------------|--------------|---------------------------|
| 通信 | REST `generateContent` | 同じ | **同じ** |
| 入力 | テキスト | テキスト | **テキスト（プロンプト）** |
| 出力 | `parts[].text` | `inlineData`（音声） | **`inlineData`（画像）** |
| 可視化 | Request / Response 全文 | Base64 は短縮 | **3A と同型（画像は左に表示）** |

---

## 学習上の位置づけ

```text
[1A] Text ──────────────► LLM ──────────────────────► Text
[3A] … ──► TTS ─────────► Audio                      … バイト列の出口（声）
[6]  Text ──────────────► Image Gen ────────────────► Image   … バイト列の出口（絵）
[7]  Image ＋ Text ─────► Image Edit ───────────────► Image   … 次（入力に画像を足す）
```

- 4/5 は Live の映像→声。6 は **REST に戻る**。新しいプロトコルは増やさない。
- 学生が追う山場は **`generationConfig.responseModalities` に `IMAGE` を入れる**ことと、返ってきた Base64 を `Texture2D.LoadImage` すること。
- 会話の続きで絵を直すのは 7。6 は **1回の送信で1枚**。

---

## 処理の骨格

```text
Play
  → APIキー読込
  → 左に空のプレビュー（「まだ画像がありません」）

[送信] / Enter
  → プロンプトを contents に載せる
  → generationConfig.responseModalities = ["TEXT", "IMAGE"]
  → UnityWebRequest で POST（1A と同じ URL 形）
  → 応答 JSON から inlineData を取り出す（3A の TTS と同じ）
  → Base64 → byte[] → Texture2D → RawImage
  → テキスト part があれば画像の下に1行出す
```

公式の入出力前提（実装時に [画像生成ドキュメント](https://ai.google.dev/gemini-api/docs/generate-content/image-generation) で再確認）:

| | 形式 |
|---|------|
| 通信 | REST `generateContent`（1A と同じホスト。パスは `v1beta` を先に試し、だめなら公式例の `v1`） |
| モデル既定 | `gemini-3.1-flash-image`（Nano Banana 2。インスペクタで変更可） |
| 応答 | `candidates[0].content.parts` に `text` と `inlineData`（PNG/JPEG の Base64） |
| 認証 | `x-goog-api-key`（`Assets/Common/APIKey.txt`） |

安く速くしたいときの候補: `gemini-3.1-flash-lite-image`（参照画像や連続編集は不得意。6 の単発生成なら足りることが多い）。

---

## UX 詳細（判断の固定）

| 操作 | 動作 |
|------|------|
| 送信 / Enter | いまのプロンプトで1枚生成。応答待ち中は再送信不可 |
| 再送信 | 左の画像を上書き（履歴は残さない） |
| アスペクト比・解像度 | **インスペクタのみ**（既定 1:1 / 1K）。画面にドロップダウンは置かない |
| 会話履歴 | **送らない**（毎回単発。続きの編集は 7） |
| System Instruction 欄 | **置かない** |
| 保存 | ディスクに書き出さない（メモリ上の `Texture2D` だけ） |

---

## UI 構成

1A の3分割を踏襲し、左だけ画像向けに差し替える。

```text
┌──────────────────────────────────────────────────────────────────────────┐
│  6.TextToImage                                                           │
├────────────────┬────────────────────────────┬────────────────────────────┤
│ Left 結果      │ Center  Request            │ Right  Response            │
│ 生成画像       │ POST URL / マスク済みキー  │ HTTP 番号                  │
│ （空なら案内） │ generationConfig           │ mime / バイト数            │
│ 短いキャプション│ responseModalities         │ 短縮した JSON              │
│ [プロンプト]   │ contents（プロンプト）     │                            │
│ [送信] Status  │                            │                            │
└────────────────┴────────────────────────────┴────────────────────────────┘
```

画面に出すもの:

1. **生成画像** … 体験の本体。`RawImage`
2. **プロンプト＋送信** … 1A と同じ入力（Enter 送信、Shift+Enter 改行）
3. **Request** … `responseModalities` が見えること（学習の核）
4. **Response** … 3A と同様、長い Base64 は先頭だけ。先頭に mime / バイト数
5. **Status** … 待機中 / 送信中 / 応答待ち / 完了 / エラー

画面に出さないもの:

- 吹き出し履歴、System Instruction 欄、段階バー、Live の送信／受信ログ

構成イメージ: [`6-text-to-image-ui.png`](6-text-to-image-ui.png)

---

## 設計の骨格（コード）

### ファイル

| パス | 由来 |
|------|------|
| `Assets/6.TextToImage/TextToImage.unity` | 新規。Camera / EventSystem / 本体。大きな UI は Play 時に組んでもよい |
| `Assets/6.TextToImage/Script/TextToImage.cs` | 新規（送信・受信・表示・可視化）。共通基底は作らない |
| `Assets/6.TextToImage/README.md` | WorkshopMaterial 準拠で本文化（実装時） |

Prefab / Resource は、実ファイルが必要になるまで作らない。

### `TextToImage.cs` の流れ（読む順）

1. `Start` — キー、UI、空プレビュー
2. `OnSendClicked` → `StartCoroutine(SendImageCoroutine)`
3. `BuildRequestJson` — `contents` ＋ `generationConfig.responseModalities`
4. `UnityWebRequest` POST — 1A と同じ待ち方
5. `TryExtractInlineImage` — 3A の TTS 取り出しを画像向けにコピー
6. `Texture2D.LoadImage` → `RawImage.texture`
7. `ShowRequest` / `ShowResponse` — Base64 は表示だけ短縮

### 1A / 3A から流用／捨てるもの

| 流用 | 捨てる／置き換える |
|------|-------------------|
| APIキー読込、POST、Status 点滅、キーマスク、JSON 整形 | 会話履歴、吹き出し、systemInstruction、コンテキスト Toggle |
| 3A の `inlineData` 抽出と Base64 短縮 | 音声バイト → **画像バイト** |

---

## README（実装時）

章立ては WorkshopMaterial 準拠。口調は `1A.TextToText` に寄せる。

- **学べること**: 画像生成／画像バイトの受け取りと表示／テキストや音声とは違う戻り値、など
- **事前準備**: APIキー。画像モデルがキーで使えること（無料枠は実装時に pricing へ追記）
- **動かし方**: シーンを開く → プロンプト送信 → 左の絵と中央の `responseModalities` → 右の mime / バイト数
- **概念節**: 画像生成とは？、`responseModalities`、`inlineData`（画像バイト）
- **主要クラス**: `TextToImage` のみ

書かないもの: 改変ヒント、つまずき集、次デモ誘導、Status の点滅など付加 UI の説明。

---

## 実装タスク順

1. `TextToImage.cs` の POST 骨子（1A コピー、履歴なし）
2. `responseModalities` 付きリクエストと Request ペイン
3. `inlineData` → `Texture2D` → 左プレビュー
4. Response の要約（mime / バイト数）と Base64 短縮
5. 体験 UI（3分割）。吹き出しは作らない
6. README・overview の「実装済み」更新、pricing に画像モデルを1行
7. コミット / push（クラウドのため Editor 検証は省略と明記）

---

## リスク・注意

| 点 | 扱い |
|----|------|
| 巨大な Base64 | 送信は全文。画面表示だけ 3A と同じ短縮 |
| 無料枠 | 画像モデルは Lite テキストより回数が少ない想定。429 は既存の pricing 案内へ |
| モデル名の可用性 | 実装時に疎通できる画像モデルを既定に。だめなら lite-image を試す |
| JSON のキー名 | REST は `inlineData` / `mimeType` を先に見る（3A 踏襲）。公式例の snake_case はフォールバック |
| 日本語 UI | NotoSansJP をインスペクタで渡す |
| セキュリティ | 教材は APIキー直結。README で ephemeral token に一言 |

---

## 判断の固定

- **REST `generateContent` 1回**（Live / Interactions API / Imagen 専用 API は使わない）
- 応答は **`TEXT` + `IMAGE`**（キャプションがあれば画像の下に出す）
- **単発**（履歴なし）。続きの編集は 7
- 可視化は 1A 型の **3分割**。画像本体は左、JSON の核は中央の `responseModalities`
- Firebase 等のラッパー SDK は使わない
- 共通基底クラスは作らない
- **7** は同じ REST 骨格で入力に画像を足す（本プランの実装スコープ外）

実装済み。クラウド環境のため Editor 検証は対象外。
