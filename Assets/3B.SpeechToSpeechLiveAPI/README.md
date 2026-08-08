# 3B.SpeechToSpeechLiveAPI

シリーズ全体の位置づけ → [Docs/demo-series-overview.md](../../Docs/demo-series-overview.md)

---

## このデモで学べること

- **Live API**  
  双方向のセッションを張り、音声をリアルタイムにやり取りする
- **リアルタイム音声対話**  
  STT / チャット / TTS を分けず、声のまま会話する流れ
- **ストリームとしての音声**  
  一枚のファイルではなく、チャンクが連続して流れるデータの扱い

---

## 事前準備

1. Google AI Studio から Gemini の API にアクセスするための APIキーを取得し、`Assets/Common/APIKey.txt` に保管してください。  
   手順 → [Docs/gemini-ai-studio-setup.md](../../Docs/gemini-ai-studio-setup.md)
2. PC にマイクがつながり、Unity から使える状態にしてください（OS のマイク権限を含む）。
3. スピーカーまたはヘッドホンで再生音が聞こえる状態にしてください。

---

## 動かし方

Project ウィンドウで `Assets/3B.SpeechToSpeechLiveAPI/SpeechToSpeechLiveAPI.unity` を開き、Play を押してください。

### 1. 接続を確認する

1. 上部の段階バーが `Connect` 付近であること、左 Status が「接続済み」になることを見てください。
2. 中央（送信）上部の **Setup** ヘッダに、model / AUDIO / voice / transcription などの設定が出ることを確認してください。

### 2. Space で話してみる

1. **Space を押したまま**短い文を話し、**離してください**。
2. 段階バーが `Send PCM` → `Receive PCM` → `Play` と進むこと、中央に送信チャンクログ、右に受信チャンクログが増えることを見てください。
3. 左に吹き出し（transcription）が出て、返答が声で再生されることを確認してください。

教材デモでは APIキーをクライアントから直接使います。本番アプリでは ephemeral token などの短い資格情報を使うことが推奨されます。

---

## Live API（セッション）とは？

Live API とは、HTTP の `generateContent` を何回も呼ぶのではなく、**WebSocket で1本のセッションを張り、音声をチャンクで双方向に流す**仕組みです。

このデモでは Play 開始時に接続と Setup を行い、そのあと Space 押し話しの PCM を `realtimeInput` で送り、サーバからの PCM と transcription を受け取ります。3A のように「文字起こし用」「チャット用」「TTS 用」とリクエストを分けません。

試し方: 中央 Setup ヘッダと、Space 中に増える送信ログ、返答時の受信ログを見比べる。

---

## 3A（REST 三段）との違いは？

| | 3A | 3B（このデモ） |
|---|----|----------------|
| 通信 | `generateContent` ×3 | WebSocket Live ×1 |
| 見える化 | 発生順 1〜6 | 送信列 / 受信列 |
| 文字 | Chat の text | transcription |

体験（声で入って声で返る）は近いですが、学生が追う山場が「何段の REST か」から「セッションとオンの向き」へ移ります。

試し方: 同じ発話内容を 3A と 3B で試し、画面の欄の種類の違いを見る。

---

## PCM ストリームと再生バッファとは？

PCM とは、音を時刻ごとの振幅の数字列として表した音声データです。このデモの送信は 16 kHz、受信はおおむね 24 kHz の 16-bit PCM です。

ソケットには一度に全部ではなく、小さなチャンクが連続して流れます。右の受信ログがその要約で、再生側ではチャンクをキューに入れてから `AudioSource` で順に鳴らします。

試し方: Space 中に中央ログの `+chunk` が増えること、返答中に右ログの `+audio` と「再生中」を見比べる。

---

## 主要クラス

### SpeechToSpeechLiveAPI（[`SpeechToSpeechLiveAPI.cs`](Script/SpeechToSpeechLiveAPI.cs)）

デモの本体です。上から、接続〜送信〜受信〜再生の順に追うとわかりやすいです。

通信は **ClientWebSocket**（双方向のソケット）です。受信はバックグラウンドのループで行い、UI や `AudioSource` の更新だけメインスレッドのキュー経由で戻します。Space の押し話し検知は `Update` と **旧 Input Manager**（`Input.GetKeyDown` / `GetKeyUp`）です。

1. **Live セッションに接続する**  
   `ConnectLiveSessionCoroutine` — WSS 接続 → Setup JSON 送信 → `setupComplete` 待ち
2. **Space 押し話しを検知する**  
   `UpdatePushToTalk` — 押しているあいだ録音送信、離したら `activityEnd`
3. **PCM チャンクを送る**  
   `PumpMicrophoneChunksIfRecording` — マイク差分 → 16-bit PCM → Base64 → `realtimeInput.audio`
4. **サーバメッセージを振り分ける**  
   `HandleServerMessage` — 音声は再生キュー、transcription はログと吹き出し
5. **受信 PCM を再生する**  
   `PlaybackPumpCoroutine` — キューから `AudioClip` 化して `AudioSource.Play`

### ChatBubble（[`ChatBubble.cs`](../1A.TextToText/Script/ChatBubble.cs)）

左ペインの吹き出し1件分です（Prefab: [`Prefab/MessageBubble.prefab`](Prefab/MessageBubble.prefab)）。見た目用で、通信ロジックは持ちません。文面は Live の transcription 由来です。
