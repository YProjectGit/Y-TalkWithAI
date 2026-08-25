# 3B.SpeechToSpeechLiveAPI

声のやり取りを、Gemini Live API のひとつのセッションで行います。送っては待つ形ではなくなり、人と話すのに近いテンポになります。

シリーズ全体の位置づけ → [Assets/Docs/demo-series-overview.md](../Docs/demo-series-overview.md)

---

## このデモで学べること

- **Live API**  
  WebSocket で双方向のセッションを張り、音声をやり取りする
- **ストリーミング**  
  まとまったデータを一度に送らず、小さく分けて流し続ける
- **VAD（Voice Activity Detection）**  
  音の流れから、話している区間を自動で見つける

---

## 事前準備

1. Google AI Studio から Gemini の API にアクセスするための APIキーを取得し、`Assets/Common/APIKey.txt` に保管してください。  
   手順 → [Assets/Docs/gemini-ai-studio-setup.md](../Docs/gemini-ai-studio-setup.md)
2. PC にマイクがつながり、Unity から使える状態にしてください（OS のマイク権限を含む）。
3. スピーカーまたはヘッドホンで再生音が聞こえる状態にしてください。

---

## 動かし方

Project ウィンドウで `Assets/3B.SpeechToSpeechLiveAPI/SpeechToSpeechLiveAPI.unity` を開き、Play を押してください。

### 1. 接続を確認する

1. 上部の段階バーが `Connect` 付近であること、左 Status が「接続済み」になることを見てください。
2. 中央（送信）上部の **Setup** ヘッダに、`model` / `voice` / `responseModalities` などの設定行が出ることを確認してください（接続前から表示され、接続後に Setup JSON が続きます）。

### 2. Space で話してみる（手動モード）

1. **Space を押したまま**短い文を話し、**離してください**。左の横棒が声に合わせて伸びます。
2. 段階バーが `Send PCM` → `Receive PCM` → `Play` と進むこと、中央に送信チャンクログ、右に受信チャンクログが増えることを見てください。
3. 左に吹き出し（transcription）が出て、返答が声で再生されることを確認してください。

初期状態は **手動モード** です。Setup ヘッダの `VAD: manual` と、送信ログの `activityStart` / `activityEnd` を確認してください。

### 3. VAD 自動モードに切り替える

1. 左ペインの **VAD 自動** ボタンを押してください（再接続のあと、ボタンが **VAD ON** になります）。
2. Setup ヘッダの `VAD:` が `auto` になり、`automaticActivityDetection.disabled` が `false` になっていることを確認してください。
3. スピーカーを使うときは、ボタン右の **再生中マイクOFF** をオンにしてください（再生中はマイク送信を止めます。ヘッドホンだけならオフでも構いません）。
4. **Space を使わず**話し、少し黙るとサーバが区切って返答することを聞いてください。
5. もう一度ボタンを押すと手動モードに戻り、Space 押し話しが再び有効になります。

無音の切れ方は Inspector の **Silence Duration Ms** と **End Of Speech Sensitivity** で変えられます。意味は下の「設定項目」を見てください。Setup 時の設定のため、変更後は **Stop → Play**、または VAD ボタンで再接続してください。

