# デモシリーズ全体構成（概要）

音声・画像インタラクション・ワークショップの学習順と、各デモの位置づけです。手順は各フォルダの README を見てください。

---

## 事前準備

1. Unity Hub でこのプロジェクトを開き、バージョン **6000.3.6f1**（Unity 6.3）で開いてください。
2. Google AI Studio から Gemini の API にアクセスするための APIキーを取得し、`Assets/Common/APIKey.txt` に保管してください。  
   手順 → [gemini-ai-studio-setup.md](gemini-ai-studio-setup.md)

---

## デモ一覧

上から学習していく順です。

- **[1A.TextToText](../1A.TextToText/)**  
  テキストを送って、テキストで返してもらう基本のやり取り
- **[1B.TextToJSON](../1B.TextToJSON/)**  
  返事を決まった形（JSON）で受け取り、UI やパラメータに反映する
- **[2A.SpeechToText](../2A.SpeechToText/)**  
  マイクの声を文字にして、キーボードなしで同じやり取りをする
- **[2B.SpeechToJSON](../2B.SpeechToJSON/)**  
  声の指示を JSON で受け取り、話しかけるだけで見た目や設定を変える
- **[2C.SpeechToTextLocal](../2C.SpeechToTextLocal/)**（任意）  
  文字起こしだけローカルPC上の sherpa-onnx で行い、待ち時間を減らす。追加手順 → [sherpa-onnx-setup.md](sherpa-onnx-setup.md)
- **[3A.SpeechToSpeech](../3A.SpeechToSpeech/)**  
  返事を音声で受け取り、画面を見ないやり取りにする
- **[3B.SpeechToSpeechLiveAPI](../3B.SpeechToSpeechLiveAPI/)**  
  声の往復を Live API の1セッションにまとめ、人と話すテンポに近づける
- **[3C.SpeechToMotion](../3C.SpeechToMotion/)**  
  会話の途中でアプリの機能を呼び、会話をそのまま操作にする
- **[4.VisionToSpeech](../4.VisionToSpeech/)**  
  カメラが捉えたものについて、映像を見せて声で話す
- **[5.ScreenToSpeech](../5.ScreenToSpeech/)**  
  アプリ自身が描く画面を見せ、画面の中身を声で解釈する
- **[6.TextToImage](../6.TextToImage/)**  
  言葉から絵を1枚受け取る
- **[7.ImageToImage](../7.ImageToImage/)**  
  元画像と指示を一緒に送り、いまある絵を変える

---

## コードの考え方

- 各デモの本体は、そのフォルダのメインスクリプト1本です。処理は送信 → 待ち → 受信の順です。
- リクエスト JSON の組み立てと、レスポンスからの取り出しは、そのスクリプトに書いてあります。共通化していません。
- 画面中央が Request、右が Response です。`5` と `7` にはこの欄がありません。
- `Assets/Common/Script/` は JSON 整形・APIキー読込・音声変換などのユーティリティです。デモの流れを読むときは見なくてよいです。
- 改変するときはデモフォルダをコピーしてください。`Common` を変えると全デモに波及します。
