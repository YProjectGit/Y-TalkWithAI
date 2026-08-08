# 1B.TextToJSON

シリーズ全体の位置づけ → [Docs/demo-series-overview.md](../../Docs/demo-series-overview.md)

---

## このデモで学べること

- **構造化出力**  
  AIからのレスポンスを「文章」ではなく、「あるフォーマットに沿ったデータ（JSON）」として受け取る
- **スキーマ**  
  返してほしい JSON の構造を先に定義する
- **パース**  
  返ってきた JSON を読み取り、プログラムの動作につなげる

---

## 動かし方

Project ウィンドウで `Assets/1B.TextToJSON/TextToJSON.unity` を開き、Play を押してください。

### 1. 文字を送信すると、キューブの色が変わるのを見る

1. 入力欄に、たとえば「キューブは夕焼けのオレンジ、背景は夜の紺」と入れて **送信** を押してください。
2. 画面上の **3D キューブの色** と **カメラの背景色** が変わることを確認してください。

### 2. Responseの内容と色が対応しているのを確認する

1. 右ペインの Response を開き、自由文ではなく **項目の並んだ JSON** が返ってきていることを確認してください。
2. `cubeColor` の `r` / `g` / `b` とキューブの色、`backgroundColor` と背景色が対応しているかを見比べてください。

### 3. Schema とResponseの対応を確認する

1. 中央ペインの **Schema** を開き、返してほしい構造（`cubeColor` と `backgroundColor` など）が書かれていることを確認してください。
2. さきほど見た Response の JSON が、この Schema と同じ構造になっているかを見比べてください。

---

## 構造化出力とは？

AI に自由文で答えてもらうのではなく、**あらかじめ決められたフォーマットのデータ（JSON）** として返してもらうやり方です。

たとえ話で言うと、自由文のチャットが「作文」なら、構造化出力は **記入欄のある用紙** です。作文だとプログラムは文の意味を読み解く必要がありますが、用紙なら「キューブの色」の欄を見れば済みます。このデモでは、その欄が `cubeColor` や `backgroundColor` といったキーにあたります。

このデモのポイントは、返ってきた JSON をプログラムが解釈し、文字の表示ではなく **キューブや背景の色** として画面に反映していることです。TextToText が「返事を読んで見せる」なら、ここでは **返事でプログラムを動かす** が体験の中心です。

---

## スキーマとは？

返してほしい JSON の構造（キー名・型・入れ子など）を、リクエスト時に先に指定しておく定義です。さきほどのたとえで言うと、**記入用紙のフォーマット**（どの欄があるか）にあたります。

このデモのスキーマは、Unity の `Color` に合わせて各色を **0〜1 の r / g / b** で受け取る形です。

```json
{
  "cubeColor": { "r": 1.0, "g": 0.5, "b": 0.2 },
  "backgroundColor": { "r": 0.1, "g": 0.15, "b": 0.25 }
}
```

- `cubeColor` … キューブの色
- `backgroundColor` … カメラの背景色

Gemini へのリクエストにこのスキーマを含めることで、モデルは自由文ではなく、指定した構造の JSON を返す方向に寄ります。中央の Schema ペインには、いま送っているスキーマが表示されます。

| 返し方 | Unity 側でやりやすいこと |
|--------|--------------------------|
| **自由文** | そのまま表示する（TextToText） |
| **構造化** | キーを読んで色などに割り当てる（このデモ） |

試し方: 送信後、中央 Schema と右 Response の JSON が同じ構造になっているかを見比べる。

---

## パースとは？

レスポンスとして届いた文字列（JSON）を読み、プログラムが使える値に分解することです。

このデモでは、右に出ている JSON から `cubeColor` と `backgroundColor` を取り出し、キューブのマテリアル色とカメラの背景色に書き込みます。パースに成功すると画面が変わり、形が崩れていると反映できない、という対応関係を追うのがポイントです。

試し方: Response の `r` / `g` / `b` と、キューブ／背景の見え方を一項目ずつ対応させて見る。

---

## 発展課題

教材の初期実装は **色（キューブ＋背景）だけ** です。スキーマとパース／反映の両方を直して、次を足してみてください。

- **サイズ** … `scale` を受け取り、キューブの `localScale` へ反映する
- **回転** … `rotationSpeed` を受け取り、毎フレーム回転させる

片方だけ変えても、Response に値は出ても画面は動きません。通信まわり（`UnityWebRequest` など）は触らなくて構いません。

### スキーマのつくり方（例: scale）

`TextToJSON.cs` の `ResponseSchemaJson` に、プロパティと `required` を足します。

```json
"scale": {
  "type": "NUMBER",
  "description": "Uniform scale of the cube. Typical range 0.2 to 3."
}
```

`required` にも `"scale"` を追加します。

### プログラムでの対応（例: scale）

1. **受け皿クラスを増やす**  
   `StructuredColors` に `public float scale;` を足す
2. **パース結果を反映する**  
   `TryParseAndApply` で `data.scale` を読み、`cubeTransform.localScale = Vector3.one * data.scale;` のように書く

回転なら、受け取った値をフィールドに保持し、`Update` で `Rotate` する、という流れになります。

### エージェントへの指示例

エージェントに頼むときは、「スキーマ」と「反映コード」の両方を明示するとよいです。

```
Assets/1B.TextToJSON/Script/TextToJSON.cs を改修してください。

追加したいフィールド:
- scale (NUMBER): キューブの均一スケール。0.2〜3 程度
- rotationSpeed (NUMBER): Y 軸まわりの回転速度（度/秒）

やること:
1. ResponseSchemaJson に上記プロパティと required を追加する
2. StructuredColors に対応するフィールドを追加する
3. TryParseAndApply で scale を localScale に反映する
4. rotationSpeed は保持し、Update で毎フレーム回転させる
5. 通信まわりは変更しない

既存の cubeColor / backgroundColor の挙動は維持してください。
```

---

## 主要クラス

### TextToJSON（[`TextToJSON.cs`](Script/TextToJSON.cs)）

デモの本体です。上から、送信後の流れを追うとわかりやすいです。

通信は **UnityWebRequest**（HTTP の送受信）と **コルーチン**（`IEnumerator` + `yield`）による **非同期処理** です。コルーチンは `Update` などのメインスレッドの処理とは独立した時間軸で進むので、応答待ちのあいだも画面が固まりません。TextToText との違いは、返答テキストを吹き出しに足すのではなく、**JSON をパースしてキューブ色と背景色へ反映する**ところです。

1. **起動時の準備をする**  
   `Start` — APIキー読込、送信ボタン／Enter の購読、キューブとカメラの参照
2. **送信を始める**  
   `OnSendClicked` → `StartCoroutine(SendStructuredCoroutine)` — 送信の入口
3. **API と通信する**  
   `SendStructuredCoroutine` — 通信本体  
   - `BuildRequestJson` でスキーマ込みのリクエストを組み立てる  
   - `UnityWebRequest` で POST → `yield return` で応答待ち  
   - 構造化 JSON の表示 → パース → 色の反映
4. **リクエスト JSON を組み立てる**  
   `BuildRequestJson` — ユーザー入力と、返してほしい形（スキーマ）を載せる
5. **構造化の返答を取り出す**  
   `TryExtractJson` — レスポンスから構造化された JSON 本文を取り出す
6. **パースして画面へ反映する**  
   `TryParseAndApply` — `cubeColor` / `backgroundColor` を読み、キューブとカメラ背景に書く  
   `ShowSchema` / `ShowResponse` — 中央・右ペインへの可視化