VAD は Setup 時の設定のため、切替のたびに Live セッションを張り直します。自動モードの説明 → [Live API: Automatic VAD](https://ai.google.dev/gemini-api/docs/live-api/capabilities#automatic-vad)

### 4. 声を変えてみる

1. Play を止め、Hierarchy でデモ本体（`SpeechToSpeechLiveAPI`）を選び、Inspector の **Voice Name**（`voiceName`）を変更してください（初期値は `Kore`）。
2. もう一度 Play を押し、中央 Setup ヘッダの `voice` が新しい名前になっていることを確認してから、Space で話してください。

声は接続時の Setup で一度だけ送るため、変更後は **Stop → Play** が必要です。設定の書き方 → [Live API: Change voice and language](https://ai.google.dev/gemini-api/docs/live-api/capabilities#change-voice-and-language)  
使える声の名前一覧 → [Gemini API: Text-to-speech（Voice options）](https://ai.google.dev/gemini-api/docs/speech-generation#voices)

---

## Live API（セッション）

Live API とは、HTTP の `generateContent` を何回も呼ぶのではなく、**WebSocket で1本のセッションを張り、音声をチャンクで双方向に流す**仕組みです。

このデモでは Play 開始時に接続と Setup を行い、そのあと PCM を `realtimeInput` で送り、サーバからの PCM と transcription を受け取ります。声色は Setup の `speechConfig`（`voiceName`）で一度指定します。3A のように「文字起こし用」「チャット用」「TTS（Text-to-Speech）用」とリクエストを分けません。

試し方: 中央 Setup ヘッダと、Space 中に増える送信ログ、返答時の受信ログを見比べる。Inspector で声を変えて聞き比べる（手順は「動かし方」の節。変更後は Stop → Play）。

---

## VAD（Voice Activity Detection）

VAD とは、音声の流れのなかから「話している区間」を見つける処理です。このデモの **VAD 自動モード** では、Live API の `automaticActivityDetection` がサーバ側で無音を見てターンを区切ります。クライアントはマイク PCM を流し続けるだけで、`activityStart` / `activityEnd` は送りません。

**手動モード**（初期）では自動 VAD をオフにし、Space の押し／離しで `activityStart` / `activityEnd` を自分で送ります。どちらも同じ Live セッションの見た目ですが、ターン境界を誰が決めるかが違います。

試し方: ボタンでモードを切り替え、Setup ヘッダの `VAD:` 行と、送信ログに `activityStart` が出るかどうかを比べる。自動モードでは Inspector の無音 2 項目を変えて、間の取り方の違いを聞く。

---

## 設定項目

このデモがいま使っている項目です。Setup に載るものは接続時に一度だけ送るので、変えたら **Stop → Play**（VAD はボタンでも再接続）してください。

設定ガイド → [Live API capabilities](https://ai.google.dev/gemini-api/docs/live-api/capabilities)

| 項目 | 指定 | 意味 |
|---|---|---|
| `model` | Inspector（`modelName`） | 使う Live モデル。初期値は `gemini-3.1-flash-live-preview` |
| `systemInstruction` | 左ペイン／`SystemInstruction.txt` | 事前指示。空なら Setup に載せない |
| `voiceName` | Inspector | 返答の声色。初期値は `Kore` |
| `responseModalities` | 固定 `AUDIO` | 返答を音声にする |
| `inputAudioTranscription` / `outputAudioTranscription` | 固定（空オブジェクト） | 入出力の文字起こしをオンにする |
| `automaticActivityDetection.disabled` | **VAD 自動** ボタン | `false`=サーバが無音で区切る。`true`=Space で `activityStart` / `activityEnd` |
| `silenceDurationMs` | Inspector（`0`=載せない） | 無音が何ミリ秒続いたら発話終了とみなす。大きいほど間を許し、小さいほどすぐ返答する |
| `endOfSpeechSensitivity` | Inspector（`Server Default`=載せない） | 発話終了の切れやすさ。`High`=切れやすい（API 既定）。`Low`=切れにくい |
| `sampleRate` | Inspector | 送信 PCM のサンプルレート（Hz）。初期値は `16000` |
| `playbackSampleRate` | Inspector | 受信 PCM の想定レート（Hz）。初期値は `24000`。サーバの mime があればそちらを使う |
| `maxRecordingSeconds` / `minRecordingSeconds` | Inspector | 手動モードの録音上限と、これより短い発話は送らない下限 |
| `micResumeDelaySeconds` | Inspector | 再生が終わってから、マイク送信を再開するまでの待ち（秒） |
| 再生中マイクOFF | 画面のトグル | VAD 自動時、再生中はマイク送信を止める（Setup には載せない） |

---

## 3A（REST 三段）との違い

| | 3A | 3B（このデモ） |
|---|----|----------------|
| 通信 | `generateContent` ×3 | WebSocket Live ×1 |
| 見える化 | 発生順 1〜6 | 送信列 / 受信列 |
| 文字 | Chat の text | transcription |

体験（声で入って声で返る）は近いですが、追いどころが「何段の REST か」から「セッションの寿命と PCM の流れ」へ移ります。

試し方: 同じ発話内容を 3A と 3B で試し、画面の欄の種類の違いを見る。

---

## PCM ストリームと再生バッファ

PCM とは、音を時刻ごとの振幅の数字列として表した音声データです。このデモの送信は 16 kHz、受信はおおむね 24 kHz の 16-bit PCM です。

ソケットには一度に全部ではなく、小さなチャンクが連続して流れます。右の受信ログがその要約で、再生側ではチャンクをキューに入れてから `AudioSource` で順に鳴らします。

試し方: Space 中に中央ログの `+chunk` が増えること、返答中に右ログの `+audio` と「再生中」を見比べる。

---

## 主要クラス

### SpeechToSpeechLiveAPI（[`SpeechToSpeechLiveAPI.cs`](Script/SpeechToSpeechLiveAPI.cs)）

デモの本体です。上から、接続〜送信〜受信〜再生の順に追うとわかりやすいです。

通信は **ClientWebSocket**（双方向のソケット）です。受信はバックグラウンドのループで行い、UI や `AudioSource` の更新だけメインスレッドのキュー経由で戻します。Space の押し話し検知は `Update` と **旧 Input Manager**（`Input.GetKeyDown` / `GetKeyUp`）です。VAD 自動中は Space を無視します。

1. **Live セッションに接続する**  
   `ConnectLiveSessionCoroutine` — WSS（WebSocket Secure）接続 → Setup JSON 送信 → `setupComplete` 待ち
2. **手動 / VAD 自動を切り替える**  
   `OnVadModeButtonClicked` → `ReconnectForVadModeCoroutine` — Setup の VAD 設定を載せ替えるため再接続
3. **Space 押し話しを検知する（手動のみ）**  
   `UpdatePushToTalk` — 押しているあいだ録音送信、離したら `activityEnd`
4. **VAD 自動でマイク常時送信する**  
   `BeginContinuousListening` — `activityStart` なしで PCM を流し、サーバ無音判定に任せる。**再生中マイクOFF** がオンなら、再生中は送信を止める
5. **PCM チャンクを送る**  
   `PumpMicrophoneChunksIfStreaming` — マイク差分 → 16-bit PCM → Base64 → `realtimeInput.audio`
6. **サーバメッセージを振り分ける**  
   `HandleServerMessage` — 音声は再生キュー、transcription 断片が来たら吹き出しと右欄をその場で更新
7. **受信 PCM を再生する**  
   `PlaybackPumpCoroutine` — キューから `AudioClip` 化して `AudioSource.Play`

### 共通スクリプト（`Assets/Common/Script/`）

このデモが使っている共通の道具です。**上の流れを追うときに中身を読む必要はありません。**

| ファイル | 中身 |
|---|---|
| [`GeminiJson`](../Common/Script/GeminiJson.cs) | JSON のエスケープ・整形・省略表示 |
| [`GeminiJsonScan`](../Common/Script/GeminiJsonScan.cs) | Live API の受信 JSON からキーを頼りに文字列を拾う |
| [`GeminiKey`](../Common/Script/GeminiKey.cs) | APIキーの読込・マスク・generateContent の URL |
| [`AudioCodec`](../Common/Script/AudioCodec.cs) | AudioClip ⇄ WAV / PCM16 の変換 |
| [`MicLevel`](../Common/Script/MicLevel.cs) | マイク直近窓の RMS → 横棒の 0〜1 |
| [`ChatBubble`](../Common/Script/ChatBubble.cs) | 吹き出し1件分の見た目（Prefab: [`MessageBubble.prefab`](Prefab/MessageBubble.prefab)） |

これらは他のデモも使っています。挙動を変えたくなったら Common を直さず、そのファイルをこのデモの `Script/` にコピーしてクラス名を変えてください。
