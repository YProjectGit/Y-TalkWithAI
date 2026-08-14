# 4.VisionToSpeech 実装プラン

## 要点（サマリー）

- **何をするか**: WebCam の映像を **Live API** に渡し、見たものを **声で説明する**。REST の Vision→TTS 二段は使わない。
- **パイプライン**: Camera → **Live API（画像フレーム in → ネイティブ音声 out）** → 再生。マイク／STT／TTS モデルは使わない。
- **UX**: 既定は **Space シャッター（1フレーム送信）**。トグルボタンで **ストリーミング（約1 FPS 連続送信）** に切り替え、同じボタンで解除。ストリーミング中は Space 無効。
- **UI**: 3B と同じ **送信／受信の2分割**＋段階バー。左に WebCam プレビュー・シャッター案内・ストリームトグル・吹き出し（transcription）。
- **コピー派生**: 共通基底は作らない。`SpeechToSpeechLiveAPI.cs` を参考に映像入力へ差し替える。
- **触らないもの**: 1A〜3B の挙動、5 以降の実装（5 は後続で入力源だけ画面キャプチャに変える想定）。
- **完了条件**: シーン・スクリプト・README。クラウドのため Editor 検証は省略。
- **一言**: 3B が「声の Live」なら、4 は「目の Live」。
- **適用状況**: 実装済み（シーン・スクリプト・README・UIイメージ）。

| | 3B.SpeechToSpeechLiveAPI | 4.VisionToSpeech（本プラン） |
|---|--------------------------|------------------------------|
| 通信 | WebSocket Live ×1 | **同じ** |
| 入力 | マイク PCM（Space 押し話し） | **WebCam JPEG（Space シャッター／連続送信）** |
| 出力 | ネイティブ音声 ＋ transcription | **同じ** |
| 可視化 | 送信／受信2分割 | **同型**（送信ログは画像フレーム） |

---

## 学習上の位置づけ

```text
[3B] Mic ════════════► Live API ══════► Audio     … 声のセッション
[4]  Camera ═════════► Live API ══════► Audio     … 映像のセッション
[5]  Screen ═════════► Live API ══════► Audio     … 入力源だけ画面（後続）
```

- 3A/3B で REST 段分解と Live 一発を知ったあと、4 で **入力モダリティを映像に替える**。
- 学生が追う山場は **JPEG フレームを `realtimeInput` で送る**こと（音声 PCM ではない）。
- 画像→テキスト→TTS の REST 二段は教えない（Live でダイレクトに声が返る）。

---

## 処理の骨格

```text
Play
  → APIキー読込
  → WebCam 起動（ローカルプレビュー）
  → Live セッション接続（setup: AUDIO + voice + transcription + media_resolution）

【シャッターモード・既定】
Space 押下（Down）
  → いまのフレームを取得 → 長辺768前後へ縮小 → JPEG → Base64
  → realtimeInput（image/jpeg）を1回送る
  → サーバから PCM ＋ output transcription → 再生／吹き出し

【ストリーミングモード】
トグル ON
  → Space 無効
  → 約1秒間隔で同様のフレーム送信を繰り返す
トグル OFF
  → 連続送信停止 → シャッターモードへ戻る（Space 再び有効）
```

公式の入出力前提（実装時にドキュメントで再確認）:

| | 形式 |
|---|------|
| 送信映像 | JPEG（または PNG）フレーム、目安 **最大約1 FPS**、推奨 **768×768** 前後 |
| 受信音声 | raw 16-bit PCM, **24 kHz**, little-endian |
| プロトコル | ステートフル **WebSocket（WSS）** |
| モデル例 | `gemini-3.1-flash-live-preview`（インスペクタで変更可・3B と同系） |
| `media_resolution` | 既定は `low` または `medium`（細部／文字読みは `high` を検討） |

---

## Live API（実装時の前提）

