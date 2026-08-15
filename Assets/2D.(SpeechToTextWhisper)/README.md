# 2D.(SpeechToTextWhisper)

2C と同じく、ローカルの音声認識エンジンで文字起こしを速くします。使うのは whisper.unity の Whisper で、多くの言語を1つのモデルで扱えます。

シリーズ全体の位置づけ → [Docs/demo-series-overview.md](../../Docs/demo-series-overview.md)

---

## このデモで学べること

- **ローカル STT（Speech-to-Text）**  
  音声の文字起こしを、クラウドではなく端末のエンジンで行い、待ち時間を減らす
- **whisper.unity**  
  多言語の音声認識モデル（Whisper）を、自分の PC 上で動かす

---

## 事前準備

1. Google AI Studio から Gemini の API にアクセスするための APIキーを取得し、`Assets/Common/APIKey.txt` に保管してください（Chat 用。STT には使いません）。  
   手順 → [Docs/gemini-ai-studio-setup.md](../../Docs/gemini-ai-studio-setup.md)
2. whisper.unity の ggml モデルを配置してください。  
   手順 → [Docs/whisper-unity-setup.md](../../Docs/whisper-unity-setup.md)
3. PC にマイクがつながり、Unity から使える状態にしてください（OS のマイク権限を含む）。

---

## 動かし方

Project ウィンドウで `Assets/2D.(SpeechToTextWhisper)/SpeechToTextWhisper.unity` を開き、Play を押してください。

### 1. Space で日本語を話してみる

1. **Space を押したまま**短い日本語を話し、**離してください**。
2. Status が「録音中」→「ローカル STT 中」→「3. Request」と進むことを見てください。
3. 左に認識されたあなたの発言と Gemini の返答が出ることを確認してください。

### 2. 発生順（1〜4）で追う

1. **1. Local STT（whisper.unity）** … 端末のモデル名・言語・サンプル数  
2. **2. Local STT 結果** … 認識テキストと経過 ms / RTF（音声の長さに対する処理時間の比。1 未満なら実時間より速い）  
3. **3. Request - GenerateContent（Text）** … 認識テキストを会話として送る  
4. **4. Response - GenerateContent（Text）** … チャットの返答が返る  

1 と 2 に HTTP の JSON は出ません。音声は Gemini に載せず、文字になってから 3 へ進みます。  
会話の履歴は毎回まとめて送られます（コンテキストは常にオンです）。

---

## ローカル STT

音声をテキストに変換する処理を、インターネット上の API ではなく、自分の PC 上のエンジンで行うことです。

ねらいは速さです。2A は音声を送ってから文字が返るまで、クラウドとの往復を待ちます。ここは端末の中で完結するので、その待ちがなくなります。実際にどれくらいかかったかは、画面の 2. 欄に出る経過 ms と RTF が目安です（速さは PC の性能とモデルの大きさで変わります）。

このデモでは、Space を離したあとの音声（`AudioClip` の float サンプル）を **whisper.unity** に渡します。2A のように WAV を Base64 にして `generateContent` に載せることはしません。

```text
Microphone.Start
  → AudioClip にサンプルが書き込まれる
Microphone.End と切り出し
  → float の列（16 kHz）
WhisperManager.GetTextAsync
  → 認識テキスト
Gemini generateContent（Text）
  → チャットの返答
```

試し方: 2A と同じ文を話し、文字が出るまでの速さを比べる。1. 欄にモデル名があること、2. 欄に経過 ms と本文が出ること、3. 欄の JSON に `inlineData` が無いことを見る。

---

## Whisper

Whisper は、音声を文字にする専用のモデルです。文章を考える LLM（Large Language Model）とは役割が違います。

このデモでは [whisper.unity](https://github.com/Macoron/whisper.unity) 経由で、端末の whisper.cpp が多言語モデル（既定は `ggml-base.bin`）を動かします。言語は `ja` を指定しています。返答の文は、そのあと Gemini が作ります。画面の 1→2 が音声認識、3→4 が LLM です。

試し方: 2. の本文と左の You 吹き出しが一致しているか、4. の返答が会話になっているかを見る。

---

## 主要クラス

### SpeechToTextWhisper（[`SpeechToTextWhisper.cs`](Script/SpeechToTextWhisper.cs)）

デモの本体です。上から、録音〜ローカル認識〜Chat の流れを追うとわかりやすいです。

Chat の通信は **UnityWebRequest** と **コルーチン** による **非同期処理** です。ローカル STT は `WhisperManager.GetTextAsync`（`Task`）をコルーチンから待ち、待っているあいだ画面は固まりません。Space の押し話し検知だけは `Update` で、**旧 Input Manager**（`Input.GetKeyDown` / `GetKeyUp`）を使います。

1. **起動時の準備をする**  
   `Start` — APIキー読込、事前指示、マイク確認、Whisper 初期化
2. **Space 押し話しを検知する**  
   `UpdatePushToTalk` — 押しているあいだ録音、離したら認識へ
3. **マイクで録音する**  
   `BeginRecording` / `EndRecordingAndSend` — `Microphone.Start` → `End` → float サンプル
4. **1→2. ローカル STT**  
   `RecognizeThenChatCoroutine` の前半 — `WhisperManager.GetTextAsync` で文字起こし
5. **3→4. GenerateContent（Text）**  
   同コルーチンの後半 — 認識テキストと会話履歴を POST し、返答を吹き出しへ

### WhisperManager（パッケージ `com.whisper.unity`）

端末の認識エンジンです。ggml モデルの読み込みと、float サンプルからテキストを返すことだけを持ちます。Gemini 通信や吹き出しは持ちません。

1. **モデルを読み込む**  
   `InitModel` — `whisperModelRelativePath` の ggml を読み、言語を `ja` にする
2. **音声を文字にする**  
   `GetTextAsync` — サンプル列とサンプルレートを渡し、本文を返す

### 共通スクリプト（`Assets/Common/Script/`）

このデモが使っている共通の道具です。**上の流れを追うときに中身を読む必要はありません。**

| ファイル | 中身 |
|---|---|
| [`GeminiJson`](../Common/Script/GeminiJson.cs) | JSON のエスケープ・整形・省略表示 |
| [`GeminiKey`](../Common/Script/GeminiKey.cs) | APIキーの読込・マスク・generateContent の URL |
| [`GeminiTextResponse`](../Common/Script/GeminiTextResponse.cs) | レスポンスから candidates[0] のテキストを取り出す |
| [`AudioCodec`](../Common/Script/AudioCodec.cs) | AudioClip ⇄ WAV / PCM16 の変換 |
| [`HttpDisplay`](../Common/Script/HttpDisplay.cs) | Request / Response ペインに出す文字列の整形 |
| [`ChatBubble`](../Common/Script/ChatBubble.cs) | 吹き出し1件分の見た目（Prefab: [`MessageBubble.prefab`](Prefab/MessageBubble.prefab)） |

これらは他のデモも使っています。挙動を変えたくなったら Common を直さず、そのファイルをこのデモの `Script/` にコピーしてクラス名を変えてください。
