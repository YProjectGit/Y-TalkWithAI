# 1A. TextToText

  <br/>


基本的な「AIチャット」のアプリケーションです。

テキストをGeminiのAPIへ送信すると、AIからの返答がテキストで表示されます。

学習のため、通信の生データが見えるようになっています。

<br/>

---

## このデモで学ぶこと

  <br/>

- ### Web APIの基本：リクエストとレスポンス

  Gemini APIへのリクエストとしてテキストを送信し、テキストでレスポンスを得て、それを解釈するまでの基本的なプロセスを知ってください。

- ### System Instruction  

  返答の方針や口調を、あらかじめ指定する指示ドキュメントを「SystemInstruction」といいます。GeminiAPIの中で、これを指定する方法を学びます。

- ### コンテキスト  

  まるで自分とAIとで、一連の会話が行われているように感じる仕組みとして、「コンテキスト」があります。このコンテキストとは何かが解ると、AIのコミュニケーションへの理解が深まります。

<br/>

---

## 事前準備

<br/>

### APIキーの取得

- GeminiAPIにアクセスするには、Google AI Studioのアカウントを取得し、自分がGemini API にアクセスするための **APIキー**を取得する必要があります。
- アカウント取得からAPIキーの取得するまでの手順はこちらを参照してください。
  [Assets/Docs/gemini-ai-studio-setup.md](../Docs/gemini-ai-studio-setup.md)  
- APIキーを取得したら、UnityEditor内にある
  **`Assets/Common/APIKey.txt`** 
  にAPIキーをコピペして補完してください。 
- APIキーは、**自分の課金でAPIに自由にアクセスできるチケット**なので、絶対に他人に見せないでください。アプリを公開することも絶対しないでください。容易にハッキングされます。
- もし、APIKeyが他人に漏れるような事態になれば **Google AI Studio でAPIKeyを削除し、作り直してください**。 



<br/>

---

## 動かしてみる

 <br/>

Project ウィンドウで `Assets/1A.TextToText/TextToText.unity` を開き、Playしてください。

### 1. チャットをしてみる

1. 左ペイン下部にメッセージを入力し、**送信** を押してください
   （Enterキーでも送信できます。Shift+Enter で改行です）。
2. 左に会話、中央にGeminiへのRequest、右に GeminiからのResponse が表示されることを確認してください。
3. もう一度メッセージを送り、中央 Request の会話の履歴が増えることを見てください。

### 2. System Instruction になにかを入れてみる

1. 左ペイン上部の **System Instruction** に、たとえば「必ず一行で、元気な関西弁で答えてください」と入れてください。
2. さきほどと同じような質問をもう一度送ってください。
3. 答え方が変わったか、中央 Request に `"systemInstruction"` がどのように入ったかを見てください。

### 3. コンテキストのオプションを外してみる

1. Message と Status のあいだの **Option** で、**コンテキストを送る** を OFF にしてください。

2. 「私の名前は太郎です」と送り、続けて「私の名前は？」と聞いてください。

3. 覚えていないように見えること、中央 Request にはいまの一文だけが載っていることを見てください。

4. 比較のため、Option を ON に戻して同じことを試し、差を見てください。

<br/>

------

## 基礎知識

<br/>

### インターネット

世界中のコンピューター端末同士を相互に接続し、定められた**通信プロトコル**によって、データを送受信する仕組み。

 <br/>

### 通信プロトコル

通信のルール・取り決めのこと。

インターネット上の通信は、各レイヤーにおける、さまざまなプロトコルによって成立している。

| 層                   | 役割                                   | 主なプロトコル                                               |
| :------------------- | :------------------------------------- | :----------------------------------------------------------- |
| アプリケーション層   | ユーザの利用に適したサービスを提供する | **HTTP**（ウェブ閲覧）<br />**SMTP**（メール送受信） <br />**WebSocket**（チャットなど双方向通信） |
| **トランスポート層** | データの届け方を管理                   | **TCP**（確認手続き多いけど確実） <br />**UDP**（速さ重視で多少のロスはOK） |
| **インターネット層** | 通信の経路を決定                       | **IP**（通信の経路を決める。ルーティング）                   |
| **リンク層**         | 物理的な送受信を担当                   | **Ethernet**（LANケーブル）<br /> **Wi-Fi**（無線LAN）       |

 <br/>

## HTTP

- **HyperText Transfer Protocol** の略
- ウェブページのHTMLテキストなどを伝送するためにつくられた
- URLに「http://」と書かれている謎の呪文の正体
- クライアントとサーバーの間で「リクエスト／レスポンス」をやり取りするための仕組み

<br/>

## リクエストとレスポンス

**リクエストは「要求」で、レスポンスは「応答」**

