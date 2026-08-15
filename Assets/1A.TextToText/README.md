# 1A.TextToText

シリーズ全体の位置づけ → [Docs/demo-series-overview.md](../../Docs/demo-series-overview.md)

---

## このデモで学べること

- **テキストチャット**  
  テキストを送って、テキストで返してもらう基本のやり取り
- **会話コンテキスト**  
  これまでのやりとりをまとめて送り、続きの会話として扱う
- **System Instruction**  
  返答の方針や口調を、あらかじめ指示として渡す

---

## 事前準備

Google AI Studio から Gemini の API にアクセスするための APIキーを取得し、`Assets/Common/APIKey.txt` に保管してください。  
手順 → [Docs/gemini-ai-studio-setup.md](../../Docs/gemini-ai-studio-setup.md)  
無料枠で 429 が出たら、有料への移り方と値段の目安 → [Docs/gemini-api-pricing.md](../../Docs/gemini-api-pricing.md)

---

## 動かし方

Project ウィンドウで `Assets/1A.TextToText/TextToText.unity` を開き、Play を押してください。

### 1. チャットをしてみる

1. 左ペイン下部にメッセージを入力し、**送信** を押してください（Enter でも送信できます。Shift+Enter で改行です）。
2. 左に会話、中央に Request、右に Response が出ることを確認してください。
3. もう一度メッセージを送り、中央 Request の会話の履歴が増えることを見てください。

### 2. System Instruction になにかを入れてみる

1. 左ペイン上部の **System Instruction** に、たとえば「必ず一行で、やさしい言葉で答えてください」と入れてください。
2. さきほどと同じような質問をもう一度送ってください。
3. 答え方が変わったか、中央 Request に `"systemInstruction"` が入ったかを見てください。

### 3. コンテキストのオプションを外してみる

1. Message と Status のあいだの **Option** で、**コンテキストを送る** を OFF にしてください。
2. 「私の名前は太郎です」と送り、続けて「私の名前は？」と聞いてください。
3. 覚えていないように見えること、中央 Request にはいまの一文だけが載っていることを見てください。
4. 比較のため、Option を ON に戻して同じことを試し、差を見てください。

---

## Request と Response とは？

Web の API 呼び出しは「手紙のやりとり」に似ています。本文はどちらも **JSON というデータ形式** です。

- **Request（リクエスト）** … こちらから送る手紙。宛先の URL、認証（APIキー）、本文（いま何を言ったか）が入る
- **Response（レスポンス）** … 向こうからの返事。うまくいったか（HTTP の番号）と、AI の返答本文が入る

このデモのポイントは、その手紙を隠さず中央・右に出していることです。左の吹き出しだけ見ると「魔法のチャット」に見えますが、中央・右を見ると「こういう JSON で送っている／返ってきている」が追えます。

---

## System Instruction（事前指示）とは？

会話のメッセージとは別に、「答えるときの前提」をあらかじめ渡す欄です。

たとえ話で言うと、チャット本文が「今日の質問」なら、事前指示は「この人は丁寧語で、短く答えてください」のような **役割やルールのメモ** です。Gemini はこのメモを踏まえて、あとの質問に答えます。欄が空のときは Request に `"systemInstruction"` 自体が出ず、文字があるときだけ載ります。

試し方: 指示を「必ず一行で、やさしい言葉で答えてください」などに変え、同じ質問を送る。左の答え方と、中央 Request に事前指示が入っているかを見比べる。

---

## コンテキストとは？

一般にコンテキストとは、AI が「いまこの答えを出すために見ている範囲」のことです。会話の履歴、直前の指示、コードを書くときの開いているファイルやエラーメッセージなど、判断材料になる情報すべてがここに入ります。

モデルが一度に見られる量には上限があり、これを **コンテキストウィンドウ**（何トークンまで入るか）と呼びます。最近のコーディング向けモデルには数十万〜100万トークンを扱えるものも多く、大きなプロジェクトのコードや長い会話をまとめて渡せます。ただし上限を超えたぶんは切られたり要約されたりします。「全部覚えてくれている」わけではありません。

チャットで「さっきの名前、覚えてる？」が通じるのも、AI が勝手に記憶しているからではなく、**こちらがこれまでのやりとりを毎回一緒に送り直している**からです。このデモでは、その「一緒に送る履歴」をコンテキストと呼び、Option の **コンテキストを送る** で載せ方を切り替えられます。

| Option | 意味（体験） | API に載るもの | 左の吹き出し |
|--------|--------------|----------------|--------------|
| **ON** | いつものチャット。前の発言を踏まえて答える | これまでのやりとり全部 | そのまま増える |
| **OFF** | 毎回「初めまして」に近い状態 | **いま打った一文だけ** | 見た目のログとしては残る |

試し方:

1. Option を **ON** のまま「私の名前は太郎です」→「私の名前は？」と送る（覚えて答えるはず）
2. Option を **OFF** にして、同じように名前を伝えたあと「私の名前は？」と聞く（覚えていないように見えることが多い）
3. 毎回、中央 Request の `contents` が何件あるかを見比べる

---

## 主要クラス

### TextToText（[`TextToText.cs`](Script/TextToText.cs)）

デモの本体です。上から、送信後の流れを追うとわかりやすいです。

通信は **UnityWebRequest**（HTTP の送受信）と **コルーチン**（`IEnumerator` + `yield`）による **非同期処理** です。コルーチンは `Update` などのメインスレッドの処理とは独立した時間軸で進むので、応答待ちのあいだも画面が固まりません。

1. **起動時の準備をする**  
   `Start` — APIキー読込、`SystemInstruction.txt` → UI、送信ボタン／Enter の購読
2. **送信を始める**  
   `OnSendClicked` → `StartCoroutine(SendChatCoroutine)` — 送信の入口（直前に systemInstruction を同期）
3. **API と通信する**  
   `SendChatCoroutine` — 通信本体  
   - 履歴に user 追加 → `BuildRequestJson` → `UnityWebRequest` で POST  
   - `yield return request.SendWebRequest()` で応答待ち  
   - Response 表示 → テキスト抽出 → 履歴に model 追加
4. **リクエスト JSON を組み立てる**  
   `BuildRequestJson` — `systemInstruction`（空ならキーごと省略）と `contents` を組み立てる（Toggle OFF なら今回の user のみ）
5. **返答テキストを取り出す**  
   `TryExtractAssistantText` — レスポンス JSON から返答テキストを取り出す
6. **送受信を画面に出す**  
   `ShowRequest` / `ShowResponse` — 中央・右ペインへの可視化

### ChatBubble（[`ChatBubble.cs`](../Common/Script/ChatBubble.cs)）

左ペインの吹き出し1件分です（Prefab: [`Prefab/MessageBubble.prefab`](Prefab/MessageBubble.prefab)）。各デモが Prefab を `Instantiate` し、`SetMessage` で話者名・本文・背景色（user / assistant）を書き込みます。見た目の色分けだけを持つ小さなクラスで、通信ロジックは持ちません。`Assets/Common/Script/` に置き、吹き出し付きのデモから共有します。
