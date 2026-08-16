# Y-TalkWithAI

学生が Gemini の API を触って、テキスト・音声・画像のインタラクションをつくるワークショップ教材です。

学習順と各デモの位置づけ → [Docs/demo-series-overview.md](Docs/demo-series-overview.md)

---

## 必要なもの

- **Unity 6.3**（このプロジェクトは `6000.3.6f1`）
- **Google アカウント**（APIキー用）
- **マイク**（2A 以降の音声デモ）
- **カメラ**（4.VisionToSpeech / 7.ImageToImage）

---

## 最初にやること

1. Unity Hub でこのフォルダを開き、バージョン `6000.3.6f1` で開いてください。
2. Google AI Studio から Gemini の API にアクセスするための APIキーを取得し、`Assets/Common/APIKey.txt` に保管してください。  
   手順 → [Docs/gemini-ai-studio-setup.md](Docs/gemini-ai-studio-setup.md)
3. Project ウィンドウで `Assets/1A.TextToText/TextToText.unity` を開き、Play してください。

無料枠で 429 が出たら → [Docs/gemini-api-pricing.md](Docs/gemini-api-pricing.md)

---

## デモ一覧

上から学習していく順です。詳細は各フォルダの README を見てください。

| デモ | 学ぶこと |
|------|----------|
| [1A.TextToText](Assets/1A.TextToText/) | テキストを送って、テキストで返してもらう |
| [1B.TextToJSON](Assets/1B.TextToJSON/) | 返事を決まった形（JSON）で受け取る |
| [2A.SpeechToText](Assets/2A.SpeechToText/) | マイクの声を文字にして、同じやり取りをする |
| [2B.SpeechToJSON](Assets/2B.SpeechToJSON/) | 声の指示を JSON で受け取り、見た目を変える |
| [2C.(SpeechToTextSherpa)](Assets/2C.(SpeechToTextSherpa)/) | （任意）文字起こしをローカルの sherpa-onnx で行う |
| [2D.(SpeechToTextWhisper)](Assets/2D.(SpeechToTextWhisper)/) | （任意）文字起こしをローカルの Whisper で行う |
| [3A.SpeechToSpeech](Assets/3A.SpeechToSpeech/) | 返事を音声で受け取る |
| [3B.SpeechToSpeechLiveAPI](Assets/3B.SpeechToSpeechLiveAPI/) | 声の往復を Live API の1セッションにまとめる |
| [3C.SpeechToMotion](Assets/3C.SpeechToMotion/) | 会話の途中でアプリの機能を呼ぶ |
| [4.VisionToSpeech](Assets/4.VisionToSpeech/) | カメラ映像について声で話す |
| [5.ScreenToSpeech](Assets/5.ScreenToSpeech/) | アプリ自身が描く画面を声で解釈する |
| [6.TextToImage](Assets/6.TextToImage/) | 言葉から絵を1枚受け取る |
| [7.ImageToImage](Assets/7.ImageToImage/) | 元画像と指示を送り、いまある絵を変える |

2C / 2D は本線ではありません。動かすときだけ追加の配置が要ります。

- 2C → [Docs/sherpa-onnx-setup.md](Docs/sherpa-onnx-setup.md)
- 2D → [Docs/whisper-unity-setup.md](Docs/whisper-unity-setup.md)
