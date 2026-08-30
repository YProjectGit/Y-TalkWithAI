# Y-TalkWithAI

Gemini の API を触って、テキスト・音声のインタラクションをつくるワークショップ教材です。

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

---

## デモ一覧

上から学習していく順です。各デモの位置づけは [Assets/Docs/demo-series-overview.md](Assets/Docs/demo-series-overview.md)、手順は各フォルダの README を見てください。

- [1A.TextToText](Assets/1A.TextToText/)
- [1B.TextToData](Assets/1B.TextToData/)
- [2A.SpeechToText](Assets/2A.SpeechToText/)
- [2B.SpeechToData](Assets/2B.SpeechToData/)
- [2C.SpeechToTextLocal](Assets/2C.(SpeechToTextLocal)/)（任意）
- [3A.SpeechToSpeech](Assets/3A.SpeechToSpeech/)
- [3B.SpeechToSpeechLiveAPI](Assets/3B.SpeechToSpeechLiveAPI/)
- [3C.SpeechToFunction](Assets/3C.SpeechToFunction/)

2C は本線ではありません。動かすときだけ追加の配置が要ります。

- 2C → [Assets/Docs/sherpa-onnx-setup.md](Assets/Docs/sherpa-onnx-setup.md)
