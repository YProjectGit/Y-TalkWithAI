# デモシリーズ全体構成（概要）

音声・画像インタラクション・ワークショップの学習順と、各デモのパイプライン位置づけです。  
**いま実装済みなのは `1. TextChat` のみ**です。2〜8 はフォルダと概要 README だけの段階です。

---

## 学習の三段

1. **つながる** … `1. TextChat`（送受信と可視化）
2. **形で動かす** … `2. StructuredOutput`（構造化出力で Unity を更新）
3. **感覚を足す** … `3`〜`8`（声・映像・画像）

---

## 学習の進め方

入力と出力を一段ずつ足していきます。前のデモで覚えた「LLM への送受信」を土台にします。

| # | フォルダ | 入力 | 処理の骨格 | 出力 |
|---|----------|------|------------|------|
| 1 | [`Assets/1. TextChat/`](../Assets/1.%20TextChat/) | テキスト | LLM | テキスト |
| 2 | [`Assets/2. StructuredOutput/`](../Assets/2.%20StructuredOutput/) | テキスト | LLM（JSON） | UI / パラメータ |
| 3 | [`Assets/3. TextToSpeech/`](../Assets/3.%20TextToSpeech/) | テキスト | LLM → TTS | 音声 |
| 4 | [`Assets/4. SpeechToSpeech/`](../Assets/4.%20SpeechToSpeech/) | マイク音声 | STT → LLM → TTS | 音声 |
| 5 | [`Assets/5. VisionToSpeech/`](../Assets/5.%20VisionToSpeech/) | カメラ画像 | Vision LLM → TTS | 音声 |
| 6 | [`Assets/6. ScreenToSpeech/`](../Assets/6.%20ScreenToSpeech/) | 画面キャプチャ | Vision LLM → TTS | 音声 |
| 7 | [`Assets/7. TextToImage/`](../Assets/7.%20TextToImage/) | テキスト | 画像生成 | 画像 |
| 8 | [`Assets/8. ImageToImage/`](../Assets/8.%20ImageToImage/) | 画像＋指示 | 画像変換 | 画像 |

```text
[1]  Text ──────────────► LLM ──────────────────────► Text
[2]  Text ──────────────► LLM (JSON) ───────────────► UI / 数値など
[3]  Text ──────────────► LLM ──► TTS ──────────────► Audio
[4]  Mic ──► STT ─────► LLM ──► TTS ──────────────► Audio
[5]  Camera ──────────► Vision LLM ──► TTS ───────► Audio
[6]  Screen ──────────► Vision LLM ──► TTS ───────► Audio
[7]  Text ──────────────► Image Gen ────────────────► Image
[8]  Image ＋ Text ─────► Image Edit ───────────────► Image
```

共通の前提（キー取得など）は [gemini-ai-studio-setup.md](gemini-ai-studio-setup.md) を参照します。

---

## 設計の骨格（実装時の方針）

教材として追いやすくするため、次を守る想定です。

- **1 デモ = 1 フォルダ**（シーン・メインスクリプト・README をセットで後から足す）
- **パイプラインを隠さない** — 右ペインなどで Status / 中間結果（テキスト・JSON）を見える化する（TextChat と同じ考え方）
- **API キーは `Assets/Common/APIKey.txt`**（リポジトリにはコミットしない）
- **共通基盤への寄せすぎはしない** — コピーして改変しやすい短い流れを優先
- 具体的なエンドポイント名・モデル名・音声／画像フォーマットは、各デモ実装時に決める（概要 README ではパイプラインだけ固定）

---

## 各デモで増えるもの（ざっくり）

| デモ | 前の段階から増える主な要素 |
|------|------------------------------|
| 2. StructuredOutput | 決まった形（JSON）での返答、パース、UI / パラメータへの反映 |
| 3. TextToSpeech | LLM の返答テキストを音声にする（TTS）、再生 |
| 4. SpeechToSpeech | マイク録音、音声→テキスト（STT）、その後は 3 と同様 |
| 5. VisionToSpeech | WebCam などからの画像取得、画像付きで LLM へ、返答を TTS |
| 6. ScreenToSpeech | 画面／RenderTexture のキャプチャ（入力源がカメラではなく画面） |
| 7. TextToImage | 画像生成リクエスト、テクスチャ表示（出力が音声ではなく画像） |
| 8. ImageToImage | 入力画像＋指示での変換、Before / After 表示 |

---

## いまの完了条件（この段階）

- [x] 2〜8 の番号フォルダがある（`2. StructuredOutput` を含む）
- [x] 各フォルダに概要 README がある
- [x] 本ドキュメントでシリーズ全体の位置づけが読める
- [ ] 各デモのシーン・スクリプト（実装は別タスク。`1. TextChat` のみ実装済み）

詳細な手順・改変ヒントは、実装が入ったタイミングで各デモ README を TextChat 並みに厚くします。