1. **クライアント**（例：自分のブラウザ） が**サーバー**（例：アメリカにあるAmazonのサーバー）に対して HTTP リクエストを送る
2. サーバー はリクエストを処理し、HTTP レスポンスを返送
3. クライアントはレスポンスを受け取り、HTML を描画したり画像を表示したりする

 <br/>

### 主な HTTP リクエスト

#### **GET**

- **データを「取りにいく」リクエスト**
  - パラメータ（検索ワードなど）は URL の末尾に `?` でつける
  - ブラウザのアドレスバーや履歴に残る

#### **POST**

- **データを「送信する」リクエスト**
  - 送る情報（フォームの内容など）はリクエスト本文（ボディ）に入れる
  - 送信内用はURL には表示されず、詳しい中身は隠せる


<br/>

## Web API

- **API = Application Programming Interface**

- インターネットを介した**「関数」**のような役割を担う仕組みのこと。
- 自分のプログラムからインターネット上のサーバーにリクエストを送り、サーバが処理した結果をレスポンスとして返してくれる。
- HTMLのような表示レイアウトを含んだ情報ではなく「**データの中身だけ**」をやりとりする。
- そのデータ形式として、**JSON**やXMLのようなテキストフォーマットが使われる。
- 利用には認証が必要な場合が多く、**API キー**などによってアクセスが制限される。

<br/>

例：Gemini3.1（Google Cloudの大規模言語モデル）のAPIへにアクセスするURL

`https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-flash-lite:generateContent?key=(自分のAPIキー)`

<br/>

## **JSON**

- **JavaScript Object Notation** の略

- データ交換のためのシンプルなテキスト形式。Web API では、標準的に用いられている。

- **オブジェクト**（キーと値のペア）や**配列**（順序付きリスト）を入れ子構造で表現できる。


<br/>

### 基本構文

```json
{
	"date": {
    "month": 9
    "day": 25
  }
  
  "users": [
    { "id": 1, "name": "Alice" },
    { "id": 2, "name": "Bob" }
  ],
  
}
```

1. **オブジェクト**（`{ }`）
   - 複数のキーと値を `{ "key": 値, ... }` でまとめる
2. **配列**（`[ ]`）
   - 複数の値を順序付きで `[ 値1, 値2, ... ]` でまとめる
3. **値の型**
   - 文字列（`"文字列"`）、数値（`123`）、真偽値（`true`/`false`）、`null` も可能

 <br/>

**GeminiへのリクエストのJSON（最小限）**

```json
{
    "contents": [
    {
      "role": "user",
      "parts": [
        {
          "text": "こんにちは、今日の天気を教えてください。"
        }
      ]
    }
  ]
}
```

  <br/>

1. **`contents`**
   会話の配列。1回の発言が1件のオブジェクトになる。
2. **`role`**
   誰の発言か。`user` が自分、`model` が AI。
3. **`parts`**
   その発言の部品。このデモではテキスト1つだけを入れる。
4. **`text`**
   実際の文章。

  <br/>

**GeminiからのレスポンスのJSON（最小限）**

```json
{
  "candidates": [
    {
      "content": {
       "role": "model"
        "parts": [
          {
            **"text": "今日の東京の天気は、晴れです。最高気温は20℃、最低気温は9℃です。"**
          }
        ],
      },
      "finishReason": "STOP",
      "avgLogprobs": -0.22494593262672424
    }
  ],
  "usageMetadata": {
    "promptTokenCount": 7,
    "candidatesTokenCount": 32,
    "totalTokenCount": 39,
    "promptTokensDetails": [
      {
        "modality": "TEXT",
        "tokenCount": 7
      }
    ],
    "candidatesTokensDetails": [
      {
        "modality": "TEXT",
        "tokenCount": 32
      }
    ]
  },
  "modelVersion": "gemini-3.1-flash-lite",
  "responseId": "XweQaLybIregnvgPpaC-qQI"
}
```

<br/>

1. **`candidates`**
   候補の答えの配列。このデモでは先頭（`candidates[0]`）のテキストを使う。
2. **`content`**
   モデルの返答。中の `role` / `parts` / `text` はリクエストと同じ形。
4. **`usageMetadata`**
   使ったトークン数（入力・出力・合計）。

<br/>

---

## System Instruction（事前指示）

  <br/>

- 返答の方針や口調を、あらかじめ指定する（例：「関西弁で答えて」など）指示文を**SystemInstruction**といいます。

- Gemini APIへのリクエストJSONで以下のように指定されます。

  <br/>

**System Instructionを入れたリクエストのJSON**

```json
{
  "systemInstruction": {　// ← SytemInstrunctionの指定
    "parts": [
      {
        "text": "必ず一行で、元気な関西弁で答えてください"
      }
    ]
  },
  "contents": [
    {
      "role": "user",
      "parts": [
        {
          "text": "こんにちは、今日の天気を教えてください。"
        }
      ]
    }
  ]
}
```

