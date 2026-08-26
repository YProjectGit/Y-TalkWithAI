# 2A. SpeechToText

![speech-to-text](../Docs/Image/speech-to-text.png)<br/>

入力を文字から**音声**に広げたAIチャットのアプリケーションです。

マイクで録音した音声をGeminiへ送ると、文字起こしされ、その内容に対する返答が返ってきます。

学習のため、音声を送る通信と、文字で会話する通信の2回分が見えるようになっています。

<br/>

---

## このデモで学ぶこと

<br/>

- ### STT（Speech-to-Text）

  音声をテキストへ変換し、その結果をそのままチャットの入力として使います。

- ### マイク入力

  PCのマイクが拾った音を録音し、プログラムが扱えるデータとして取り込みます。

- ### 音声データの送信（inlineData）

  音声をBase64という形式の文字列に変換し、リクエストのJSONへ埋め込んで送ります。

<br/>

---

## 事前準備

<br/>

### マイクの準備

- PCにマイクを接続し、音声が入力できる状態にしてください。
- OSの設定でマイクの使用が許可されていることを確認してください。許可がないと、録音しても無音のデータが送られます。

<br/>

---

## 動かしてみる

<br/>

Project ウィンドウで `Assets/2A.SpeechToText/SpeechToText.unity` を開き、Playしてください。

### 1. Spaceを押して話す

1. 左ペインのMessage欄の下にあるボリュームゲージが、自分の声に合わせて動くことを確認してください。
2. **Spaceキーを押したまま**短い文を話し、話し終えたらキーを**離して**ください。

### 2. 音声データが送られていることを確認する

1. 中央ペインの **1. Request** を見て、`inlineData` という項目があることを確認してください。
2. その中の `mimeType` が `audio/wav` になっていることを確認してください。
3. `data` に長い文字列（Base64）が入っていることを確認してください。画面では途中が省略して表示されます。

### 3. 2回の通信を順番に追う

1. **1. Request** で音声をSTTに送り、**2. Response** で文字起こしが返っていることを確認してください。
2. **3. Request** で、文字起こしされたテキストがLLMへと送られていることを確認してください。

<br/>

---

## 解説

<br/>

### マイク入力

-　PCのマイクが拾った音を、プログラムが扱えるデータとして取り込みます。Unityでは `Microphone` クラスを使い、録音した音を **AudioClip** という入れ物へ書き込みます。AudioClipの中身は、各瞬間の音の波形を数値化にしたデータ（サンプル）の配列です。

<br/>

### 音声データ

-　AudioClipはUnity内部のデータなので、そのままではAPIへ送れません。送る前に、一般的な音声ファイルの形式へ変換します。

-　このデモでは **WAV** という形式に変換します。変換の流れは次のとおりです。


```text
Microphone.Start
  → AudioClip にマイクの音が書き込まれる

Microphone.End と切り出し
  → 実際に録れた長さだけの AudioClipが作成される

WAV化
  → ヘッダ + 16-bit PCM のバイト列

Base64
  → そのバイト列を文字列にしてリクエストJSONに載せる
```

<br/>

---

## inlineData

<br/>

**inlineData** とは、音声や画像などのファイルを、リクエストのJSONの中に直接埋め込んで送るための項目です。

JSONは文字だけを扱うデータ形式なので、音声のようなバイト列はそのまま書き込めません。そこで **Base64** という方式でバイト列を文字列へ変換し、`data` に載せます。あわせて `mimeType` でデータの種類を伝えます。

<br/>

**音声を送るリクエストのJSON**

```json
{
  "contents": [
    {
      "role": "user",
      "parts": [
        {
          "text": "この音声を日本語で文字起こししてください。前置きや説明は付けず、発話の本文だけを返してください。"
        },
        {
          "inlineData": {
            "mimeType": "audio/wav",
            "data": "UklGRiQAAABXQVZFZm10IBAAAAABAAEAgD4AAAB9AAACABAAZGF0YQAA..."
          }
        }
      ]
    }
  ]
}
```

1. **`parts`**  
   1回の発言に複数の部品を入れられます。ここでは指示文と音声の2つを並べています。
2. **`text`**  
   音声に対して何をしてほしいかを、文章で指示します。
3. **`inlineData`**  
   埋め込むファイル本体です。`mimeType` で種類を、`data` で中身を伝えます。
4. **`mimeType`**  
   データの種類です。WAV形式の音声なので `audio/wav` を指定します。
5. **`data`**  
   WAVのバイト列をBase64で文字列にしたものです。実際は非常に長い文字列になります。

<br/>

---

## STT（Speech-to-Text）

<br/>

-　**STT (Speech To Text)** とは、音声をテキストへ変換することです。「音声認識」と呼ばれます。

-　このデモではまず、音声認識APIとしてGemini APIの `generateContent` に音声を送信し、認識されたテキストをレスポンスとして受け取ります。さらにそのテキストを自分の発言テキストとして `generateContent` に送信し、そのレスポンスとしてチャットの返答を受け取ります。

-　つまり、1回の発話につきGemini APIとの通信が2回行われます。画面の番号1〜4が、その順番に対応しています。

| 番号 | 内容 |
|---|---|
| **1. Request** | 音声（`inlineData` / `audio/wav`）を送る |
| **2. Response** | 文字起こしされたテキストが返る |
| **3. Request** | そのテキストを、チャットのメッセージとして送信する |
| **4. Response** | チャットの返答が返る |

<br/>

会話履歴は毎回まとめて送られます。1A.TextToTextと違い、コンテキストは常にオンです。

<br/>

---

## コードの解説

<br/>

### SpeechToText（[`SpeechToText.cs`](Script/SpeechToText.cs)）

<br/>

デモの本体です。上から、録音から送信までの流れを追うとわかりやすいです。

通信は **UnityWebRequest**（HTTPの送受信）と **コルーチン**（`IEnumerator` + `yield`）による **非同期処理** です。コルーチンは `Update` などのメインスレッド処理とは独立した時間軸で進むので、応答待ちのあいだも画面が固まりません。Spaceキーの押し離しの検知だけは `Update` の中で、**旧 Input Manager**（`Input.GetKeyDown` / `GetKeyUp`）を使っています。

<br/>

1. **起動時の準備をする**  
   `Start` — APIキーの読込、`SystemInstruction.txt` の読込、マイクの選択、レベル表示の開始
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
   同じコルーチンの後半 — `BuildChatRequestJson` で会話履歴込みのJSONを組み立ててPOSTし、返答を吹き出しへ表示する
   <br/>
8. **送受信を画面に出す**  
   `HttpDisplay.FormatRequest` / `FormatResponse` — 中央・右ペインへ見やすく整形して表示する

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
