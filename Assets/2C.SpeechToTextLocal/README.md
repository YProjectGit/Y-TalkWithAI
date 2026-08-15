# 2C.SpeechToTextLocal

シリーズ全体の位置づけ → [Docs/demo-series-overview.md](../../Docs/demo-series-overview.md)

---

## このデモで学べること

- **ローカル STT（Speech-to-Text）**  
  音声の文字起こしを、クラウドではなく端末のエンジンで行う
- **sherpa-onnx**  
  日本語の音声認識モデル（ReazonSpeech）を、自分の PC 上で動かす

---

## 事前準備

1. Google AI Studio から Gemini の API にアクセスするための APIキーを取得し、`Assets/Common/APIKey.txt` に保管してください（Chat 用。STT には使いません）。  
   手順 → [Docs/gemini-ai-studio-setup.md](../../Docs/gemini-ai-studio-setup.md)
2. sherpa-onnx のモデルとネイティブライブラリを配置してください。  
   手順 → [Docs/sherpa-onnx-setup.md](../../Docs/sherpa-onnx-setup.md)
3. PC にマイクがつながり、Unity から使える状態にしてください（OS のマイク権限を含む）。

---

## 動かし方

Project ウィンドウで `Assets/2C.SpeechToTextLocal/SpeechToTextLocal.unity` を開き、Play を押してください。

### 1. Space で日本語を話してみる

1. **Space を押したまま**短い日本語を話し、**離してください**。
2. Status が「録音中」→「ローカル STT 中」→「3. Request」と進むことを見てください。
3. 左に認識されたあなたの発言と Gemini の返答が出ることを確認してください。

### 2. 発生順（1〜4）で追う

1. **1. Local STT（sherpa-onnx）** … 端末のモデル名・ファイル・サンプル数  
2. **2. Local STT 結果** … 認識テキストと経過 ms / RTF（音声の長さに対する処理時間の比。1 未満なら実時間より速い）  
3. **3. Request - GenerateContent（Text）** … 認識テキストを会話として送る  
4. **4. Response - GenerateContent（Text）** … チャットの返答が返る  

1 と 2 に HTTP の JSON は出ません。音声は Gemini に載せず、文字になってから 3 へ進みます。  
会話の履歴は毎回まとめて送られます（コンテキストは常にオンです）。

---

## ローカル STT

音声をテキストに変換する処理を、インターネット上の API ではなく、自分の PC 上のエンジンで行うことです。

このデモでは、Space を離したあとの音声（`AudioClip` の float サンプル）を **sherpa-onnx** に渡します。2A のように WAV を Base64 にして `generateContent` に載せることはしません。

```text
Microphone.Start
  → AudioClip にサンプルが書き込まれる
Microphone.End と切り出し
  → float の列（16 kHz）
sherpa-onnx OfflineRecognizer
  → 認識テキスト
Gemini generateContent（Text）
  → チャットの返答
```

試し方: 同じ発話のあと、1. 欄にモデル名があること、2. 欄に経過 ms と本文が出ること、3. 欄の JSON に `inlineData` が無いことを見る。

---

## sherpa-onnx

[sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) は、音声認識のモデルを端末で動かすためのライブラリです。音声認識のモデルは音声を文字にするだけで、文章を考える LLM（Large Language Model）とは役割が違います。

このデモで動かす ReazonSpeech（Zipformer）は、日本語の文字起こしだけをします。返答の文は、そのあと Gemini が作ります。画面の 1→2 が音声認識、3→4 が LLM です。

試し方: 2. の本文と左の You 吹き出しが一致しているか、4. の返答が会話になっているかを見る。

---

## 主要クラス

### SpeechToTextLocal（[`SpeechToTextLocal.cs`](Script/SpeechToTextLocal.cs)）

デモの本体です。上から、録音〜ローカル認識〜Chat の流れを追うとわかりやすいです。

Chat の通信は **UnityWebRequest** と **コルーチン** による **非同期処理** です。ローカル STT もコルーチンからスレッドプールに渡し、待っているあいだ画面は固まりません。Space の押し話し検知だけは `Update` で、**旧 Input Manager**（`Input.GetKeyDown` / `GetKeyUp`）を使います。

1. **起動時の準備をする**  
   `Start` — APIキー読込、事前指示、マイク確認、sherpa 初期化
2. **Space 押し話しを検知する**  
   `UpdatePushToTalk` — 押しているあいだ録音、離したら認識へ
3. **マイクで録音する**  
   `BeginRecording` / `EndRecordingAndSend` — `Microphone.Start` → `End` → float サンプル
4. **1→2. ローカル STT**  
   `RecognizeThenChatCoroutine` の前半 — `SherpaOfflineAsr.Recognize` で文字起こし
5. **3→4. GenerateContent（Text）**  
   同コルーチンの後半 — 認識テキストと会話履歴を POST し、返答を吹き出しへ

### SherpaOfflineAsr（[`SherpaOfflineAsr.cs`](Script/SherpaOfflineAsr.cs)）

端末の認識エンジンです。モデルの読み込みと、float サンプルからテキストを返すことだけを持ちます。Gemini 通信や吹き出しは持ちません。

1. **モデルを読み込む**  
   `TryInitialize` — encoder / decoder / joiner / tokens のパスを確認して `OfflineRecognizer` を作る
2. **音声を文字にする**  
   `Recognize` — `AcceptWaveform` → `Decode` → 本文と経過 ms / RTF

### ChatBubble（[`ChatBubble.cs`](../Common/Script/ChatBubble.cs)）

左ペインの吹き出し1件分です（Prefab: [`Prefab/MessageBubble.prefab`](Prefab/MessageBubble.prefab)）。見た目用のクラスで、`Assets/Common/Script/` のものを使います。通信ロジックは持ちません。
