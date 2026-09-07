# 2B. SpeechToData

![speech-to-data](../Docs/Image/speech-to-data.png)

<br/>

声で受け取った指示を、文章ではなく**プログラムがそのまま読めるJSON**として受け取るアプリケーションです。

マイクで話した内容が文字起こしされたあと、**1B.TextToDataのデモ**と同じく3Dキューブと背景の色が変わります。

<br/>

---

## 学ぶこと

<br/>

このデモは、これまでの **[TextToData](../1B.TextToData/README.md)** と、**[SpeechToText](../2A.SpeechToText/README.md)** の組み合わせです。

下記の要素それぞれについては、上記のリンクから参照してください。

- ### STT（Speech-to-Text）

- ### 構造化出力

<br/>

---

## コードの解説

<br/>

### SpeechToData（[`SpeechToData.cs`](Script/SpeechToData.cs)）

<br/>

デモの本体です。上から、録音から色反映までの流れを追うとわかりやすいです。

通信は **UnityWebRequest**（HTTPの送受信）と **コルーチン**（`IEnumerator` + `yield`）による **非同期処理** です。コルーチンは `Update` などのメインスレッド処理とは独立した時間軸で進むので、応答待ちのあいだも画面が固まりません。Spaceキーの押し離しの検知だけは `Update` の中で、**旧 Input Manager**（`Input.GetKeyDown` / `GetKeyUp`）を使っています。

<br/>

1. **起動時の準備をする**  
   `Start` — APIキーの読込、マイクの選択、3Dプレビュー、レベル表示の開始
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
7. **テキストを送って構造化JSONを受け取る（3→4）**  
   同じコルーチンの後半 — `BuildStructuredRequestJson` で `responseMimeType` と `responseSchema` を載せてPOSTする
   <br/>
8. **JSONをパースして色を変える**  
   `TryParseAndApply` — `cubeColor` / `backgroundColor` を読み、キューブとカメラ背景へ適用する
   <br/>
9. **送受信を画面に出す**  
   `HttpDisplay.FormatRequest` / `FormatResponse` — 中央・右ペインへ見やすく整形して表示する

<br/>

### 共通ライブラリ（`Assets/Common/Script/`）

<br/>

このデモが使っている共通のライブラリです。シンプルなユーティリティクラスなので、**上の流れを追うときに中身を読む必要はありません。**

| ファイル | 中身 |
|---|---|
| [`GeminiJson`](../Common/Script/GeminiJson.cs) | JSONのエスケープ・整形・省略表示 |
| [`GeminiKey`](../Common/Script/GeminiKey.cs) | APIキーの読込・マスク・generateContentのURL |
| [`AudioCodec`](../Common/Script/AudioCodec.cs) | AudioClip ⇄ WAV / PCM16 の変換 |
| [`MicLevel`](../Common/Script/MicLevel.cs) | マイクの直近の音の大きさを0〜1にする |
| [`HttpDisplay`](../Common/Script/HttpDisplay.cs) | Request / Response ペインに出す文字列の整形 |
| [`ResponseTime`](../Common/Script/ResponseTime.cs) | 送信から返信までの経過時間をConsoleへ表示 |

これらは他のデモも使っています。挙動を変えたくなったらCommonを直さず、そのファイルをこのデモの `Script/` にコピーしてクラス名を変えてください。
