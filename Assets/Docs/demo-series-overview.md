# デモ全体構成

<br/>

テキスト・音声インタラクション・ワークショップの学習順と、各デモの位置づけです。手順は各フォルダの README を見てください。

<br/>

---

## 事前準備

<br/>

1. Unity Hub でこのプロジェクトを開き、バージョン **6000.3.6f1**（Unity 6.3）で開いてください。
2. Google AI Studio から Gemini の API にアクセスするための APIキーを取得し、`Assets/Common/APIKey.txt` に保管してください。  
   手順 → [gemini-ai-studio-setup.md](gemini-ai-studio-setup.md)

<br/>

---

## デモ一覧

<br/>

上から学習していく順です。

- **[1A.TextToText](../1A.TextToText/)**  
  テキストを送って、テキストで返してもらう基本のやり取り
- **[1B.TextToData](../1B.TextToData/)**  
  返事を決まったデータ形式（JSON）で受け取り、UI やパラメータに反映する
- **[2A.SpeechToText](../2A.SpeechToText/)**  
  マイク入力の音声を文字に変換する
- **[2B.SpeechToData](../2B.SpeechToData/)**  
  1Bと2Aを組み合わせたサンプル。声でグラフィックを操作する
- **[2C.SpeechToTextLocal](../2C.(SpeechToTextLocal)/)**（任意）  
  音声認識だけローカルPC上のエンジンで行い、レスポンスを向上させる
- **[3A.SpeechToSpeech](../3A.SpeechToSpeech/)**  
  返事も音声で受け取り、音声と音声のインタラクションを実現する
- **[3B.SpeechToSpeechLiveAPI](../3B.SpeechToSpeechLiveAPI/)**  
  音声のコミュニケーションを Live API の1セッションにまとめ、ストリーミング化する
- **[3C.SpeechToFunction](../3C.SpeechToFunction/)**  
  会話の途中でアプリの関数を呼び出し、会話で対象を操作する

<br/>

---

## コードの考え方

<br/>

- 各デモの本体は、そのフォルダのメインスクリプト1本です。処理は送信 → 待ち → 受信の順です。
- リクエスト JSON の組み立てと、レスポンスからの取り出しは、そのスクリプトに書いてあります。共通化していません。
- `Assets/Common/Script/` は JSON 整形・APIキー読込・音声変換などのユーティリティです。細かいのでデモの流れを読むときは見なくてよいです。
- 改変するときはデモフォルダをコピーしてください。`Common` を変えると全デモに波及します。
