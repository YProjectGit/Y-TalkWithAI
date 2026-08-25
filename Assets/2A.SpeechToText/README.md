# 2A.SpeechToText

入力を文字から声に広げます。Gemini が声を文字にしてくれるので、キーボードを使わないやり取りが作れます。

シリーズ全体の位置づけ → [Assets/Docs/demo-series-overview.md](../Docs/demo-series-overview.md)

---

## このデモで学べること

- **マイク入力**  
  PC のマイクから音声を取り込み、プログラムが扱えるデータにする
- **inlineData**  
  音声を Base64 の文字列にして、リクエストの JSON に埋め込む
- **STT（Speech-to-Text）**  
  音声をテキストに変換する

---

## 事前準備

1. Google AI Studio から Gemini の API にアクセスするための APIキーを取得し、`Assets/Common/APIKey.txt` に保管してください。  
   手順 → [Assets/Docs/gemini-ai-studio-setup.md](../Docs/gemini-ai-studio-setup.md)
2. PC にマイクがつながり、Unity から使える状態にしてください（OS のマイク権限を含む）。

---

## 動かし方

Project ウィンドウで `Assets/2A.SpeechToText/SpeechToText.unity` を開き、Play を押してください。

### 1. Space で話してみる

1. Play したら、左の Message 下の横棒が声に合わせて伸びることを見てください。
2. **Space を押したまま**短い文を話し、**離してください**。
3. Status が「録音中」→「音声データ変換中」→「1. Request」→「3. Request」と進むことを見てください。
4. 左に認識されたあなたの発言と Gemini の返答が出ること、中央・右に番号 1〜4 の欄があることを確認してください。

### 2. 発生順（1〜4）で Request / Response を追う

どちらも Gemini の `generateContent` です。番号は呼ばれた順番です。

1. **1. Request - GenerateContent（Audio）** … 音声（`inlineData` / `audio/wav`）を送る  
2. **2. Response - GenerateContent（Audio）** … 文字起こしが返る  
3. **3. Request - GenerateContent（Text）** … 認識テキストを会話として送る  
4. **4. Response - GenerateContent（Text）** … チャットの返答が返る  

1 の欄で `inlineData` と `mimeType: audio/wav` があること、`data` に長い文字（Base64）が載っていることを確認してください（画面では途中で省略表示されます）。  
会話の履歴は毎回まとめて送られます（コンテキストは常にオンです）。

---

## マイク入力と音声データ

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

このデモのポイントは、その変換を隠さず Status と **1. Request - GenerateContent（Audio）** で見せていることです。左の吹き出しだけ見ると「声が文字になった」ように見えますが、Request を見ると「まず WAV のバイト列を送り、文字起こししている」ことが追えます。左の横棒は、AudioClip に書き込まれている音の大きさをその場で見せます。

試し方: Play 直後から横棒が声で動くこと、Space で録音したあと Status に「音声データ変換中」が出ることと、1. Request の `inlineData` を見比べる。

---

## STT（Speech-to-Text）

音声をテキストに変換することです。このデモでは専用の別サービス（Cloud Speech-to-Text）ではなく、Gemini の `generateContent` に音声（`audio/wav`）を載せ、「文字起こしだけ返して」と頼んでいます。

返ってきたテキストを user の発言として扱い、そのあと **もう一度** `generateContent`（Text）でチャット返信を取ります。画面の 1→2→3→4 がその順番です。

試し方: 同じ発話のあと、2. Response の文字と左の You 吹き出しが一致しているかを見る。

---

## 主要クラス

### SpeechToText（[`SpeechToText.cs`](Script/SpeechToText.cs)）

デモの本体です。上から、録音〜送信の流れを追うとわかりやすいです。

通信は **UnityWebRequest**（HTTP の送受信）と **コルーチン**（`IEnumerator` + `yield`）による **非同期処理** です。コルーチンは `Update` などのメインスレッドの処理とは独立した時間軸で進むので、応答待ちのあいだも画面が固まりません。Space の押し話し検知だけは `Update` で、**旧 Input Manager**（`Input.GetKeyDown` / `GetKeyUp`）を使います。

1. **起動時の準備をする**  
   `Start` — APIキー読込、`SystemInstruction.txt` → UI、マイク確認、レベル監視開始、録音案内の表示
2. **Space 押し話しを検知する**  
   `UpdatePushToTalk` — 押しているあいだ録音、離したら変換へ（System Instruction 編集中は録音しない）
3. **マイク音量を横棒で見せる**  
   `UpdateLevelMeter` — `MicLevel` で直近サンプルの大きさを 0〜1 にし、`fillAmount` にする。待機中は短いループ録音、Space 中は送信用クリップを見る
4. **マイクで録音する**  
   `BeginRecording` / `EndRecordingAndSend` — 監視を止めて `Microphone.Start` → `End` → 監視再開 → 録れたサンプルだけ切り出し
5. **音声データに変換する**  
   `AudioCodec.ClipToWav` — float サンプルを 16-bit PCM にし、WAV ヘッダを付けて `byte[]` にする → Base64（共通スクリプト）
6. **1→2. GenerateContent（Audio）**  
   `SendSpeechPipelineCoroutine` の前半 — 音声付き JSON を POST し、文字起こしを取り出す
7. **3→4. GenerateContent（Text）**  
   同コルーチンの後半 — 認識テキストと会話履歴を POST し、返答を吹き出しへ

### 共通スクリプト（`Assets/Common/Script/`）

このデモが使っている共通の道具です。**上の流れを追うときに中身を読む必要はありません。**

| ファイル | 中身 |
|---|---|
| [`GeminiJson`](../Common/Script/GeminiJson.cs) | JSON のエスケープ・整形・省略表示 |
| [`GeminiKey`](../Common/Script/GeminiKey.cs) | APIキーの読込・マスク・generateContent の URL |
| [`GeminiTextResponse`](../Common/Script/GeminiTextResponse.cs) | レスポンスから candidates[0] のテキストを取り出す |
| [`AudioCodec`](../Common/Script/AudioCodec.cs) | AudioClip ⇄ WAV / PCM16 の変換 |
| [`MicLevel`](../Common/Script/MicLevel.cs) | マイク直近窓の RMS → 横棒の 0〜1 |
| [`HttpDisplay`](../Common/Script/HttpDisplay.cs) | Request / Response ペインに出す文字列の整形 |
| [`ChatBubble`](../Common/Script/ChatBubble.cs) | 吹き出し1件分の見た目（Prefab: [`MessageBubble.prefab`](Prefab/MessageBubble.prefab)） |

これらは他のデモも使っています。挙動を変えたくなったら Common を直さず、そのファイルをこのデモの `Script/` にコピーしてクラス名を変えてください。
