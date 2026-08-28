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

上から学習していく順です。詳細は各フォルダの README を見てください。

| デモ | 学ぶこと |
|------|----------|
| [1A.TextToText](Assets/1A.TextToText/) | テキストを送って、テキストで返してもらう基本のやり取り |
| [1B.TextToData](Assets/1B.TextToData/) | 返事を決まったデータ形式（JSON）で受け取り、UI やパラメータに反映する |
| [2A.SpeechToText](Assets/2A.SpeechToText/) | マイク入力の音声を文字に変換する |
| [2B.SpeechToData](Assets/2B.SpeechToData/) | 1Bと2Aを組み合わせたサンプル。声でグラフィックを操作する |
| [2C.SpeechToTextLocal](Assets/2C.(SpeechToTextLocal)/)（任意） | 音声認識だけローカルPC上のエンジンで行い、レスポンスを向上させる |
| [3A.SpeechToSpeech](Assets/3A.SpeechToSpeech/) | 返事も音声で受け取り、音声と音声のインタラクションを実現する |
| [3B.SpeechToSpeechLiveAPI](Assets/3B.SpeechToSpeechLiveAPI/) | 音声のコミュニケーションを Live API の1セッションにまとめ、ストリーミング化する |
| [3C.SpeechToFunction](Assets/3C.SpeechToFunction/) | 会話の途中でアプリの関数を呼び出し、会話で対象を操作する |

2C は本線ではありません。動かすときだけ追加の配置が要ります。

- 2C → [Assets/Docs/sherpa-onnx-setup.md](Assets/Docs/sherpa-onnx-setup.md)