<br/>

---

## コンテキスト

<br/>

- **コンテキスト**とは、AI が「**いまこの答えを出すために見ている範囲**」のことです。会話の履歴、直前の指示、添付ファイルなど、判断材料になる情報すべてが対象です。

- 自分が前に話したことをAIが覚えているように見えるのは、AIが勝手に内部で会話記憶しているからではなく、**メッセージの送信のたびに、これまでの会話履歴をコンテキストとして全て送り直している**からです。

- このデモでは 「**コンテキストを送る**」オプションのON／OFFで、コンテキストとして会話履歴を送信するかどうかを切り替えることが出来ます。

- デフォルトはONですが、これをOFFにするとAIはチャットの度に記憶喪失になったかのようにふるまいます。

  

<br/>

**コンテキスト（会話履歴）ありのGeminiへのリクエストのJSON**

```json
{
  "contents": [
    {
      "role": "user",
      "parts": [
        {
          "text": "私の好きな動物は犬です。覚えておいてね。"
        }
      ]
    },
    {
      "role": "model",
      "parts": [
        {
          "text": "了解しました！ 承知いたしました。あなたの好きな動物は犬ですね。 覚えておきます。"
        }
      ]
    },
    {
      "role": "user",
      "parts": [
        {
          "text": "私の好きな動物はなんですか？"
        }
      ]
    }
  ]
}
```

<br/>

**コンテキストなしのGeminiへのリクエストのJSON**

```json
{
  "contents": [
    {
      "role": "user",
      "parts": [
        {
          "text": "私の好きな動物はなんですか？"
        }
      ]
    }
  ]
}
```



<br/>

- モデルが一度に見られる量には上限があり、これを **コンテキストウィンドウ**（何トークンまで入るか）と呼びます。
- 最近のコーディング向けモデルには**数十万〜100万トークン**を扱えるものも多く、大きなプロジェクトのコードや長い会話をまとめて渡せます。ただし上限を超えたぶんは切られたり要約されたりします。

<br/> 

---

## コードの解説

<br/>

### TextToText（[`TextToText.cs`](Script/TextToText.cs)）

<br/>

デモの本体です。上から、送信後の流れを追うとわかりやすいです。

通信は **UnityWebRequest**（HTTP の送受信）と **コルーチン**（`IEnumerator` + `yield`）による **非同期処理** です。コルーチンは `Update` などのメインスレッドの処理とは独立した時間軸で進むので、応答待ちのあいだも画面が固まりません。

<br/>

1. **起動時の準備をする**  
   `Start` — APIキー読込、`SystemInstruction.txt` → UI、送信ボタン／Enter の購読
   <br/>
2. **送信を始める**  
   `OnSendClicked` → `StartCoroutine(SendChatCoroutine)` — 送信の入口（直前に systemInstruction を同期）
   <br/>
3. **API と通信する**  
   `SendChatCoroutine` — 通信本体  
   - 履歴に user 追加 → `BuildRequestJson` → `UnityWebRequest` で POST  
   - `yield return request.SendWebRequest()` で応答待ち  
   - Response 表示 → テキスト抽出 → 履歴に model 追加
     <br/>
4. **リクエスト JSON を組み立てる**  
   `BuildRequestJson` — `systemInstruction`（空ならキーごと省略）と `contents` を組み立てる（Toggle OFF なら今回の user のみ）
   <br/>
5. **返答テキストを取り出す**  
   `GeminiTextResponse.TryExtractText` — レスポンス JSON から返答テキストを取り出す（共通スクリプト）
   <br/>
6. **送受信を画面に出す**  
   `ShowRequest` / `ShowResponse` — 中央・右ペインへの可視化

<br/>

### 共通ライブラリ（`Assets/Common/Script/`）

<br/>

このデモが使っている共通のライブラリです。シンプルなユーティリティクラスなので**上の流れを追うときに中身を読む必要はありません。**

| ファイル | 中身 |
|---|---|
| [`GeminiJson`](../Common/Script/GeminiJson.cs) | JSON のエスケープ・整形・省略表示 |
| [`GeminiKey`](../Common/Script/GeminiKey.cs) | APIキーの読込・マスク・generateContent の URL |
| [`GeminiTextResponse`](../Common/Script/GeminiTextResponse.cs) | レスポンスから candidates[0] のテキストを取り出す |
| [`ChatBubble`](../Common/Script/ChatBubble.cs) | 吹き出し1件分の見た目（Prefab: [`MessageBubble.prefab`](Prefab/MessageBubble.prefab)） |

これらは他のデモも使っています。挙動を変えたくなったら Common を直さず、そのファイルをこのデモの `Script/` にコピーしてクラス名を変えてください。