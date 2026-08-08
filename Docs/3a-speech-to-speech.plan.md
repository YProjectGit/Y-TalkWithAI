# 3A.SpeechToSpeech 実装プラン

## 要点（サマリー）

- **何をするか**: `2A.SpeechToText` の後段に **TTS（声の出口）と再生** を足し、「声で話して声で返る」一気通貫にする。シリーズで TTS 初出（REST）。
- **パイプライン**: Mic → WAV → **1→2 Audio（文字起こし）** → **3→4 Text（返答）** → **5→6 TTS（音声生成）** → `AudioSource` 再生。
- **UI**: 左は 2A 型（吹き出し＋Space）。中央／右は発生順 **1〜6**（Request/Response × Audio / Text / TTS）。
- **コピー派生**: 共通基底は作らない。`SpeechToText.cs` を土台に TTS 段と再生を足す。
- **触らないもの**: 1A/1B/2A/2B の挙動、`3B`（Live API）、4 以降。
- **完了条件**: シーン・スクリプト・README。クラウドのため Editor 検証は省略し、UI 構成イメージを画像で出す。
- **姉妹デモ**: Live API で音声→音声を一発にする案は `3B.SpeechToSpeechLiveAPI`（後続）。

| | 2A.SpeechToText | 3A.SpeechToSpeech（本プラン） |
|---|-----------------|------------------------------|
| 1→2 | GenerateContent（Audio）文字起こし | 同じ |
| 3→4 | GenerateContent（Text）自由文返答 | 同じ（吹き出し表示） |
| 5→6 | なし | **GenerateContent（TTS）→ 再生** |
| 左ペイン | チャット | チャット＋再生中表示 |

---

## 処理の骨格

```text
Space 押し話し
  → Microphone → WAV → Base64
  → 1. Request  GenerateContent（Audio）
  → 2. Response GenerateContent（Audio）   … 認識テキスト
  → 3. Request  GenerateContent（Text）
  → 4. Response GenerateContent（Text）    … 返答テキスト
  → 5. Request  GenerateContent（TTS）
  → 6. Response GenerateContent（TTS）     … 音声バイト
  → AudioClip 化 → AudioSource.Play
```

標題（2A/2B と同じ規則・発生順）:

1. `Request - GenerateContent（Audio）`
2. `Response - GenerateContent（Audio）`
3. `Request - GenerateContent（Text）`
4. `Response - GenerateContent（Text）`
5. `Request - GenerateContent（TTS）`
6. `Response - GenerateContent（TTS）`

---

## TTS API（実装時の前提）

Gemini の **TTS 向け `generateContent`** を使う（Cloud Text-to-Speech の別エンドポイントにはしない。キーも `APIKey.txt` のまま）。

- モデル: 実装時にキーで使える TTS モデルを選ぶ（既定例: `gemini-3.1-flash-tts-preview`）。インスペクタで変更可能にする。
- リクエスト要点:
  - `contents` に返答テキスト（短い読み上げ指示付き）
  - `generationConfig.responseModalities`: `["AUDIO"]`
  - `generationConfig.speechConfig.voiceConfig.prebuiltVoiceConfig.voiceName`（例: `Kore`）
- レスポンス: `candidates[].content.parts[].inlineData`（音声バイト／Base64）を取り出し、Unity で `AudioClip` 化して再生。

教材コメントでは「Text 段で得た文を、TTS モデルの generateContent に渡して AUDIO を受け取る」と追えるように書く。

---

## UI 構成

```text
┌──────────────┬─────────────────────────────┬─────────────────────────────┐
│ Left Chat    │ Center                      │ Right                       │
│ 吹き出し     │ 1. Request（Audio）         │ 2. Response（Audio）        │
│ Space 案内   │ 3. Request（Text）          │ 4. Response（Text）         │
│ Status       │ 5. Request（TTS）           │ 6. Response（TTS）          │
│ （再生中）   │                             │                             │
└──────────────┴─────────────────────────────┴─────────────────────────────┘
```

- **左**: 2A と同型（System Instruction あり、コンテキスト常時 ON、Space 押し話し、吹き出し）。再生中は Status で分かるようにする。
- **中央／右**: 上下3段ずつ（発生順の奇数＝Request、偶数＝Response）。
- 6. Response は音声バイナリが大きいので、表示は MIME／バイト数／先頭省略に留め、実データは再生に回す。

---

## 設計の骨格（コード）

### ファイル

| パス | 由来 |
|------|------|
| `Assets/3A.SpeechToSpeech/SpeechToSpeech.unity` | 2A シーンを複製し、中央／右を3段化 |
| `Assets/3A.SpeechToSpeech/Script/SpeechToSpeech.cs` | 2A をコピーして TTS＋再生を追加 |
| `Assets/3A.SpeechToSpeech/Prefab/MessageBubble.prefab` | 2A からコピー（ChatBubble は 1A 流用） |
| `Assets/3A.SpeechToSpeech/README.md` | WorkshopMaterial 準拠で本文化 |

### `SpeechToSpeech.cs` の流れ

1. `Start` — APIキー、System Instruction、マイク、`AudioSource` 用意
2. Space 押し話し（旧 Input Manager）→ WAV → Base64
3. **1→2** Audio generateContent（文字起こし）→ You 吹き出し
4. **3→4** Text generateContent（履歴付きチャット）→ Gemini 吹き出し
5. **5→6** TTS generateContent（`responseModalities: AUDIO`）→ PCM/WAV 解釈 → `AudioSource.Play`
6. 各欄へ 1〜6 を表示

### 2A から流用／増やすもの

| 流用 | 追加 |
|------|------|
| Space、Mic、WAV、1→4、吹き出し、コンテキスト常時 ON | TTS モデル名、voiceName、`AudioSource`、音声デコード、5→6 欄 |

---

## README（実装時）

- 学べること（問い）: 声が返るまでに何段の generateContent があるか／TTS はどの Request か、など
- 動かし方: Space → 吹き出し → 声で返答 → 1〜6 を順に見る
- 概念節: TTS（Text-to-Speech）とは？、マイクと音声データ（短く）
- 主要クラス: 発生順 1〜6 で読む

---

## 実装タスク順

1. `3.SpeechToSpeech` を `3A.SpeechToSpeech` に改名し、`3B.SpeechToSpeechLiveAPI` スタブを追加
2. 2A から Script／Prefab／シーンを 3A へコピー
3. 中央・右を3段（1〜6）に拡張し、標題を設定
4. `SpeechToSpeech.cs` に TTS リクエスト・デコード・再生を追加
5. README・overview 更新
6. **UI 構成イメージを画像出力**
7. コミット / push（Editor 検証は省略と明記）

---

## リスク・注意

| 点 | 扱い |
|----|------|
| TTS モデル名の可用性 | 実装時に疎通できるモデルを選び、インスペクタ既定にする |
| 音声フォーマット | レスポンスの MIME（PCM/WAV 等）に合わせてデコード。失敗時は 6. Response に理由を出す |
| UI の高さ | 3段×2は窮屈になりやすい。LayoutElement の flexibleHeight で均等割り |

---

## 判断の固定

- TTS も **generateContent**（専用 Cloud TTS REST にはしない）
- 番号は発生順 **1〜6**（Audio / Text / TTS）
- テキスト送信欄は置かない（Space のみ）
- 共通クラス化はしない
- Live API 版は **3B** に分離（本デモでは扱わない）
