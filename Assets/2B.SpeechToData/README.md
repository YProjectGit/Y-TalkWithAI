# 2B.SpeechToData



![speech-to-data](../Docs/Image/speech-to-data.png)

声で受け取った指示を、フォーマット化されたデータとして返します。話しかけるだけで、アプリの見た目や設定を変えられます。

シリーズ全体の位置づけ → [Assets/Docs/demo-series-overview.md](../Docs/demo-series-overview.md)

---

## このデモで学べること

- **音声入力と構造化出力**  
  声で受け取った指示を、決まった形の JSON にして返す
- **パラメータへの反映**  
  受け取った値を、アプリの見た目や動作につなげる

---

## 事前準備

1. Google AI Studio から Gemini の API にアクセスするための APIキーを取得し、`Assets/Common/APIKey.txt` に保管してください。  
   手順 → [Assets/Docs/gemini-ai-studio-setup.md](../Docs/gemini-ai-studio-setup.md)
2. PC にマイクがつながり、Unity から使える状態にしてください（OS のマイク権限を含む）。

---

## 動かし方

Project ウィンドウで `Assets/2B.SpeechToData/SpeechToData.unity` を開き、Play を押してください。

### 1. Space で色を変えてみる

1. Play したら、左の Message 下の横棒が声に合わせて伸びることを見てください。
2. **Space を押したまま**、たとえば「キューブは夕焼けのオレンジ、背景は夜の紺」と話し、**離してください**。
3. Status が「録音中」→「音声データ変換中」→「1. Request」→「3. Request」と進むことを見てください。
4. 左の **3D キューブの色** と **背景色** が変わること、認識テキスト欄に文字起こしが出ることを確認してください。

### 2. 発生順（1〜4）で Request / Response を追う

どちらも Gemini の `generateContent` です。番号は呼ばれた順番です。

1. **1. Request - GenerateContent（Audio）** … 音声（`inlineData` / `audio/wav`）を送る  
2. **2. Response - GenerateContent（Audio）** … 文字起こしが返る  
3. **3. Request - GenerateContent（Text）** … 認識テキスト＋`responseSchema`（rgb は `NUMBER`）を送る  
4. **4. Response - GenerateContent（Text）** … 構造化 JSON が返り、色へ反映される  

---

## マイク入力と音声データ

マイク入力とは、PC のマイクが拾った音を、プログラムが扱える数字の列として取り込むことです。このデモでは Unity の `Microphone` が録音中の音を **AudioClip** へ書き込み、WAV（16-bit PCM）→ Base64 にして 1. Request の `inlineData` に載せます。

試し方: Space で録音したあと、1. Request に `mimeType: audio/wav` があるかを見る。

---

## 構造化出力とスキーマ

構造化出力とは、自由文ではなく **決まった形の JSON** で返してもらうことです。スキーマはその形の約束で、このデモでは `cubeColor` / `backgroundColor` の `r` / `g` / `b` を **`NUMBER`（0〜1）** で受け取ります（`1B.TextToData` と同じ）。

3. Request の `generationConfig.responseSchema` と、4. Response の JSON、左のキューブ色を見比べてください。

---

## 主要クラス

### SpeechToData（[`SpeechToData.cs`](Script/SpeechToData.cs)）

デモの本体です。上から、録音〜文字起こし〜構造化 JSON〜色反映の流れを追うとわかりやすいです。

通信は **UnityWebRequest**（HTTP の送受信）と **コルーチン**（`IEnumerator` + `yield`）による **非同期処理** です。コルーチンは `Update` などのメインスレッドの処理とは独立した時間軸で進むので、応答待ちのあいだも画面が固まりません。Space の押し話し検知だけは `Update` で、**旧 Input Manager**（`Input.GetKeyDown` / `GetKeyUp`）を使います。2A との違いは、チャット返答ではなく **スキーマ付き JSON をパースしてキューブ色と背景色へ反映する**ところです。

1. **起動時の準備をする**  
   `Start` — APIキー読込、マイク確認、3D プレビュー、案内文言
2. **Space 押し話しを検知する**  
   `UpdatePushToTalk` — 押しているあいだ録音、離したら変換へ
3. **マイクで録音する**  
   `BeginRecording` / `EndRecordingAndSend` — `Microphone.Start` → `End` → 録れたサンプルだけ切り出し
4. **音声データに変換する**  
   `AudioCodec.ClipToWav` — float サンプルを 16-bit PCM にし、WAV ヘッダを付けて `byte[]` にする → Base64（共通スクリプト）
5. **1→2. GenerateContent（Audio）**  
   `SendSpeechPipelineCoroutine` の前半 — 音声付き JSON を POST し、文字起こしを認識テキスト欄へ
6. **3→4. GenerateContent（Text）**  
   同コルーチンの後半 — スキーマ付きで構造化 JSON を受け取り、色へ反映

### 共通スクリプト（`Assets/Common/Script/`）

このデモが使っている共通の道具です。どれも入力と出力だけの小さな関数なので、**上の流れを追うときに中身を読む必要はありません。**

| ファイル | 中身 |
|---|---|
| [`GeminiJson`](../Common/Script/GeminiJson.cs) | JSON のエスケープ・整形・省略表示 |
| [`GeminiKey`](../Common/Script/GeminiKey.cs) | APIキーの読込・マスク・generateContent の URL |
| [`AudioCodec`](../Common/Script/AudioCodec.cs) | AudioClip ⇄ WAV / PCM16 の変換 |
| [`MicLevel`](../Common/Script/MicLevel.cs) | マイク直近窓の RMS → 横棒の 0〜1 |
| [`HttpDisplay`](../Common/Script/HttpDisplay.cs) | Request / Response ペインに出す文字列の整形 |

これらは他のデモも使っています。挙動を変えたくなったら Common を直さず、そのファイルをこのデモの `Script/` にコピーしてクラス名を変えてください。
