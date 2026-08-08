# 3B.SpeechToSpeechLiveAPI 実装プラン

## 要点（サマリー）

- **何をするか**: `3A` と同じ「声で話して声で返る」体験を、**Live API（WebSocket・ネイティブ音声）1セッション**で実現する。REST の STT→LLM→TTS 三段は使わない。
- **学習の山場**: 「1回の `generateContent` が何段あるか」ではなく、**接続 → PCM を流す → PCM が返る →（文字起こしで）吹き出し** というストリームの時間軸。
- **入力**: 学生 UX は 2A/3A に寄せて **Space 押し話し**（押しているあいだ PCM を送る）。連続常時会話は入れない（難度・教材の追従性のため）。
- **出力**: `response_modalities: ["AUDIO"]`。左吹き出し用に **input / output audio transcription** をオンにする（音声だけだと追いにくい）。
- **コピー派生**: 共通基底は作らない。UI は 3A 左ペインを参考にしつつ、中央／右は **送信／受信のリアルタイム進行**が分かれる Live 可視化に差し替える（1〜6 の GenerateContent 欄は置かない）。
- **触らないもの**: 1A〜3A の挙動、4 以降。Firebase SDK への依存は避ける（教材は APIキー＋素の接続で追える形）。
- **完了条件**: シーン・スクリプト・README。クラウドのため Editor 検証は省略し、UI 構成イメージを画像で出す。

| | 3A.SpeechToSpeech（REST） | 3B.SpeechToSpeechLiveAPI（本プラン） |
|---|---------------------------|--------------------------------------|
| 通信 | HTTP `generateContent` ×3 | **WebSocket Live セッション ×1** |
| 段 | Audio → Text → TTS | **音声 in → 音声 out（＋文字起こし）** |
| 可視化 | 発生順 1〜6 | **段階バー + Setup厚め + 送受信ログ（ステータスは1行）** |
| 文字 | Chat の text パート | **transcription**（吹き出し用） |
| キー | `APIKey.txt` | 同じ（教材ではクライアント直結） |

---

## 学習上の位置づけ

```text
[3A] Mic ──► STT ──► LLM ──► TTS ──► Audio     … 段が見える
[3B] Mic ══════════► Live API ══════► Audio     … セッションが見える
```

- `3A` で「声の出口（TTS）」を知ったあと、`3B` で「同じ体験をストリーム1本にまとめる」対比にする。
- 学生が追う山場は **WebSocket の寿命**と **PCM チャンクの送受信**（Base64 の一枚岩 JSON ではない）。

---

## 処理の骨格

```text
Play
  → APIキー読込
  → Live セッション接続（setup: AUDIO + voice + transcription）
Space 押下
  → Microphone から PCM チャンクを読み出し
  → realtimeInput（audio/pcm;rate=16000）を送り続ける
Space 解放
  → 送信停止（必要なら activityEnd / ターン完了の合図）
  → サーバから PCM チャンク受信 → 再生バッファ → AudioSource
  → input/output transcription が来たら左吹き出しへ
```

公式の入出力前提（実装時にドキュメントで再確認）:

| | 形式 |
|---|------|
| 送信音声 | raw 16-bit PCM, **16 kHz**, little-endian |
| 受信音声 | raw 16-bit PCM, **24 kHz**, little-endian |
| プロトコル | ステートフル **WebSocket（WSS）** |
| モデル例 | `gemini-3.1-flash-live-preview`（インスペクタで変更可） |

---

## Live API（実装時の前提）

