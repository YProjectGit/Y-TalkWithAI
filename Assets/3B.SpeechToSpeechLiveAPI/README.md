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
- **VAD（Voice Activity Detection）**  
  無音で発話の区切りを決め、手動の押し話しと比較できる

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
2. 中央（送信）上部の **Setup** ヘッダに、`model` / `voice` / `responseModalities` などの設定行が出ることを確認してください（接続前から表示され、接続後に Setup JSON が続きます）。

### 2. Space で話してみる（手動モード）

1. **Space を押したまま**短い文を話し、**離してください**。
2. 段階バーが `Send PCM` → `Receive PCM` → `Play` と進むこと、中央に送信チャンクログ、右に受信チャンクログが増えることを見てください。
3. 左に吹き出し（transcription）が出て、返答が声で再生されることを確認してください。

初期状態は **手動モード** です。Setup ヘッダの `VAD: manual` と、送信ログの `activityStart` / `activityEnd` を確認してください。

### 3. VAD 自動モードに切り替える

1. 左ペインの **VAD 自動** ボタンを押してください（再接続のあと、ボタンが **VAD ON** になります）。
2. Setup ヘッダの `VAD:` が `auto` になり、`automaticActivityDetection.disabled` が `false` になっていることを確認してください。
3. **Space を使わず**話し、少し黙るとサーバが区切って返答することを聞いてください。
4. もう一度ボタンを押すと手動モードに戻り、Space 押し話しが再び有効になります。

VAD は Setup 時の設定のため、切替のたびに Live セッションを張り直します。自動モードの説明 → [Live API: Automatic VAD](https://ai.google.dev/gemini-api/docs/live-api/capabilities#automatic-vad)

### 4. 声を変えてみる

1. Play を止め、Hierarchy でデモ本体（`SpeechToSpeechLiveAPI`）を選び、Inspector の **Voice Name**（`voiceName`）を変更してください（初期値は `Kore`）。
2. もう一度 Play を押し、中央 Setup ヘッダの `voice` が新しい名前になっていることを確認してから、Space で話してください。

声は接続時の Setup で一度だけ送るため、変更後は **Stop → Play** が必要です。設定の書き方 → [Live API: Change voice and language](https://ai.google.dev/gemini-api/docs/live-api/capabilities#change-voice-and-language)  
使える声の名前一覧 → [Gemini API: Text-to-speech（Voice options）](https://ai.google.dev/gemini-api/docs/speech-generation#voices)

教材デモでは APIキーをクライアントから直接使います。本番アプリでは ephemeral token などの短い資格情報を使うことが推奨されます。

---

## Live API（セッション）とは？

Live API とは、HTTP の `generateContent` を何回も呼ぶのではなく、**WebSocket で1本のセッションを張り、音声をチャンクで双方向に流す**仕組みです。

このデモでは Play 開始時に接続と Setup を行い、そのあと PCM を `realtimeInput` で送り、サーバからの PCM と transcription を受け取ります。声色は Setup の `speechConfig`（`voiceName`）で一度指定します。3A のように「文字起こし用」「チャット用」「TTS 用」とリクエストを分けません。

試し方: 中央 Setup ヘッダと、Space 中に増える送信ログ、返答時の受信ログを見比べる。Inspector で声を変えて聞き比べる（手順は「動かし方」の節。変更後は Stop → Play）。

---

## VAD（Voice Activity Detection）とは？

VAD とは、音声の流れのなかから「話している区間」を見つける処理です。このデモの **VAD 自動モード** では、Live API の `automaticActivityDetection` がサーバ側で無音を見てターンを区切ります。クライアントはマイク PCM を流し続けるだけで、`activityStart` / `activityEnd` は送りません。

**手動モード**（初期）では自動 VAD をオフにし、Space の押し／離しで `activityStart` / `activityEnd` を自分で送ります。どちらも同じ Live セッションの見た目ですが、ターン境界を誰が決めるかが違います。

試し方: ボタンでモードを切り替え、Setup ヘッダの `VAD:` 行と、送信ログに `activityStart` が出るかどうかを比べる。

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

通信は **ClientWebSocket**（双方向のソケット）です。受信はバックグラウンドのループで行い、UI や `AudioSource` の更新だけメインスレッドのキュー経由で戻します。Space の押し話し検知は `Update` と **旧 Input Manager**（`Input.GetKeyDown` / `GetKeyUp`）です。VAD 自動中は Space を無視します。

1. **Live セッションに接続する**  
   `ConnectLiveSessionCoroutine` — WSS 接続 → Setup JSON 送信 → `setupComplete` 待ち
2. **手動 / VAD 自動を切り替える**  
   `OnVadModeButtonClicked` → `ReconnectForVadModeCoroutine` — Setup の VAD 設定を載せ替えるため再接続
3. **Space 押し話しを検知する（手動のみ）**  
   `UpdatePushToTalk` — 押しているあいだ録音送信、離したら `activityEnd`
4. **VAD 自動でマイク常時送信する**  
   `BeginContinuousListening` — `activityStart` なしで PCM を流し、サーバ無音判定に任せる
5. **PCM チャンクを送る**  
   `PumpMicrophoneChunksIfStreaming` — マイク差分 → 16-bit PCM → Base64 → `realtimeInput.audio`
6. **サーバメッセージを振り分ける**  
   `HandleServerMessage` — 音声は再生キュー、transcription はログと吹き出し
7. **受信 PCM を再生する**  
   `PlaybackPumpCoroutine` — キューから `AudioClip` 化して `AudioSource.Play`

### ChatBubble（[`ChatBubble.cs`](../Common/Script/ChatBubble.cs)）

左ペインの吹き出し1件分です（Prefab: [`Prefab/MessageBubble.prefab`](Prefab/MessageBubble.prefab)）。見た目用のクラスで、`Assets/Common/Script/` のものを使います。通信ロジックは持ちません。文面は Live の transcription 由来です。
