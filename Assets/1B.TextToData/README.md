# 1B. TextToData

![text-to-data](../Docs/Image/text-to-data.png)

<br/>

AIからの返答を、文章ではなく**プログラム内で解釈できるJSON形式データ**として受け取るアプリケーションです。

入力した言葉のイメージに合わせて、3Dキューブと背景の色を変えます。

<br/>

---

## このデモで学ぶこと

<br/>

- ### 構造化出力

  AIからのレスポンスを文章ではなく、決められたフォーマットのJSONとして受け取る方法を学びます。

- ### スキーマ

  返してほしいJSONの項目や型を、リクエストの中であらかじめ指定する方法を学びます。

- ### パース

  返ってきたJSONをプログラムで読み取り、画面上の色の変化へつなげる流れを学びます。

<br/>

---

## 動かしてみる

<br/>

Project ウィンドウで `Assets/1B.TextToData/TextToData.unity` を開き、Playしてください。

### 1. 文字を送って色を変える

1. 入力欄に、たとえば「キューブは夕焼けのオレンジ、背景は夜の紺」と入力し、**送信** を押してください。
2. 画面上の3Dキューブと背景の色が変わることを確認してください。
3. 別の色や雰囲気を入力し、返答に応じて結果が変わることを試してください。

### 2. Responseと画面の色を見比べる

1. 右ペインの **Response** を見て、文章ではなく項目の並んだJSONが返っていることを確認してください。
2. `cubeColor` の `r` / `g` / `b` と、3Dキューブの色を見比べてください。
3. `backgroundColor` の `r` / `g` / `b` と、背景の色を見比べてください。

### 3. SchemaとResponseを見比べる

1. 中央ペインの **Schema** を見て、`cubeColor` と `backgroundColor` が定義されていることを確認してください。
2. 各色の中に、数値を入れる `r` / `g` / `b` が定義されていることを確認してください。
3. 右ペインのResponseが、中央ペインのSchemaと同じ構造になっていることを見比べてください。

<br/>

---

## 前提知識

<br/>

### 構造化出力

-　AIに自由な文章で答えてもらうのではなく、**あらかじめ決めたフォーマットのデータ**として返してもらう方法です。

-　自由文のチャットが「作文」なら、構造化出力は「**記入欄のある用紙**」にたとえられます。作文は内容を読んで意味を判断する必要がありますが、記入欄が決まっていれば、プログラムは必要な項目を直接探せます。

-　データのフォーマットは、**スキーマ**によって定義します。

-　このデモでは、`cubeColor` と `backgroundColor` という項目を持つJSONを受け取り、返答を文章として表示するのではなく、キューブと背景の色として画面に反映します。

<br/>

---

## スキーマ

<br/>

**スキーマ**とは、返してほしいJSONの構造をあらかじめ定めたものです。JSONに含めるキー、値の型、入れ子の構造などを指定します。このデモでは、キューブと背景の色を0.0〜1.0fの `r` / `g` / `b` で受け取るように指定しています。実際にGemini APIへ送っているスキーマは次のとおりです。

<br/>

**リクエストに含めているスキーマ**

```json
{
  "type": "OBJECT",
  "properties": {
    "cubeColor": {
      "type": "OBJECT",
      "description": "Color of the 3D cube. Each of r,g,b is 0 to 1.",
      "properties": {
        "r": {
          "type": "NUMBER"
        },
        "g": {
          "type": "NUMBER"
        },
        "b": {
          "type": "NUMBER"
        }
      },
      "required": [
        "r",
        "g",
        "b"
      ]
    },
    "backgroundColor": {
      "type": "OBJECT",
      "description": "Camera background color. Each of r,g,b is 0 to 1.",
      "properties": {
        "r": {
          "type": "NUMBER"
        },
        "g": {
          "type": "NUMBER"
        },
        "b": {
          "type": "NUMBER"
        }
      },
      "required": [
        "r",
        "g",
        "b"
      ]
    }
  },
  "required": [
    "cubeColor",
    "backgroundColor"
  ]
}
```

1. **`type`**  
   その項目の型です。`OBJECT` は複数の項目をまとめた構造、`NUMBER` は数値を表します。
2. **`properties`**  
   オブジェクトの中に含める項目を定義します。
3. **`required`**  
   レスポンスに必ず含めてほしい項目を指定します。