- エンドポイント／メッセージ形は実装時に [Live API ドキュメント](https://ai.google.dev/gemini-api/docs/live-api) の現行形に合わせる。
- Setup で少なくとも次を送る:
  - `response_modalities`: `["AUDIO"]`（TEXT との同時指定はしない）
  - `speech_config` / voice（例: `Kore`）
  - `input_audio_transcription` / `output_audio_transcription`（吹き出し用）
  - 任意: `system_instruction`（3A と同様 `SystemInstruction.txt` から）
- 音声送信は `realtimeInput`（PCM チャンク）。一発 WAV の `inlineData` 丸投げ（3A の STT）にはしない。
- 受信はサーバイベントを順に処理し、**audio パートは再生へ、transcript パートは UI へ**（1イベントに複数パートがあり得る点に注意）。
- キーは `Assets/Common/APIKey.txt`。教材デモのためクライアント直結とする（本番の ephemeral token は README で一言触れる程度）。

Unity 側の通信方針:

- **Firebase / 外部 Live SDK は使わない**（依存とブラックボックスを増やさない）。
- `ClientWebSocket`（または同等の素の WebSocket）＋コルーチン／async で送受信する短い自前実装にする。
- メインスレッド制約に注意: ソケット受信と `AudioSource` / UI 更新の橋渡しを明示する。

---

## UI 構成

3A の「1〜6 GenerateContent」は **置かない**（呼んでいないため）。代わりに **ソケットの送信／受信が左右で同時に進む**見える化にする。

### 方針（リアルタイム進行を明快にする）

| 原則 | 内容 |
|------|------|
| 左右で向きを分ける | **中央＝送信（Outbound）**、**右＝受信（Inbound）** |
| 上に段階バー | `Connect → Send PCM → Receive PCM → Play`。いまの段階だけ強調 |
| **初期設定を厚く** | 旧案の **1. Setup** を中央上部でしっかり見せる（model / modalities / voice / transcription フラグなど）。折りたたみで隠さない |
| チャンクは追記ログ | `+chunk …B / total …KB` を末尾に足す。生 PCM / 生 Base64 は出さない |
| **送受信中表示は薄い** | 「送信中」「受信中／再生中」は各列下の **1行ステータス**だけ。大きなバナー・レベルメーターは置かない |
| 文字は受信側 | **4. Transcription** は右下段。左吹き出しは会話の結果表示 |

```text
┌──────────────────────────────────────────────────────────────────────────┐
│ 段階バー:  Connect → Send PCM → Receive PCM → Play （細め）             │
├────────────────┬────────────────────────────┬────────────────────────────┤
│ Left Chat      │ Center 送信（Outbound）    │ Right 受信（Inbound）      │
│ 吹き出し       │ 1. Setup（設定 JSON 本体） │ 2. ServerContent（Audio）  │
│ Space 案内     │ 3. RealtimeInput ログ      │ 4. Transcription           │
│ Connected      │ ─ 送信中（1行・狭い）      │ ─ 受信中/再生中（1行・狭い）│
└────────────────┴────────────────────────────┴────────────────────────────┘
```

欄の中身:

1. **段階バー** … セッション全体のいま（細く、下部の送受信バナーの代わりに段階を担う）
2. **1. Setup - Live session（Outbound・広め）** … 接続時に送った設定のエッセンス  
   `model` / `response_modalities: AUDIO` / `speech_config.voice` / `input_audio_transcription` / `output_audio_transcription` など（キーはマスク）
3. **3. RealtimeInput - Live（Audio）** … 送信チャンクの追記ログ（16 kHz・累計バイト）
4. **2. ServerContent - Live（Audio）** … 受信チャンクの追記ログ（24 kHz・累計バイト）＋受信側の前提（レート等）の短い要約
5. **4. Transcription - Live** … input/output の文字起こし（左吹き出しと対応）
6. **各列フッター（狭い）** … `送信中` / `受信中・再生中` の1行だけ。常時大きく出さない

- **左**: 3A に近いチャット（You / Gemini）。文面は transcription 由来。Option は置かない。
- Space 以外のテキスト送信欄は置かない。
- 向きは列で固定。旧々案の「番号だけ互い違い」にも、巨大な送受信ヒーロー表示にもしない。

---

## 設計の骨格（コード）

### ファイル

| パス | 由来 |
|------|------|
| `Assets/3B.SpeechToSpeechLiveAPI/SpeechToSpeechLiveAPI.unity` | 3A 左ペインを参考に中央／右を Live 用に再構成 |
| `Assets/3B.SpeechToSpeechLiveAPI/Script/SpeechToSpeechLiveAPI.cs` | 新規（接続・PCM 送受信・再生・UI）。共通基底は作らない |
| `Assets/3B.SpeechToSpeechLiveAPI/Prefab/MessageBubble.prefab` | 1A/3A 流用可 |
| `Assets/3B.SpeechToSpeechLiveAPI/README.md` | WorkshopMaterial 準拠で本文化 |

必要なら送受信ログ整形だけの小さなネスト class は同ファイル内でよい（別アセンブリ化しない）。

### `SpeechToSpeechLiveAPI.cs` の流れ（読む順）

1. `Start` — キー読込、System Instruction、マイク、`AudioSource`、**Live 接続**
2. Space 押し話し — 旧 Input Manager（`Input.GetKeyDown` / `GetKeyUp`）
3. 録音ループ — `Microphone` のリングバッファから差分 PCM を切り出し → Base64 → `realtimeInput`
4. 受信ループ — サーバメッセージを読む → audio を再生キューへ / transcript を吹き出しへ / パネル更新
5. `OnDestroy` / 停止時 — セッション切断、マイク停止

### 3A から流用／捨てるもの

| 流用 | 捨てる／置かない |
|------|------------------|
| Space UX、吹き出し、Status、キー読込、System Instruction | STT/Chat/TTS の三段 `generateContent`、1〜6 欄、WAV 一枚送信 |

---

## README（実装時）

章立ては WorkshopMaterial 準拠。

- **学べること（問い）**: Live では声の往復がどう1セッションになるか／3A との違いは何か／PCM バッファは何か、など
- **動かし方**: 接続確認 → Space → 声で返る → Setup / RealtimeInput / ServerContent / Transcription を見る
- **概念節**: Live API（セッション）とは？、3A（REST 三段）との違い、PCM ストリームと再生バッファ
- **主要クラス**: 接続 → 送信 → 受信 → 再生の順。WebSocket とコルーチン／非同期の関係を短く

書かないもの: 改変ヒント、つまずき集、次デモ誘導。ephemeral token は「本番では推奨」程度の一文まで。

---

## 実装タスク順

1. 3A 左ペイン相当のシーン骨子を 3B に用意（中央／右は Live 4欄）
2. WebSocket 接続＋ Setup 送信＋切断
3. Space 押し話しで PCM チャンク送信
4. 受信 PCM の再生バッファ＋ transcription → 吹き出し
5. 4欄の可視化（要約表示、キーマスク）
6. README・overview の「実装済み」更新
7. **UI 構成イメージを画像出力**
8. コミット / push（Editor 検証は省略と明記）

---

## リスク・注意

| 点 | 扱い |
|----|------|
| WebSocket 実装の難度 | 教材用に最小メッセージだけ扱う。ツール呼び出し・映像は入れない |
| メインスレッド | 受信スレッド／Task から UI・Audio へ戻す経路をコメントで明示 |
| 再生ギャップ | 小さなキュー（チャンク連結）で連続再生。高度なジッタバッファは作らない |
| モデル名の可用性 | 実装時に疎通できる Live モデルを既定にし、インスペクタで変更可 |
| VAD / 割り込み | 第1版は Space でターン境界を明示。自動 VAD の細かい調整は入れない |
| セキュリティ | 教材は APIキー直結。README で ephemeral token に一言 |

---

## 判断の固定

- **Live API 一セッション**（REST の STT/LLM/TTS 再発明はしない）
- 応答モーダリティは **AUDIO のみ**＋ transcription で文字を足す
- UX は **Space 押し話し**（常時ハンズフリー会話はスコープ外）
- 可視化は GenerateContent 1〜6 ではなく **段階バー + Setup（初期設定）を厚く + 送受信チャンクログ（送受信中表示は1行）**
- Firebase 等のラッパー SDK は使わない
- 共通基底クラスは作らない

この方針で実装に進めてよいか確認する。
