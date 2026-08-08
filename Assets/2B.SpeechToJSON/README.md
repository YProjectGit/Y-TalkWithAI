# 2B.SpeechToJSON

シリーズ全体の位置づけ → [Docs/demo-series-overview.md](../../Docs/demo-series-overview.md)

---

## このデモで学べること

- **音声入力と構造化出力**  
  声の指示を受け取り、決まった形のデータ（JSON）として返す
- **スキーマ**  
  返してほしい JSON の構造を先に定義する
- **パラメータへの反映**  
  受け取った値を、色などアプリの見た目や動作につなげる

---

## 事前準備

1. Google AI Studio から Gemini の API にアクセスするための APIキーを取得し、`Assets/Common/APIKey.txt` に保管してください。  
   手順 → [Docs/gemini-ai-studio-setup.md](../../Docs/gemini-ai-studio-setup.md)
2. PC にマイクがつながり、Unity から使える状態にしてください（OS のマイク権限を含む）。

---

## 動かし方

Project ウィンドウで `Assets/2B.SpeechToJSON/SpeechToJSON.unity` を開き、Play を押してください。

### 1. Space で色を変えてみる

1. **Space を押したまま**、たとえば「キューブは夕焼けのオレンジ、背景は夜の紺」と話し、**離してください**。
2. Status が録音 → 音声変換 → 1. Request → 3. Request と進むことを見てください。
3. 左の **3D キューブの色** と **背景色** が変わること、認識テキスト欄に文字起こしが出ることを確認してください。

### 2. 発生順（1〜4）で Request / Response を追う

どちらも Gemini の `generateContent` です。番号は呼ばれた順番です。

1. **1. Request - GenerateContent（Audio）** … 音声（`inlineData` / `audio/wav`）を送る  
2. **2. Response - GenerateContent（Audio）** … 文字起こしが返る  
3. **3. Request - GenerateContent（Text）** … 認識テキスト＋`responseSchema`（rgb は `NUMBER`）を送る  
4. **4. Response - GenerateContent（Text）** … 構造化 JSON が返り、色へ反映される  

---

## マイク入力と音声データとは？

マイク入力とは、PC のマイクが拾った音を、プログラムが扱える数字の列として取り込むことです。このデモでは Unity の `Microphone` が録音中の音を **AudioClip** へ書き込み、WAV（16-bit PCM）→ Base64 にして 1. Request の `inlineData` に載せます。

試し方: Space で録音したあと、1. Request に `mimeType: audio/wav` があるかを見る。

---

## 構造化出力とスキーマとは？

構造化出力とは、自由文ではなく **決まった形の JSON** で返してもらうことです。スキーマはその形の約束で、このデモでは `cubeColor` / `backgroundColor` の `r` / `g` / `b` を **`NUMBER`（0〜1）** で受け取ります（`1B.TextToJSON` と同じ）。

3. Request の `generationConfig.responseSchema` と、4. Response の JSON、左のキューブ色を見比べてください。

---

## 主要クラス

### SpeechToJSON（[`SpeechToJSON.cs`](Script/SpeechToJSON.cs)）

デモの本体です。Space 押し話しは **旧 Input Manager**（`Input.GetKeyDown` / `GetKeyUp`）です。通信は **UnityWebRequest** と **コルーチン** による非同期処理です。

1. **起動時の準備をする** … APIキー、マイク、3D プレビュー、案内文言  
2. **Space で録音する** … `Microphone.Start` → `End` → WAV → Base64  
3. **1→2. GenerateContent（Audio）** … 文字起こしを取り、認識テキスト欄へ  
4. **3→4. GenerateContent（Text）** … スキーマ付きで構造化 JSON を受け取り、色へ反映  
