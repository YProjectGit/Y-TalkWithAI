# 3A.SpeechToSpeech

シリーズ全体の位置づけ → [Docs/demo-series-overview.md](../../Docs/demo-series-overview.md)

---

## このデモで学べること

- **TTS（Text-to-Speech）**  
  テキストを音声データにして再生する
- **声の往復（段階的）**  
  マイク → 文字起こし → 返答テキスト → 読み上げ、を段に分けてつなぐ
- **REST での音声パイプライン**  
  声の入口と出口を、HTTP のリクエストの連なりとして組む

---

## 事前準備

1. Google AI Studio から Gemini の API にアクセスするための APIキーを取得し、`Assets/Common/APIKey.txt` に保管してください。  
   手順 → [Docs/gemini-ai-studio-setup.md](../../Docs/gemini-ai-studio-setup.md)
2. PC にマイクがつながり、Unity から使える状態にしてください（OS のマイク権限を含む）。
3. スピーカーまたはヘッドホンで再生音が聞こえる状態にしてください。

---

## 動かし方

Project ウィンドウで `Assets/3A.SpeechToSpeech/SpeechToSpeech.unity` を開き、Play を押してください。

### 1. Space で話してみる

1. **Space を押したまま**短い文を話し、**離してください**。
2. Status が「録音中」→「1. Request」→「3. Request」→「5. Request」→「再生中」と進むことを見てください。
3. 左に吹き出しが出たあと、Gemini の返答が声で再生されることを確認してください。

### 2. 発生順（1〜6）で Request / Response を追う

どれも Gemini の `generateContent` です。番号は呼ばれた順番です。

1. **1. Request - GenerateContent（Audio）** … 音声（`inlineData` / `audio/wav`）を送る  
2. **2. Response - GenerateContent（Audio）** … 文字起こしが返る  
3. **3. Request - GenerateContent（Text）** … 認識テキストを会話として送る  
4. **4. Response - GenerateContent（Text）** … チャットの返答テキストが返る  
5. **5. Request - GenerateContent（TTS）** … その返答テキストを TTS モデルへ送る  
6. **6. Response - GenerateContent（TTS）** … 音声バイトが返り、再生に使う  

5 の欄の先頭に `ttsModel` / `voice` / `responseModalities` の設定行が出ること、本文に `responseModalities` に `AUDIO` があること、6 の欄では MIME とバイト数など要約だけが出ることを確認してください（音声本体は再生に回します）。

### 3. 声を変えてみる

1. Hierarchy でデモ本体（`SpeechToSpeech`）を選び、Inspector の **Tts Voice Name**（`ttsVoiceName`）を変更してください（初期値は `Kore`）。
2. Play し直し、**5. Request** 欄先頭の `voice:` が新しい名前になっていることを確認してから、Space で話してください（次の TTS リクエストから反映されます）。

使える声の名前一覧 → [Gemini API: Text-to-speech（Voice options）](https://ai.google.dev/gemini-api/docs/speech-generation#voices)

---

## TTS（Text-to-Speech）とは？

テキストを音声データに変換することです。このデモでは、Chat（3→4）で得た返答文を、**別の TTS 向けモデル**の `generateContent` に渡し、`responseModalities: ["AUDIO"]` で音声バイトを受け取ります。声色はリクエスト内の `speechConfig`（`ttsVoiceName`）で指定します。

2A までは「声 → 文字 → 文字の返答」で終わりました。3A ではその返答をもう一度 API に渡し、「文字 → 声」にしてスピーカーで再生します。画面の 1→2→3→4→5→6 がその順番です。

試し方: 左の Gemini 吹き出しの文を聞いた声と照らし合わせる。5. Request の本文に同じ文が載っているかを見る。Inspector で声を変えて聞き比べる（手順は「動かし方」の節）。

---

## マイク入力と音声データとは？

マイク入力とは、PC のマイクが拾った音を、プログラムが扱える数字の列として取り込むことです。入口側（1→2）の変換は `2A.SpeechToText` と同じです。

```text
Microphone → AudioClip → WAV → Base64 → 1. Request（Audio）
```

出口側（5→6）では逆方向に近いことが起きます。API から来た PCM（または WAV）を `AudioClip` にして `AudioSource` で再生します。

試し方: Status に「再生中」が出るタイミングと、6. Response の `mimeType` / `audio bytes` を見比べる。

---

## 主要クラス

### SpeechToSpeech（[`SpeechToSpeech.cs`](Script/SpeechToSpeech.cs)）

デモの本体です。上から、録音〜STT〜Chat〜TTS〜再生の流れを追うとわかりやすいです。

通信は **UnityWebRequest**（HTTP の送受信）と **コルーチン**（`IEnumerator` + `yield`）による **非同期処理** です。コルーチンは `Update` などのメインスレッドの処理とは独立した時間軸で進むので、応答待ちのあいだも画面が固まりません。Space の押し話し検知だけは `Update` で、**旧 Input Manager**（`Input.GetKeyDown` / `GetKeyUp`）を使います。

1. **起動時の準備をする**  
   `Start` — APIキー読込、`SystemInstruction.txt` → UI、マイク確認、`AudioSource` 確保、録音案内の表示
2. **Space 押し話しを検知する**  
   `UpdatePushToTalk` — 押しているあいだ録音、離したら変換へ（System Instruction 編集中は録音しない）
3. **マイクで録音する**  
   `BeginRecording` / `EndRecordingAndSend` — `Microphone.Start` → `End` → 録れたサンプルだけ切り出し
4. **音声データに変換する**  
   `ConvertAudioClipToWav` — float サンプルを 16-bit PCM にし、WAV ヘッダを付けて `byte[]` にする → Base64
5. **1→2. GenerateContent（Audio）**  
   `SendSpeechPipelineCoroutine` の前半 — 音声付き JSON を POST し、文字起こしを取り出す
6. **3→4. GenerateContent（Text）**  
   同コルーチンの中盤 — 認識テキストと会話履歴を POST し、返答を吹き出しへ
7. **5→6. GenerateContent（TTS）→ 再生**  
   同コルーチンの後半 — 返答テキストを TTS モデルへ POST し、PCM/WAV を `AudioClip` 化して `AudioSource.Play`

### ChatBubble（[`ChatBubble.cs`](../Common/Script/ChatBubble.cs)）

左ペインの吹き出し1件分です（Prefab: [`Prefab/MessageBubble.prefab`](Prefab/MessageBubble.prefab)）。見た目用のクラスで、`Assets/Common/Script/` のものを使います。通信ロジックは持ちません。
