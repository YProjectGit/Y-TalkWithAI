# 3B. SpeechToSpeechLiveAPI

![speech-to-speech-live-api](../Docs/Image/speech-to-speech-live-api.png)

<br/>

声のやり取りを、Gemini Live APIの**ひとつのセッション**で行うアプリケーションです。

3Aのように「文字起こし用」「チャット用」「TTS用」とリクエストを分けず、WebSocketで音声を双方向に流します。送っては待つ形ではなくなり、人と話すのに近いテンポになります。

学習のため、接続時のSetupと、送受信される音声チャンクが見えるようになっています。

<br/>

---

## このデモで学ぶこと

<br/>

- ### Live API

  WebSocketで双方向のセッションを張り、音声をやり取りする方法を学びます。

- ### ストリーミング

  まとまったデータを一度に送らず、小さく分けて流し続ける方法を学びます。

- ### VAD（Voice Activity Detection）

  音の流れから、話している区間を自動で見つける方法を学びます。

<br/>

---

## 事前準備

<br/>

### マイクの準備

- PCにマイクを接続し、音声が入力できる状態にしてください。
- OSの設定でマイクの使用が許可されていることを確認してください。許可がないと、録音しても無音のデータが送られます。

<br/>

### スピーカーの準備

- スピーカーまたはヘッドホンで、再生音が聞こえる状態にしてください。

<br/>

---

## 動かしてみる

<br/>

Project ウィンドウで `Assets/3B.SpeechToSpeechLiveAPI/SpeechToSpeechLiveAPI.unity` を開き、Playしてください。

### 1. 接続を確認する

1. 中央ペイン上部の **Setup** に、`model` / `voice` / `responseModalities` などの設定行が出ることを確認してください。
2. 接続後、SetupのJSONが続いていることを見てください。
3. 初期状態は **手動モード** です。Setupヘッダの `VAD: manual` を確認してください。

### 2. Spaceを押して話す（手動モード）

1. 左ペインのボリュームゲージが、自分の声に合わせて動くことを確認してください。
2. **Spaceキーを押したまま**短い文を話し、話し終えたらキーを**離して**ください。
3. 中央に送信チャンク、右に受信チャンクが増えること、左に吹き出し（transcription）が出て、返答が声で再生されることを確認してください。
4. 送信ログに `activityStart` / `activityEnd` が出ていることを見てください。手動モードでは、Spaceの押し離しが発話の区切りです。

### 3. VAD自動モードに切り替える

1. 左ペインの **VAD 自動** ボタンを押してください（再接続のあと、ボタンが **VAD ON** になります）。
2. Setupヘッダの `VAD:` が `auto` になり、`automaticActivityDetection.disabled` が `false` になっていることを確認してください。
3. スピーカーを使うときは、ボタン右の **再生中マイクOFF** をオンにしてください（再生中はマイク送信を止めます。ヘッドホンだけならオフでも構いません）。
4. **Spaceを使わず**話し、少し黙るとサーバが区切って返答することを聞いてください。
5. もう一度ボタンを押すと手動モードに戻り、Space押し話しが再び有効になります。

