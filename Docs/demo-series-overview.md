# デモシリーズ全体構成（概要）

音声・画像インタラクション・ワークショップの学習順と、各デモがパイプラインのどこにあたるかをまとめます。

対象デモ: `1A.TextToText` / `1B.TextToJSON` / `2A.SpeechToText` / `2C.(SpeechToTextSherpa)` / `2D.(SpeechToTextWhisper)` / `2B.SpeechToJSON` / `3A.SpeechToSpeech` / `3B.SpeechToSpeechLiveAPI` / `3C.SpeechToMotion` / `4.VisionToSpeech` / `5.ScreenToSpeech` / `6.TextToImage` / `7.ImageToImage`

---

## 学習の三段

1. **つながる / 形で動かす（テキスト入力）** … `1A` → `1B`
2. **声で入る（マイク入力）** … `2A` → `2C`（sherpa）→ `2D`（whisper）→ `2B` → `3A`（REST で TTS）→ `3B`（Live API）→ `3C`（Live + function call）
3. **見る・描く** … `4`〜`7`（映像・画像）

STT は Speech-to-Text（音声→テキスト）、TTS は Text-to-Speech（テキスト→音声）、LLM は Large Language Model（大規模言語モデル）の略です。

入力モダリティごとに、**A = 自由テキスト出力 / B = JSON（構造化）出力** を揃えています。ただし `3` 以降は出力が音声になるため、A / B ではなく別の軸で並べます。

- `3A` / `3B` … REST 三段と Live セッションの対比
- `3C` … Live の途中で関数を呼ぶ
- `4` / `5` … 映像→音声。どちらも Live API

各デモの手順は、そのフォルダの README を見てください。

---

## デモ一覧（入力と出力）

並びは番号順ではなく、上から学習していく順です。

| # | フォルダ | 入力 | 処理の骨格 | 出力 |
|---|----------|------|------------|------|
| 1A | [`Assets/1A.TextToText/`](../Assets/1A.TextToText/) | テキスト | LLM | テキスト |
| 1B | [`Assets/1B.TextToJSON/`](../Assets/1B.TextToJSON/) | テキスト | LLM（JSON） | UI / パラメータ |
| 2A | [`Assets/2A.SpeechToText/`](../Assets/2A.SpeechToText/) | マイク音声 | Gemini STT → LLM | テキスト |
| 2C | [`Assets/2C.(SpeechToTextSherpa)/`](../Assets/2C.%28SpeechToTextSherpa%29/) | マイク音声 | sherpa STT → LLM | テキスト |
| 2D | [`Assets/2D.(SpeechToTextWhisper)/`](../Assets/2D.%28SpeechToTextWhisper%29/) | マイク音声 | whisper STT → LLM | テキスト |
| 2B | [`Assets/2B.SpeechToJSON/`](../Assets/2B.SpeechToJSON/) | マイク音声 | STT → LLM（JSON） | UI / パラメータ |
| 3A | [`Assets/3A.SpeechToSpeech/`](../Assets/3A.SpeechToSpeech/) | マイク音声 | STT → LLM → TTS（REST） | 音声 |
| 3B | [`Assets/3B.SpeechToSpeechLiveAPI/`](../Assets/3B.SpeechToSpeechLiveAPI/) | マイク音声 | Live API（音声↔音声） | 音声 |
| 3C | [`Assets/3C.SpeechToMotion/`](../Assets/3C.SpeechToMotion/) | マイク音声 | Live API（function call） | 音声 + 運動 |
| 4 | [`Assets/4.VisionToSpeech/`](../Assets/4.VisionToSpeech/) | カメラ画像 | Live API（映像→音声） | 音声 |
| 5 | [`Assets/5.ScreenToSpeech/`](../Assets/5.ScreenToSpeech/) | 描画パッド | Live API（映像→音声） | 音声 |
| 6 | [`Assets/6.TextToImage/`](../Assets/6.TextToImage/) | テキスト | 画像生成 | 画像 |
| 7 | [`Assets/7.ImageToImage/`](../Assets/7.ImageToImage/) | カメラ1フレーム＋指示 | 画像変換 | 画像 |

