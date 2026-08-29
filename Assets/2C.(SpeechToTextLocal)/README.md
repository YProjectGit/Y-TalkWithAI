# 2C. SpeechToTextLocal

![speech-to-text-local](../Docs/Image/speech-to-text-local.png)

<br/>

**このデモは必須ではありません。作品制作の中で、より高速に音声認識を行いたくなった場合に参照してください。**

<br/>

SpeechToText（音声の文字起こし）を、クラウドではなく**自分のPC上の音声認識エンジン**で行います。

クラウドGemini API経由だと通常1〜2秒程度かかっていたSTT処理が、ローカル処理だと0.1秒程度へと高速化します。

その後の処理については、先の[SpeechToText](../2A.SpeechToText/README.md)のデモと同様です。

<br/>

---

## 学ぶこと

<br/>

- ### ローカルSTT（Speech-to-Text）

  音声の文字起こしを、インターネット上のAPIではなく、自分のPC上のエンジンで行います。

- ### sherpa-onnx / ReazonSpeech

  日本語の高速な音声認識モデルを、自分のPC上で動かす方法を学びます。

<br/>

---

## 事前準備

<br/>

### sherpa-onnxの配置

- ローカルSTTに使うモデルとネイティブライブラリを、自分でダウンロード・展開し、PCへ配置してください。
- 配置の手順はこちらを参照してください。
  [Assets/Docs/sherpa-onnx-setup.md](../Docs/sherpa-onnx-setup.md)

<br/>

---

## 動かしてみる

<br/>

Project ウィンドウで `Assets/2C.(SpeechToTextLocal)/SpeechToTextLocal.unity` を開き、Playしてください。

### 1. Spaceを押して話す

1. 左ペインのMessage欄の下にあるボリュームゲージが、自分の声に合わせて動くことを確認してください。
2. **Spaceキーを押したまま**短い日本語を話し、話し終えたらキーを**離して**ください。
3. 左に認識された自分の発言と、Geminiの返答が出ることを確認してください。

### 2. ローカルSTTの結果を確認する

1. **1. Local STT** を見て、エンジン名、モデル名、サンプル数が並んでいることを確認してください。
2. **2. Local STT 結果** を見て、認識テキストと経過時間、**RTF** が出ていることを確認してください。

<br/>

---

## ローカルSTT

<br/>

- **ローカルSTT** とは、音声をテキストへ変換する処理を、インターネット上のAPIではなく、自分のPC上のエンジンで行うことを指します。

- ねらいは処理速度です。前回のGeminiベースのSTTは、音声を送信してから文字が返るまで、インターネットを介したデータの往復を待ちます。ローカルSTTは端末の中で処理が完結するので、その待ち時間がほぼなくなります。

- 実際にどれくらいかかったかは、画面の 2. 欄に出る経過時間と **RTF** が目安です。RTF（Real Time Factor）は、音声の長さに対する処理時間の比です。1未満なら、音声の再生時間より速く認識できています。速さはPCの性能とモデルの大きさで変わります。

- このデモでは、Spaceを離したあとの音声（`AudioClip` のfloatサンプル）を **sherpa-onnx** に渡します。

```text
Microphone.Start
  → AudioClip にマイクの音が書き込まれる

Microphone.End と切り出し
  → 実際に録れた長さだけの float の列（16 kHz）

sherpa-onnx OfflineRecognizer
  → 認識テキスト

Gemini generateContent（Text）
  → チャットの返答
```

<br/>

1回の発話のうち、画面の番号1〜4は次の役割です。

| 番号 | 内容 |
|---|---|
| **1. Local STT** | 端末のエンジン名・モデル・サンプル数 |
| **2. Local STT 結果** | 認識テキストと経過時間 / RTF |
| **3. Request** | 認識テキストを、チャットのメッセージとして送信する |
| **4. Response** | チャットの返答が返る |

<br/>

会話履歴は毎回まとめて送られます。1A.TextToTextと違い、コンテキストは常にオンです。

<br/>

---

## sherpa-onnx / ReazonSpeech

<br/>

[sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) は、音声認識のモデルを端末で動かすためのライブラリです。音声認識のモデルは音声を文字にするだけで、文章を考えるLLM（Large Language Model）とは役割が違います。

このデモで動かす ReazonSpeech（Zipformer）は、日本語の音声認識だけを行います。

モデルは次の4ファイルで構成されます。配置先と選び方は [sherpa-onnx-setup.md](../Docs/sherpa-onnx-setup.md) を見てください。

| ファイル | 役割 |
|---|---|
| encoder | 音声の特徴を圧縮する |
| decoder | 文字の並びに変換する |
| joiner | encoder と decoder の出力をつなぐ |
| tokens.txt | 文字と内部IDの対応表 |

<br/>

---

## コードの解説

<br/>

### SpeechToTextLocal（[`SpeechToTextLocal.cs`](Script/SpeechToTextLocal.cs)）

<br/>

デモの本体です。上から、録音から送信までの流れを追うとわかりやすいです。

Chatの通信は **UnityWebRequest**（HTTPの送受信）と **コルーチン**（`IEnumerator` + `yield`）による **非同期処理** です。ローカルSTTもコルーチンからスレッドプールに渡し、待っているあいだ画面は固まりません。Spaceキーの押し離しの検知だけは `Update` の中で、**旧 Input Manager**（`Input.GetKeyDown` / `GetKeyUp`）を使っています。

<br/>

1. **起動時の準備をする**  
   `Start` — APIキーの読込、`SystemInstruction.txt` の読込、マイクの選択、sherpaの初期化、レベル表示の開始
   <br/>
2. **Spaceキーの押し離しを見る**  
   `UpdatePushToTalk` — 押した瞬間に録音を始め、離した瞬間に認識へ進む
   <br/>
3. **マイクの音量を横棒で見せる**  
   `UpdateLevelMeter` — `MicLevel` で直近の音の大きさを0〜1にし、横棒の長さへ反映する
   <br/>
4. **マイクで録音する**  
   `BeginRecording` / `EndRecordingAndSend` — `Microphone.Start` で録音し、`Microphone.End` で止めて、floatサンプルを切り出す
   <br/>
5. **ローカルで文字起こしする（1→2）**  
   `RecognizeThenChatCoroutine` の前半 — `RecognizeBackgroundCoroutine` で `SherpaOfflineAsr.Recognize` をスレッドプールに渡し、認識テキストを取り出す
   <br/>
6. **テキストを送って返答を受け取る（3→4）**  
   同じコルーチンの後半 — `BuildChatRequestJson` で会話履歴込みのJSONを組み立ててPOSTし、返答を吹き出しへ表示する
   <br/>
7. **送受信を画面に出す**  
   1と2はローカルSTTの要約、3と4は `HttpDisplay.FormatRequest` / `FormatResponse` で整形して表示する

<br/>

### SherpaOfflineAsr（[`SherpaOfflineAsr.cs`](Script/SherpaOfflineAsr.cs)）

<br/>

端末の認識エンジンです。モデルの読み込みと、floatサンプルからテキストを返すことだけを持ちます。Gemini通信や吹き出しは持ちません。

<br/>

1. **モデルを読み込む**  
   `TryInitialize` — encoder / decoder / joiner / tokens のパスを確認して `OfflineRecognizer` を作る
   <br/>
2. **音声を文字にする**  
   `Recognize` — `AcceptWaveform` → `Decode` → 本文と経過時間 / RTF を返す

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