声を変えたいときは、Playを止めてInspectorの **Voice Name**（`voiceName`）を変更し、もう一度Playしてください。声は接続時のSetupで一度だけ送るため、Play中の変更は次の接続まで反映されません。使える声の名前は [Gemini API: Voice options](https://ai.google.dev/gemini-api/docs/speech-generation#voices) を参照してください。

<br/>

---

## 前提知識

<br/>

### WebSocket

-　これまでのデモは **HTTP** のリクエスト／レスポンスでした。クライアントが送り、サーバが答え、そこで1回の通信が終わります。

-　**WebSocket** は、一度つなぐと双方向にメッセージを流し続けられる通信の仕組みです。チャットや通話のように、どちらからでも、何度でも送れます。このデモの接続先は次のURLです（キーはクエリに付けます）。

`wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent`

-　`wss` は、WebSocketを暗号化したものです。HTTPの `https` に相当します。

<br/>

### PCM

-　**PCM** とは、音を時刻ごとの振幅の数字列として表した音声データです。このデモの送信は16 kHz、受信はおおむね24 kHzの16-bit PCMです。

-　3Aでは録音が終わってからWAVファイルとしてまとめて送りました。ここでは、録音中から小さな塊（チャンク）に分けて送ります。

<br/>

---

## Live API

<br/>

**Live API** とは、HTTPの `generateContent` を何回も呼ぶのではなく、**WebSocketで1本のセッションを張り、音声をチャンクで双方向に流す**仕組みです。

このデモではPlay開始時に接続とSetupを行い、そのあとPCMを `realtimeInput` で送り、サーバからのPCMとtranscription（文字起こし）を受け取ります。声色はSetupの `speechConfig`（`voiceName`）で一度指定します。

3Aのように「文字起こし用」「チャット用」「TTS用」とリクエストを分けません。見える化も、発生順1〜6ではなく、**送信列**と**受信列**です。

<br/>

**接続時に送るSetupのJSON（手動モード）**

```json
{
  "setup": {
    "model": "models/gemini-3.1-flash-live-preview",
    "realtimeInputConfig": {
      "automaticActivityDetection": {
        "disabled": true
      }
    },
    "inputAudioTranscription": {},
    "outputAudioTranscription": {},
    "generationConfig": {
      "responseModalities": ["AUDIO"],
      "speechConfig": {
        "voiceConfig": {
          "prebuiltVoiceConfig": {
            "voiceName": "Kore"
          }
        }
      }
    }
  }
}
```

1. **`setup`**  
   セッション開始時に一度だけ送る設定です。サーバが `setupComplete` を返すと、音声のやり取りが始められます。
2. **`automaticActivityDetection.disabled`**  
   `true` が手動モード（Spaceで区切る）、`false` がVAD自動モードです。
3. **`inputAudioTranscription` / `outputAudioTranscription`**  
   入出力の文字起こしをオンにします。吹き出しの文面は、ここから来ます。
4. **`responseModalities`**  
   返答を音声にする指定です。3AのTTSリクエストと同じ `AUDIO` です。
5. **`speechConfig`**  
   返答の声色です。接続時に一度だけ送るので、変えたらStop → Playが必要です。

<br/>

---

## ストリーミング

<br/>

**ストリーミング** とは、データを全部そろえてから一度に送るのではなく、できた分から小さく分けて流し続けることです。動画の配信と同じ発想です。

このデモでは、Spaceを押しているあいだ（VAD自動なら常時）、マイクのPCMをチャンクにして送り続けます。サーバからの返答音声も、まとまったファイルではなくチャンクで届き、届いた順に再生キューへ入れます。

<br/>

**送信する音声チャンクのJSON**

```json
{
  "realtimeInput": {
    "audio": {
      "mimeType": "audio/pcm;rate=16000",
      "data": "..."
    }
  }
}
```

1. **`realtimeInput`**  
   セッション中に流す入力です。Setupのあとに何度でも送れます。
2. **`mimeType`**  
   WAVではなく、ヘッダのないPCMです。サンプルレートもここに含めます。
3. **`data`**  
   そのチャンクのバイト列をBase64にしたものです。

<br/>

手動モードでは、送り始めと送り終わりを次のJSONで伝えます。

```json
{"realtimeInput":{"activityStart":{}}}
```

```json
{"realtimeInput":{"activityEnd":{}}}
```

中央の送信ログの `+chunk` が、流れているチャンクの要約です。右の受信ログの `+audio` が、返ってきた音声チャンクです。

<br/>

---

## VAD（Voice Activity Detection）

<br/>

**VAD** とは、音声の流れのなかから「話している区間」を見つける処理です。沈黙と発話の境目を、人がボタンで教えるか、機械が見つけるかの違いです。

<br/>

**手動モード**（初期）では、自動VADをオフにし、Spaceの押し／離しで `activityStart` / `activityEnd` を自分で送ります。

**VAD自動モード**では、Live APIの `automaticActivityDetection` がサーバ側で無音を見てターンを区切ります。クライアントはマイクPCMを流し続けるだけで、`activityStart` / `activityEnd` は送りません。

どちらも同じLiveセッションの見た目ですが、ターン境界を誰が決めるかが違います。VADはSetup時の設定のため、切替のたびにセッションを張り直します。

無音の切れ方は、Inspectorの **Silence Duration Ms**（無音が何ミリ秒続いたら発話終了とみなす）と **End Of Speech Sensitivity**（切れやすさ）で変えられます。Setup時の設定なので、変えたらStop → Play、またはVADボタンで再接続してください。

<br/>

---

## コードの解説

<br/>

### SpeechToSpeechLiveAPI（[`SpeechToSpeechLiveAPI.cs`](Script/SpeechToSpeechLiveAPI.cs)）

<br/>

デモの本体です。上から、接続から再生までの流れを追うとわかりやすいです。

通信は **ClientWebSocket**（双方向のソケット）です。受信はバックグラウンドのループで行い、UIや `AudioSource` の更新だけメインスレッドのキュー経由で戻します。Spaceキーの押し離しの検知は `Update` の中で、**旧 Input Manager**（`Input.GetKeyDown` / `GetKeyUp`）を使っています。VAD自動中はSpaceを無視します。

<br/>

1. **Liveセッションに接続する**  
   `ConnectLiveSessionCoroutine` — WSS接続 → `BuildSetupJson` でSetupを送信 → `setupComplete` を待つ
   <br/>
2. **手動 / VAD自動を切り替える**  
   `OnVadModeButtonClicked` → `ReconnectForVadModeCoroutine` — SetupのVAD設定を載せ替えるため再接続する
   <br/>
3. **Spaceキーの押し離しを見る（手動のみ）**  
   `UpdatePushToTalk` — 押した瞬間に `activityStart`、離した瞬間に `activityEnd`
   <br/>
4. **VAD自動でマイクを常時送信する**  
   `BeginContinuousListening` — `activityStart` なしでPCMを流し、サーバの無音判定に任せる。**再生中マイクOFF** がオンなら、再生中は送信を止める
   <br/>
5. **PCMチャンクを送る**  
   `PumpMicrophoneChunksIfStreaming` — マイクの差分 → 16-bit PCM → Base64 → `realtimeInput.audio`
   <br/>
6. **サーバメッセージを振り分ける**  
   `HandleServerMessage` — 音声は再生キューへ、transcriptionの断片が来たら吹き出しと右欄をその場で更新する
   <br/>
7. **受信PCMを再生する**  
   `PlaybackPumpCoroutine` — キューから `AudioClip` 化して `AudioSource.Play`

<br/>

### 共通ライブラリ（`Assets/Common/Script/`）

<br/>

このデモが使っている共通のライブラリです。シンプルなユーティリティクラスなので、**上の流れを追うときに中身を読む必要はありません。**

| ファイル | 中身 |
|---|---|
| [`GeminiJson`](../Common/Script/GeminiJson.cs) | JSONのエスケープ・整形・省略表示 |
| [`GeminiJsonScan`](../Common/Script/GeminiJsonScan.cs) | Live APIの受信JSONからキーを頼りに文字列を拾う |
| [`GeminiKey`](../Common/Script/GeminiKey.cs) | APIキーの読込・マスク |
| [`AudioCodec`](../Common/Script/AudioCodec.cs) | AudioClip ⇄ WAV / PCM16 の変換 |
| [`MicLevel`](../Common/Script/MicLevel.cs) | マイクの直近の音の大きさを0〜1にする |
| [`ChatBubble`](../Common/Script/ChatBubble.cs) | 吹き出し1件分の見た目（Prefab: [`MessageBubble.prefab`](Prefab/MessageBubble.prefab)） |
| [`ResponseTime`](../Common/Script/ResponseTime.cs) | 送信から返信までの経過時間をConsoleへ表示 |

これらは他のデモも使っています。挙動を変えたくなったらCommonを直さず、そのファイルをこのデモの `Script/` にコピーしてクラス名を変えてください。
