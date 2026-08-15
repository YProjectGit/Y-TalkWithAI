# デモシリーズ全体構成（概要）

音声・画像インタラクション・ワークショップの学習順と、各デモの位置づけです。手順は各フォルダの README を見てください。

---

## 事前準備

Google AI Studio から Gemini の API にアクセスするための APIキーを取得し、`Assets/Common/APIKey.txt` に保管してください。  
手順 → [gemini-ai-studio-setup.md](gemini-ai-studio-setup.md)

---

## デモ一覧

上から学習していく順です。

- **[1A.TextToText](../Assets/1A.TextToText/)**  
  テキストを送って、テキストで返してもらう基本のやり取り
- **[1B.TextToJSON](../Assets/1B.TextToJSON/)**  
  返事を決まった形（JSON）で受け取り、UI やパラメータに反映する
- **[2A.SpeechToText](../Assets/2A.SpeechToText/)**  
  マイクの声を文字にして、キーボードなしで同じやり取りをする
- **[2C.(SpeechToTextSherpa)](../Assets/2C.%28SpeechToTextSherpa%29/)**  
  文字起こしだけローカルPC上の sherpa-onnx で行い、待ち時間を減らす
- **[2D.(SpeechToTextWhisper)](../Assets/2D.%28SpeechToTextWhisper%29/)**  
  文字起こしをローカルPC上の Whisper で行い、多くの言語を1つのモデルで扱う
- **[2B.SpeechToJSON](../Assets/2B.SpeechToJSON/)**  
  声の指示を JSON で受け取り、話しかけるだけで見た目や設定を変える
- **[3A.SpeechToSpeech](../Assets/3A.SpeechToSpeech/)**  
  返事を音声で受け取り、画面を見ないやり取りにする
- **[3B.SpeechToSpeechLiveAPI](../Assets/3B.SpeechToSpeechLiveAPI/)**  
  声の往復を Live API の1セッションにまとめ、人と話すテンポに近づける
- **[3C.SpeechToMotion](../Assets/3C.SpeechToMotion/)**  
  会話の途中でアプリの機能を呼び、会話をそのまま操作にする
- **[4.VisionToSpeech](../Assets/4.VisionToSpeech/)**  
  カメラが捉えたものについて、映像を見せて声で話す
- **[5.ScreenToSpeech](../Assets/5.ScreenToSpeech/)**  
  アプリ自身が描く画面を見せ、画面の中身を声で解釈する
- **[6.TextToImage](../Assets/6.TextToImage/)**  
  言葉から絵を1枚受け取る
- **[7.ImageToImage](../Assets/7.ImageToImage/)**  
  元画像と指示を一緒に送り、いまある絵を変える

---

## コードの考え方

- 各デモの本体は、そのフォルダのメインスクリプト1本です。処理は送信 → 待ち → 受信の順です。
- リクエスト JSON の組み立てと、レスポンスからの取り出しは、そのスクリプトに書いてあります。共通化していません。
- 画面中央が Request、右が Response です。`5` と `7` にはこの欄がありません。
- `Assets/Common/Script/` は JSON 整形・APIキー読込・音声変換などのユーティリティです。デモの流れを読むときは見なくてよいです。
- 改変するときはデモフォルダをコピーしてください。`Common` を変えると全デモに波及します。
