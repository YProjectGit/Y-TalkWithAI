# 3A. SpeechToSpeech

![speech-to-speech](../Docs/Image/speech-to-speech.png)

<br/>

音声で話し、その返事も音声で受け取るAIチャットのアプリケーションです。

上図の①②までは[2Aのサンプル](../2A.SpeechToText/README.md)と同様ですが、そこからさらに、③ **TTS（Text-to-Speech）**でテキストを音声にして再生します。

学習のため、STT・チャット・TTS の3回分の通信が見えるようになっています。

<br/>

---

## 学ぶこと

<br/>

- ### TTS（Text-to-Speech）

  テキストを音声データに変換します。

- ### responseModalities

  レスポンスをテキストではなく音声で返すよう、リクエストの中で指定する方法を学びます。

- ### speechConfig

  読み上げに使う声を指定し、トーンや速度は読み上げさせる文章で指示する方法を学びます。

<br/>

---

## 事前準備

<br/>

### スピーカーの準備

- スピーカーまたはヘッドホンで、再生音が聞こえる状態にしてください。

<br/>

---

## 動かしてみる

<br/>

Project ウィンドウで `Assets/3A.SpeechToSpeech/SpeechToSpeech.unity` を開き、Playしてください。

### 1. Spaceを押して話す

1. 左ペインのMessage欄の下にあるボリュームゲージが、自分の声に合わせて動くことを確認してください。
2. **Spaceキーを押したまま**短い文を話し、話し終えたらキーを**離して**ください。
3. 左ペインに吹き出しが出たあと、Geminiの返答が声で再生されることを確認してください。

### 2. 3回の通信を順番に追う

1. **1. Request** で音声を送り、**2. Response** で文字起こしが返っていることを確認してください。
2. **3. Request** で、文字起こしされたテキストがチャットのメッセージとして送られていることを確認してください。
3. **4. Response** で、チャットの返答テキストが返っていることを確認してください。
4. **5. Request** を見て、`responseModalities` に `AUDIO` が入っていること、先頭に `ttsModel` / `voice` が出ていることを確認してください。
5. **6. Response** には MIME とバイト数の要約だけが出ます。音声本体は再生に回すため、ここに載せていません。

### 3. 声を変えてみる