- エンドポイント／メッセージ形は [Live API ドキュメント](https://ai.google.dev/gemini-api/docs/live-api) の現行形に合わせる（3B 実装を正本に寄せる）。
- Setup で少なくとも次を送る:
  - `response_modalities`: `["AUDIO"]`
  - `speech_config` / voice（例: `Kore`）
  - `output_audio_transcription`（吹き出し用。入力は映像なので input audio transcription は必須ではない）
  - 任意: `media_resolution`、`system_instruction`
- 映像送信は `realtimeInput` に **画像 Blob**（`image/jpeg`）。3B の PCM 送信ループの代わりに、シャッターまたは 1 FPS タイマーで送る。
- 必要なら短いテキスト指示（「写っているものを日本語で短く説明して」）を clientContent または同ターンのテキスト part で添える（実装時に 3B／公式形へ合わせる）。
- キーは `Assets/Common/APIKey.txt`。教材はクライアント直結（ephemeral token は README で一言）。

Unity 側:

- Firebase / 外部 Live SDK は使わない（3B と同じ素の WebSocket）。
- `WebCamTexture` でプレビュー。送信時だけ `GetPixels` → Texture2D → `EncodeToJPG` → リサイズ。
- メインスレッド制約: ソケット受信と UI／`AudioSource` の橋渡しを明示（3B 踏襲）。

---

## UX 詳細（判断の固定）

| 操作 | 動作 |
|------|------|
| Space（シャッターモード） | 1フレーム送信 → 返答待ち中は再シャッター抑制 |
| ストリームトグル ON | 約1 FPS 連続送信。**Space は無効**（案内文も切り替える） |
| ストリームトグル OFF | 連続送信停止。シャッターモードへ復帰 |
| マイク | **使わない**（Space は録音ではない） |

- プレビューは常時ローカル表示。API に載せるバイトだけ縮小・JPEG 化。
- ストリーミング中も応答音声は従来どおり再生キューへ。割り込み／バージインは第1版では入れない（必要なら後続）。

---

## UI 構成

3B の送信／受信2分割を踏襲し、左ペインに Vision 固有 UI を足す。

```text
┌──────────────────────────────────────────────────────────────────────────┐
│ 段階バー:  Connect → Send Frame → Receive PCM → Play                     │
├────────────────┬────────────────────────────┬────────────────────────────┤
│ Left           │ Center 送信（Outbound）    │ Right 受信（Inbound）      │
│ WebCam プレビュー│ ▴ Setup 設定ヘッダ       │ ▴ 受信前提ヘッダ           │
│ [Stream] トグル  │   (model/voice/media_res) │   (24kHz PCM / mime …)     │
│ Space 案内       │ Frame 送信ログ（主領域） │ ServerContent チャンクログ │
│ 吹き出し         │  +frame …KB / …x…         │ Transcription              │
│ Status           │ ─ 送信中（1行）          │ ─ 受信中/再生中（1行）     │
└────────────────┴────────────────────────────┴────────────────────────────┘
```

欄の中身:

1. **段階バー** … `Connect → Send Frame → Receive PCM → Play`
2. **左: WebCam プレビュー** … 送信解像度ではなくプレビュー用（生解像度で可）
3. **左: Stream トグル** … ON で連続送信、OFF でシャッター。ON 中は Space 無効と明示
4. **左: Space 案内** … 例: 「Space でシャッター」／ストリーム中は「ストリーミング中（Space 無効）」
5. **送信ログ** … `+frame 842x768 …KB` のような追記（生 Base64 は出さない／先頭省略可）
6. **受信** … 3B と同型（PCM チャンク＋ transcription）
7. **吹き出し** … output transcription 由来（Gemini）。シャッター時は任意で「（キャプチャ）」等の You 行を出してもよい

UI 構成イメージ: [`4-vision-to-speech-ui.png`](4-vision-to-speech-ui.png)

---

## 設計の骨格（コード）

### ファイル

| パス | 由来 |
|------|------|
| `Assets/4.VisionToSpeech/VisionToSpeech.unity` | 3B シーンを参考に左へ WebCam／トグルを追加 |
| `Assets/4.VisionToSpeech/Script/VisionToSpeech.cs` | 新規（接続・フレーム送信・再生・UI）。共通基底は作らない |
| `Assets/4.VisionToSpeech/Prefab/MessageBubble.prefab` | 1A/3B 流用可 |
| `Assets/4.VisionToSpeech/README.md` | WorkshopMaterial 準拠で本文化（実装時） |

### `VisionToSpeech.cs` の流れ（読む順）

1. `Start` — キー、System Instruction、WebCam、`AudioSource`、**Live 接続**
2. Space シャッター — 旧 Input Manager（ストリーム OFF のときだけ）
3. Stream トグル — ON でコルーチン／タイマー（約1 FPS）開始、OFF で停止
4. フレーム送信 — リサイズ → JPEG → Base64 → `realtimeInput`
5. 受信ループ — audio → 再生キュー / transcript → 吹き出し / パネル更新（3B 踏襲）
6. `OnDestroy` — セッション切断、WebCam 停止

### 3B から流用／捨てるもの

| 流用 | 捨てる／置き換える |
|------|-------------------|
| WebSocket 接続、Setup、受信 PCM 再生、transcription、送信／受信 UI | マイク、Space 押し話し PCM、音声チャンクログ → **フレーム送信ログ** |

---

## README（実装時）

章立ては WorkshopMaterial 準拠。口調は `1A.TextToText` / `3B` に寄せる。

- **学べること（問い）**: Live で映像がどう声になるか／シャッターと連続送信の違いは何か／送っているのは動画ファイルかフレームか、など
- **動かし方**: 接続 → Space シャッター → 声 → Stream トグルで連続送信 → 送信／受信ログを見る
- **概念節**: Live API の映像入力とは？、シャッターとストリーミング、送信解像度（768 前後）と `media_resolution`
- **主要クラス**: 接続 → フレーム送信 → 受信 → 再生の順

書かないもの: 改変ヒント、つまずき集、次デモ誘導。

---

## 実装タスク順

1. 3B 相当のシーン骨子を 4 に用意（左に WebCam プレビュー＋ Stream トグル）
2. WebSocket 接続＋ Setup＋切断（3B を参考に最小移植）
3. Space シャッターで1フレーム送信
4. 受信 PCM 再生＋ transcription → 吹き出し
5. Stream トグル（約1 FPS、ON 中 Space 無効）
6. 送信／受信パネル可視化（フレームログ、キーマスク）
7. README・overview の「実装済み」更新
8. コミット / push（クラウドのため Editor 検証は省略と明記）

---

## リスク・注意

| 点 | 扱い |
|----|------|
| WebCam 権限・デバイスなし | Status に明示。Editor／実機差は README で一文 |
| 解像度・ペイロード | 送信前に長辺768前後へ縮小。プレビューは別 |
| 1 FPS 制限 | ストリーム間隔を約1秒に。連打シャッターは isSending で抑止 |
| ストリーム中の発話重複 | 第1版は割り込みなし。再生中もフレームは送り続けてよいが、必要なら「再生中は送信暂停」を後から検討 |
| メインスレッド | 3B と同様、受信→UI/Audio の戻しをコメントで明示 |
| モデル名の可用性 | 実装時に疎通できる Live モデルを既定に |
| セキュリティ | 教材は APIキー直結。README で ephemeral token に一言 |

---

## 判断の固定

- **Live API 一セッション**（REST Vision→TTS はしない）
- 応答は **AUDIO のみ**＋ output transcription
- UX は **Space シャッターが既定**、**トグルで連続送信**、**連続中は Space 無効**
- マイクは使わない
- 送信画像は **JPEG・長辺768前後・ストリーム時約1 FPS**
- 可視化は 3B 型の **送信／受信2分割**（GenerateContent 1〜n 欄は置かない）
- Firebase 等のラッパー SDK は使わない
- 共通基底クラスは作らない
- **5** は同じ Live 骨格で入力源だけ Screen に差し替える（本プランの実装スコープ外）

プラン段階（実装は別タスク）。クラウド環境のため Editor 検証は対象外。