4. **`description`**  
   その項目が何を表すか、モデルへ文章で説明します。

<br/><br/>

**スキーマに沿ったレスポンスの例**

```json
{
  "cubeColor": {
    "r": 1.0,
    "g": 0.5,
    "b": 0.2
  },
  "backgroundColor": {
    "r": 0.1,
    "g": 0.15,
    "b": 0.25
  }
}
```

スキーマで `required` に指定した `cubeColor` と `backgroundColor` が含まれ、それぞれの中に `NUMBER` 型の `r` / `g` / `b` が入っています。このJSONをパースし、各数値をUnityの色へ変換します。

<br/>

### responseMimeType

- `responseMimeType` は、AIからのレスポンスをどのデータ形式で受け取りたいか指定する設定です。このデモでは、JSONを表す `application/json` を指定しています。

```json
"responseMimeType": "application/json"
```

- `responseMimeType` がレスポンスの**データ形式**を指定し、`responseSchema` がJSONの**中身の構造**を指定します。この2つを組み合わせることで、プログラムが読み取りやすいJSONをGeminiから受け取ります。

<br/>

---

## パース

<br/>

- **パース**とは、受け取った文字列を読み取り、プログラムで使えるデータへ変換することです。

- Gemini APIから届いた時点のJSONは、文字列として扱われます。このデモでは `JsonUtility` を使ってJSONを読み、`cubeColor` と `backgroundColor` を数値として取り出します。

- 取り出した `r` / `g` / `b` をUnityの `Color` に変換し、キューブのマテリアル色とカメラの背景色へ書き込むことで、AIからの返答が画面上の変化につながります。

<br/>

---

## コードの解説

<br/>

### TextToData（[`TextToData.cs`](Script/TextToData.cs)）

<br/>

デモの本体です。上から、送信後の流れを追うとわかりやすいです。

通信は **UnityWebRequest**（HTTPの送受信）と **コルーチン**（`IEnumerator` + `yield`）による **非同期処理** です。コルーチンは `Update` などのメインスレッド処理とは独立した時間軸で進むので、応答待ちのあいだも画面が固まりません。

<br/>

1. **起動時の準備をする**  
   `Start` — APIキーの読込、送信ボタン／Enterの購読、Schemaの初期表示
   <br/>
2. **送信を始める**  
   `OnSendClicked` → `StartCoroutine(SendStructuredCoroutine)` — 入力を確認し、API通信を始める
   <br/>
3. **APIと通信する**  
   `SendStructuredCoroutine` — 通信本体  
   - `BuildRequestJson` でユーザー入力とスキーマを含むリクエストを組み立てる
   - `UnityWebRequest` でGemini APIへPOSTする
   - `yield return request.SendWebRequest()` で応答を待つ
   - 構造化されたJSONを取り出し、画面へ反映する
   <br/>
4. **リクエストJSONを組み立てる**  
   `BuildRequestJson` — `responseMimeType` と `responseSchema` を `generationConfig` に載せる
   <br/>
5. **構造化された返答を取り出す**  
   `TryExtractStructuredJson` — レスポンスの `candidates[0].content.parts[0].text` からJSON文字列を取り出す
   <br/>
6. **JSONをパースして色を変える**  
   `TryParseAndApply` — `cubeColor` / `backgroundColor` を読み、キューブとカメラ背景へ適用する
   <br/>
7. **スキーマと応答を画面に出す**  
   `ShowSchema` / `ShowResponse` — 中央・右ペインへ見やすく整形して表示する

<br/>

### 共通ライブラリ（`Assets/Common/Script/`）

<br/>

このデモが使っている共通のライブラリです。シンプルなユーティリティクラスなので、**上の流れを追うときに中身を読む必要はありません。**

| ファイル | 中身 |
|---|---|
| [`GeminiJson`](../Common/Script/GeminiJson.cs) | JSONのエスケープ・整形・省略表示 |
| [`GeminiKey`](../Common/Script/GeminiKey.cs) | APIキーの読込・マスク・generateContentのURL |
| [`ResponseTime`](../Common/Script/ResponseTime.cs) | 送信から返信までの経過時間をConsoleへ表示 |

これらは他のデモも使っています。挙動を変えたくなったらCommonを直さず、そのファイルをこのデモの `Script/` にコピーしてクラス名を変えてください。
