# 2A.SpeechToText

シリーズ全体の位置づけ → [Docs/demo-series-overview.md](../../Docs/demo-series-overview.md)

---

## このデモで学べること

- 声が文字になる前に、Unity の中では何が起きているのか？
- AI が声を直接「聞いている」ように見えるのは何故か？
- 文字起こしとチャット返信は、それぞれどのリクエストで行われているか？

---

## 事前準備

1. Google AI Studio から Gemini の API にアクセスするための APIキーを取得し、`Assets/Common/APIKey.txt` に保管してください。  
   手順 → [Docs/gemini-ai-studio-setup.md](../../Docs/gemini-ai-studio-setup.md)
2. PC にマイクがつながり、Unity から使える状態にしてください（OS のマイク権限を含む）。

---

## 動かし方

Project ウィンドウで `Assets/2A.SpeechToText/SpeechToText.unity` を開き、Play を押してください。

### 1. Space で話してみる

1. **Space を押したまま**短い文を話し、**離してください**。
2. Status が「録音中」→「音声データ変換中」→「STT」→「Chat」と進むことを見てください。
3. 左に認識されたあなたの発言と Gemini の返答が出ること、中央 Request に STT と Chat の2段があることを確認してください。

### 2. Request で音声の載り方を見る

1. 中央 Request の **STT Request** を開いてください。
2. `inlineData` と `mimeType: audio/wav` があること、`data` に長い文字（Base64）が載っていることを見てください（画面では途中で省略表示されます）。
3. その下の **Chat Request** では、認識テキストが `contents` の user として載っていることを見てください。

### 3. コンテキストのオプションを試す

1. Message と Status のあいだの **Option** で、**コンテキストを送る** を OFF にしてください。
2. Space で名前を含む文を話し、続けてもう一度「名前は？」と聞いてください。
3. 覚えていないように見えること、Chat Request の `contents` が今回の一文だけになっていることを見てください。

---

## マイク入力と音声データとは？

マイク入力とは、PC のマイクが拾った音を、プログラムが扱える数字の列として取り込むことです。このデモでは Unity の `Microphone` が、録音中の音を **AudioClip**（サンプルの集まり）へ書き込みます。

ただし Gemini の API は AudioClip をそのまま受け取りません。送る前に次の変換が必要です。

```text
Microphone.Start
  → AudioClip にサンプルが書き込まれる
Microphone.End と切り出し
  → 実際に録れた長さだけの AudioClip
WAV 化
  → ヘッダ + 16-bit PCM のバイト列（音声データ）
Base64
  → そのバイト列を文字にして JSON に載せる
```

このデモのポイントは、その変換を隠さず Status と中央 Request で見せていることです。左の吹き出しだけ見ると「声が文字になった」ように見えますが、Request を見ると「まず WAV のバイト列を送り、文字起こししている」ことが追えます。

試し方: Space で録音したあと、Status に「音声データ変換中」が出ることと、STT Request の `inlineData` を見比べる。

---

## STT（Speech-to-Text）とは？

音声をテキストに変換することです。このデモでは専用の別サービスではなく、Gemini の `generateContent` に音声（`audio/wav`）を載せ、「文字起こしだけ返して」と頼んでいます。

返ってきたテキストを user の発言として扱い、そのあと **もう一度** `generateContent` でチャット返信を取ります。だから中央 Request には STT と Chat の2段が出ます。

試し方: 同じ発話のあと、STT Response の文字と左の You 吹き出しが一致しているかを見る。

---

## 主要クラス

### SpeechToText（[`SpeechToText.cs`](Script/SpeechToText.cs)）

デモの本体です。上から、録音〜送信の流れを追うとわかりやすいです。

通信は **UnityWebRequest**（HTTP の送受信）と **コルーチン**（`IEnumerator` + `yield`）による **非同期処理** です。コルーチンは `Update` などのメインスレッドの処理とは独立した時間軸で進むので、応答待ちのあいだも画面が固まりません。Space の押し話し検知だけは `Update` で、**旧 Input Manager**（`Input.GetKeyDown` / `GetKeyUp`）を使います。

1. **起動時の準備をする**  
   `Start` — APIキー読込、`SystemInstruction.txt` → UI、マイク確認、録音案内の表示
2. **Space 押し話しを検知する**  
   `UpdatePushToTalk` — 押しているあいだ録音、離したら変換へ（System Instruction 編集中は録音しない）
3. **マイクで録音する**  
   `BeginRecording` / `EndRecordingAndSend` — `Microphone.Start` → `End` → 録れたサンプルだけ切り出し
4. **音声データに変換する**  
   `ConvertAudioClipToWav` — float サンプルを 16-bit PCM にし、WAV ヘッダを付けて `byte[]` にする → Base64
5. **STT する**  
   `SendSpeechPipelineCoroutine` の前半 — `inlineData` 付き JSON を POST し、文字起こしを取り出す
6. **Chat する**  
   同コルーチンの後半 — 認識テキストを user として 1A と同型のリクエストを POST し、返答を吹き出しへ
7. **送受信を画面に出す**  
   Request / Response ペインへ STT と Chat を並べて表示（キーはマスク、長い Base64 は表示だけ短縮）

### ChatBubble（[`ChatBubble.cs`](../1A.TextToText/Script/ChatBubble.cs)）

左ペインの吹き出し1件分です（Prefab: [`Prefab/MessageBubble.prefab`](Prefab/MessageBubble.prefab)）。見た目用のクラスで、1A と同じものを流用しています。通信ロジックは持ちません。
