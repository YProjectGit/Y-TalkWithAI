# Y-TalkWithAI

学生が Gemini の API を触って、テキスト・音声のインタラクションをつくるワークショップ教材です。

学習順と各デモの位置づけ → [Assets/Docs/demo-series-overview.md](Assets/Docs/demo-series-overview.md)

---

## 必要なもの

- **Unity 6.3**（このプロジェクトは `6000.3.6f1`）
- **Google アカウント**（APIキー用）
- **マイク**（2A 以降の音声デモ）

---

## 最初にやること

1. Unity Hub でこのフォルダを開き、バージョン `6000.3.6f1` で開いてください。
2. Google AI Studio から Gemini の API にアクセスするための APIキーを取得し、`Assets/Common/APIKey.txt` に保管してください。  
   手順 → [Assets/Docs/gemini-ai-studio-setup.md](Assets/Docs/gemini-ai-studio-setup.md)
3. Project ウィンドウで `Assets/1A.TextToText/TextToText.unity` を開き、Play してください。

無料枠で 429 が出たら → [無料枠を使い切ったとき](Assets/Docs/gemini-ai-studio-setup.md#無料枠を使い切ったとき)

---

## デモ一覧

上から学習していく順です。詳細は各フォルダの README を見てください。

| デモ | 学ぶこと |
|------|----------|
| [1A.TextToText](Assets/1A.TextToText/) | テキストを送って、テキストで返してもらう |
| [1B.TextToData](Assets/1B.TextToData/) | 返事を決まった形（JSON）で受け取る |
| [2A.SpeechToText](Assets/2A.SpeechToText/) | マイクの声を文字にして、同じやり取りをする |
| [2B.SpeechToData](Assets/2B.SpeechToData/) | 声の指示を JSON で受け取り、見た目を変える |
| [2C.SpeechToTextLocal](Assets/2C.SpeechToTextLocal/) | （任意）文字起こしをローカルの sherpa-onnx で行う |
| [3A.SpeechToSpeech](Assets/3A.SpeechToSpeech/) | 返事を音声で受け取る |
| [3B.SpeechToSpeechLiveAPI](Assets/3B.SpeechToSpeechLiveAPI/) | 声の往復を Live API の1セッションにまとめる |
| [3C.SpeechToFunction](Assets/3C.SpeechToFunction/) | 会話の途中でアプリの機能を呼ぶ |

2C は本線ではありません。動かすときだけ追加の配置が要ります。

- 2C → [Assets/Docs/sherpa-onnx-setup.md](Assets/Docs/sherpa-onnx-setup.md)