```text
[1A] Text ──────────────► LLM ──────────────────────► Text
[1B] Text ──────────────► LLM (JSON) ───────────────► UI / 数値など
[2A] Mic ──► Gemini STT ─► LLM ───────────────────► Text
[2C] Mic ──► sherpa STT ─► LLM ───────────────────► Text
[2D] Mic ──► whisper STT ─► LLM ───────────────────► Text
[2B] Mic ──► STT ────────► LLM (JSON) ────────────► UI / 数値など
[3A] Mic ──► STT ─────► LLM ──► TTS ──────────────► Audio
[3B] Mic ════════════► Live API ══════════════════► Audio
[3C] Mic ════════════► Live API + tool ═══════════► Audio + 運動
[4]  Camera ═════════► Live API ══════════════════► Audio
[5]  Screen ═════════► Live API ══════════════════► Audio
[6]  Text ──────────────► Image Gen ────────────────► Image
[7]  Camera ＋ Text ────► Image Edit ───────────────► Image
```

共通の前提（キー取得など）は [gemini-ai-studio-setup.md](gemini-ai-studio-setup.md) を参照してください。無料枠のあと有料に移るとき → [gemini-api-pricing.md](gemini-api-pricing.md)

---

## 設計の骨格

教材として追いやすくするため、次を守っています。

- **1 デモ = 1 フォルダ**（シーン・メインスクリプト・README をセット）
- **フォルダ名は `Assets/{番号}.{題名}/`**（例: `1A.TextToText`。番号と題名のあいだはピリオドのみ、スペースなし）
- **パイプラインを隠さない** — Status / 中間結果（テキスト・JSON）を見える化する（`1A` と同じ考え方。`5` と `7` は体験画面のため送信／受信欄を出さない）
- **API キーは `Assets/Common/APIKey.txt`**（リポジトリにはコミットしない）
- **共通基盤への寄せすぎはしない** — コピーして改変しやすい短い流れを優先

### 共有しているもの / していないもの

繰り返し出てくる**純粋関数だけ**を `Assets/Common/Script/` に置いています。名前だけで用が足りるので、デモを上から下まで読むときに Common へ飛ぶ必要はありません。

| | 置き場所 | 例 |
|---|---|---|
| 共有する（葉） | `Assets/Common/Script/` | JSON のエスケープ・整形（`GeminiJson`）、JSON 走査（`GeminiJsonScan`）、APIキー（`GeminiKey`）、レスポンスのテキスト取り出し（`GeminiTextResponse`）、音声の形の変換（`AudioCodec`）、通信ペインの整形（`HttpDisplay`）、テクスチャ縮小（`TextureUtil`） |
| 共有しない（背骨） | 各デモの `Script/` | Live セッション、マイク制御、音声再生、Status 表示、SystemInstruction 同期、カメラ制御 |
| 共有しない（主題） | 各デモの `Script/` | **リクエスト JSON の組み立てとレスポンスのキー解釈** — 何を送って何が返るかは、そのデモで学ぶことそのもの |

判定条件と、変更したくなったときの手順は [WorkshopMaterial.mdc](../.cursor/rules/WorkshopMaterial.mdc) にあります。

---

## 各デモで増えるもの（ざっくり）

| デモ | 前の段階から増える主な要素 |
|------|------------------------------|
| 1A.TextToText | （出発点）テキストの送受信、会話コンテキスト、System Instruction |
| 1B.TextToJSON | 決まった形（JSON）での返答、パース、UI / パラメータへの反映 |
| 2A.SpeechToText | マイク録音、音声→テキスト（STT）。出力は 1A と同型のテキスト |
| 2C.(SpeechToTextSherpa) | STT だけ sherpa-onnx（端末）。Chat は 2A と同じ Gemini |
| 2D.(SpeechToTextWhisper) | STT だけ whisper.unity（端末）。Chat は 2A と同じ Gemini |
| 2B.SpeechToJSON | 2A の入力＋1B の JSON 反映（組み合わせ） |
| 3A.SpeechToSpeech | TTS と再生（REST の `generateContent` 三段。ここで声の出口が初出） |
| 3B.SpeechToSpeechLiveAPI | Live API で音声→音声を一セッションにまとめる（送信／受信の可視化） |
| 3C.SpeechToMotion | Live の function call で符号付き角速度とサイズを変え、目標へ漸近させる |
| 4.VisionToSpeech | WebCam フレームを Live API へ（Space シャッター／連続送信）。声で返答 |
| 5.ScreenToSpeech | ドローイングパッドの画面を Live へ。送信／受信欄は出さず、描きながら声で解釈する |
| 6.TextToImage | 画像生成リクエスト、テクスチャ表示 |
| 7.ImageToImage | カメラ1フレーム＋指示での変換、ライブ映像 / After 表示 |