1. Hierarchyでデモ本体（`SpeechToSpeech`）を選び、Inspectorの **Tts Voice Name**（`ttsVoiceName`）を変更してください（初期値は `Kore`）。
2. 使える声の名前は [Gemini API: Voice options](https://ai.google.dev/gemini-api/docs/speech-generation#voices) を参照してください。

<br/>

---

## 解説

<br/>

### マイク入力と音声データ

- 入口側（1→2）の変換は [2A.SpeechToText](../2A.SpeechToText/README.md) と同じです。マイクの音をAudioClipへ書き込み、WAVにしてBase64で送ります。

- 出口側（5→6）では逆向きの変換が起きます。APIから来たPCM（またはWAV）をAudioClipにし、`AudioSource` で再生します。

```text
マイク入力 → AudioClip → WAV → Base64 
→ 1. Request（STT）

2. Response（文字起こし）
→ 3. Request（Chat）→ 4. Response（返答テキスト）

5. Request（TTS）
→ 6. Response（音声バイト）
→ AudioClip → AudioSource で再生
```

<br/>

---

## TTS（Text-to-Speech）

<br/>

- **TTS (Text To Speech)** とは、テキストを音声データへ変換することです。「音声合成」と呼ばれます。

- 2Aまでは「声 → 文字 → 文字の返答」で終わりました。このデモでは、チャットで得た返答文を**別のTTS向けモデル**へ渡し、「文字 → 声」にしてスピーカーで再生します。

- STTとChatはこれまでと同じ `generateContent` です。（TTSだけ、モデル名が `gemini-3.1-flash-tts-preview` に変わります）

- 1回の発話につき通信は3回です。画面の番号1〜6が、その順番に対応しています。

| 番号 | 内容 |
|---|---|
| **1. Request** | 音声（`inlineData` / `audio/wav`）を送る |
| **2. Response** | 文字起こしされたテキストが返る |
| **3. Request** | そのテキストを、チャットのメッセージとして送信する |
| **4. Response** | チャットの返答テキストが返る |
| **5. Request** | その返答テキストを、TTSモデルへ送る |
| **6. Response** | 音声バイトが返り、再生に使う |

<br/>

3往復の通信を介しているので、インタラクションとしてかなり遅いと感じると思います。

（この問題は、次の[3Bのデモ](../3B.SpeechToSpeechLiveAPI/README.md)で解消します）

<br/>

---

## responseModalities

<br/>

**responseModalities** とは、AIからのレスポンスをどの種類で受け取りたいかを指定する設定です。指定しないとテキストが返ります。このデモのTTSリクエストでは、音声を表す `AUDIO` を指定しています。

<br/>

**TTSのリクエストのJSON**

```json
{
  "contents": [
    {
      "role": "user",
      "parts": [
        {
          "text": "次の文を自然な日本語で読み上げてください。\n\nこんにちは。"
        }
      ]
    }
  ],
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
```

1. **`contents`**  
   読み上げてほしい文章です。このデモでは、チャットの返答文をそのまま載せます。
2. **`responseModalities`**  
   返してほしいデータの種類です。`AUDIO` を指定すると、テキストではなく音声が返ります。
3. **`speechConfig`**  
   どの声で読むかを指定します。トーンや速度は、ここの項目ではなく読み上げ文章で指定します。

<br/>

返ってきた音声は、レスポンスJSONの `inlineData` にBase64で入っています。画面の **6. Response** では、中身そのものではなく `mimeType` とバイト数だけを表示します。

<br/>

---

## speechConfig

<br/>

**speechConfig** とは、読み上げに使う声を指定する項目です。このデモでは `prebuiltVoiceConfig.voiceName` に、あらかじめ用意された声の名前を入れます。初期値は `Kore` です。

使える声の名前は [Gemini API: Voice options](https://ai.google.dev/gemini-api/docs/speech-generation#voices) を参照してください。

<br/>

### 文章でトーンと速度を指定する

- トーン（口調）と速度は、`speechConfig` の数値項目としてはありません。読み上げさせる**文章**で指示します。

- JSONのキーのような決まった書式はありません。本文の前に、**どんな声で・どの速さで読んでほしいかを自然な言葉で書きます**。

- 公式の短い例は、指示と本文を `:` でつなぐ書き方です。`:` は必須の記号ではなく、「ここからが読む文」をはっきりさせるための区切りです。区切りが曖昧だと、指示文まで読み上げてしまうことがあります。

```text
Say cheerfully: Have a wonderful day!
```

- 改行で分けても同じです。このデモでは `:` を使わず、指示のあとに空行を置いて本文を続けています。

```text
元気よく、少し早めに読んでください。

こんにちは、今日はいい天気ですね。
```

```text
ゆっくり、落ち着いた口調で読んでください。

大切な話があります。
```

<br/>

### オーディオタグで一部分だけ変える

- `[whispers]` や `[very fast]` のように、角括弧のタグを本文に挟むと、その直後の読み方だけを変えられます。公式では、日本語の本文でもタグは英語にするのが推奨です。

```text
[excitedly] こんにちは！ [very slow] 大事な話があります。 [whispers] これは秘密です。
```

よく使われるタグの例は、`[excitedly]`（元気に）、`[whispers]`（ささやき）、`[very fast]` / `[very slow]`（速さ）、`[tired]`（疲れた調子）です。決まった一覧はなく、試しながら選ぶよう公式に書かれています。

<br/>

参照:

- [Gemini API: Text-to-speech](https://ai.google.dev/gemini-api/docs/speech-generation)
- [Controlling speech style with prompts](https://ai.google.dev/gemini-api/docs/speech-generation#controlling-speech-style-with-prompts)
- [Prompting guide（Audio tags）](https://ai.google.dev/gemini-api/docs/speech-generation#prompting-guide)
- [Voice options](https://ai.google.dev/gemini-api/docs/speech-generation#voices)

<br/>

---

## コードの解説

<br/>

### SpeechToSpeech（[`SpeechToSpeech.cs`](Script/SpeechToSpeech.cs)）

<br/>

デモの本体です。上から、録音から再生までの流れを追うとわかりやすいです。

通信は **UnityWebRequest**（HTTPの送受信）と **コルーチン**（`IEnumerator` + `yield`）による **非同期処理** です。コルーチンは `Update` などのメインスレッド処理とは独立した時間軸で進むので、応答待ちのあいだも画面が固まりません。Spaceキーの押し離しの検知だけは `Update` の中で、**旧 Input Manager**（`Input.GetKeyDown` / `GetKeyUp`）を使っています。

<br/>

1. **起動時の準備をする**  
   `Start` — APIキーの読込、`SystemInstruction.txt` の読込、マイクの選択、`AudioSource` の確保、レベル表示の開始
   <br/>
2. **Spaceキーの押し離しを見る**  
   `UpdatePushToTalk` — 押した瞬間に録音を始め、離した瞬間に送信へ進む
   <br/>
3. **マイクの音量を横棒で見せる**  
   `UpdateLevelMeter` — `MicLevel` で直近の音の大きさを0〜1にし、横棒の長さへ反映する
   <br/>
4. **マイクで録音する**  
   `BeginRecording` / `EndRecordingAndSend` — `Microphone.Start` で録音し、`Microphone.End` で止めて、実際に録れた長さだけ切り出す
   <br/>
5. **音声データに変換する**  
   `AudioCodec.ClipToWav` — AudioClipを16-bit PCMのWAVバイト列にし、`Convert.ToBase64String` で文字列にする
   <br/>
6. **音声を送って文字起こしを受け取る（1→2）**  
   `SendSpeechPipelineCoroutine` の前半 — `BuildSttRequestJson` で `inlineData` 付きのJSONを組み立ててPOSTし、返ってきたテキストを取り出す
   <br/>
7. **テキストを送って返答を受け取る（3→4）**  
   同じコルーチンの中盤 — `BuildChatRequestJson` で会話履歴込みのJSONを組み立ててPOSTし、返答を吹き出しへ表示する
   <br/>
8. **返答テキストを音声にして再生する（5→6）**  
   同じコルーチンの後半 — `BuildTtsRequestJson` で `responseModalities: AUDIO` と `speechConfig` を載せてPOSTし、PCM/WAVを `AudioClip` にして `AudioSource.Play`
   <br/>
9. **送受信を画面に出す**  
   `HttpDisplay.FormatRequest` / `FormatResponse` — 中央・右ペインへ見やすく整形して表示する（TTSの応答は音声本体ではなく要約）

<br/>

### 共通ライブラリ（`Assets/Common/Script/`）

<br/>

このデモが使っている共通のライブラリです。シンプルなユーティリティクラスなので、**上の流れを追うときに中身を読む必要はありません。**

| ファイル | 中身 |
|---|---|
| [`GeminiJson`](../Common/Script/GeminiJson.cs) | JSONのエスケープ・整形・省略表示 |
| [`GeminiKey`](../Common/Script/GeminiKey.cs) | APIキーの読込・マスク・generateContentのURL |
| [`GeminiTextResponse`](../Common/Script/GeminiTextResponse.cs) | レスポンスから candidates[0] のテキストを取り出す |
| [`AudioCodec`](../Common/Script/AudioCodec.cs) | AudioClip ⇄ WAV / PCM16 の変換 |
| [`MicLevel`](../Common/Script/MicLevel.cs) | マイクの直近の音の大きさを0〜1にする |
| [`HttpDisplay`](../Common/Script/HttpDisplay.cs) | Request / Response ペインに出す文字列の整形 |
| [`ChatBubble`](../Common/Script/ChatBubble.cs) | 吹き出し1件分の見た目（Prefab: [`MessageBubble.prefab`](Prefab/MessageBubble.prefab)） |
| [`ResponseTime`](../Common/Script/ResponseTime.cs) | 送信から返信までの経過時間をConsoleへ表示 |

これらは他のデモも使っています。挙動を変えたくなったらCommonを直さず、そのファイルをこのデモの `Script/` にコピーしてクラス名を変えてください。
