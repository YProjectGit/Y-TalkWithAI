# 1. TextChat — Gemini テキストチャット（可視化付き）

シリーズ全体の位置づけ → [Docs/demo-series-overview.md](../../Docs/demo-series-overview.md)  
次のデモ（予定）: [`2. StructuredOutput`](../2.%20StructuredOutput/) — 決まった形（JSON）で返して Unity を動かす

## このデモで学べること

- Gemini Developer API（`generateContent`）へテキストを送り、返答を受け取る流れ
- 通信は **コルーチン**（`StartCoroutine` + `UnityWebRequest` + `yield`）で待ち、そのあいだ Status が点滅すること
- チャット／Request／Response の 3 ペインで、会話と生 JSON を同時に見る見方
- 複数ターンの会話が `contents` 配列として積み上がっていくこと（＝コンテキスト）
- **「コンテキストを送る」** を OFF にすると履歴が載らず、以前の発言を覚えていないように見えること
- **`systemInstruction`（事前指示）** を付けると、同じ質問でも返答の口調・形式が変わること

## 事前準備

1. APIキーの取得と `Assets/Common/APIKey.txt` への保管  
   → [Docs/gemini-ai-studio-setup.md](../../Docs/gemini-ai-studio-setup.md)
2. （任意）事前指示の編集 — [`Assets/Common/SystemInstruction.txt`](../Common/SystemInstruction.txt)  
   Play 開始時に左ペイン上部の入力欄へ読み込まれます。UI で編集して確定すると、このファイルへ書き戻されます
3. Unity でこのプロジェクトを開いていること
4. シーン内の UI（Canvas / EventSystem）がそろっていること（同梱済み）

## シーンの開き方と動かし方

1. Project ウィンドウで `Assets/1. TextChat/TextChat.unity` を開く
2. Play を押す
3. 左ペイン上部の **System Instruction** を確認する（空にすると `systemInstruction` は送られない）
4. Message と Status のあいだの **Option** で、**コンテキストを送る** が ON であることを確認する
5. 左ペイン下部にメッセージを入力し、**送信** を押す（Enter でも送信。Shift+Enter で改行）
6. 左に会話、中央に Request、右に Response が出ることを確認する（Status は左下。応答待ち中は点滅）  
   → Request の JSON に `"systemInstruction"` があるか見ると、事前指示の有無が一目でわかります  
   → もう一度送ると `contents` が増えること、Option を OFF にすると毎回 user 1件だけになることを見比べる

うまくいかないとき:

- Request / Response に APIキー関連のエラー → Docs のキー手順を見直す
- HTTP 401 / 403 → キーの空白・種類（Auth）を確認
- HTTP 404 → モデル名が `gemini-3.6-flash` か確認（Inspector の `modelName`）

## systemInstruction（事前指示）とは

モデルへの「口調・形式の前提」です。会話のやりとり（`contents`）とは **別枠** で送られます。

| 状態 | リクエストに何が載るか | 中央 Request での見え方 |
|------|------------------------|-------------------------|
| 欄が空 | `systemInstruction` キー自体を出さない | JSON に `"systemInstruction"` がない |
| 文字がある | `systemInstruction.parts[0].text` にその文言 | `"systemInstruction": { "parts": [ { "text": "..." } ] }` がある |

同期のタイミング:

- **起動時** … `Assets/Common/SystemInstruction.txt` → UI 欄
- **欄の編集を確定したとき** … UI → ファイルへ書き戻し
- **送信直前** … ファイル側に新しい変更があれば取り込み、そのあと UI を正として保存

試し方: 指示を「必ず一行で答えてください」などに変え、同じ質問を送る。返答の形と、中央 Request の `"systemInstruction"` の有無／内容を見比べる。

## 「コンテキストを送る」ON / OFF

ここでいうコンテキストは、これまでのやりとりを `contents` に載せて API に送ることです。

| Option | API に載る `contents` | 左の吹き出し | 内部の履歴 `turns` |
|--------|----------------------|--------------|-------------------|
| **ON** | これまでの user / model 全部 | そのまま増える | 送信のたびに増える |
| **OFF** | **今回の user 1件だけ** | そのまま残る（見た目のログ） | 成功後にクリア（次も単発） |

ポイント:

- 吹き出しが残っていても、OFF のときは **次の API 呼び出しには履歴が乗らない**
- 差は左ペインだけでなく、**中央 Request の `contents` の件数**で確認する

試し方:

1. Option を **ON** のまま「私の名前は太郎です」→「私の名前は？」と送る（覚えて答えるはず）
2. Option を **OFF** にして、同じように名前を伝えたあと「私の名前は？」と聞く（覚えていないように見えることが多い）
3. 毎回、中央 Request の `contents` 配列の長さを見比べる

## 主要スクリプトの読み方

入口は [`Script/TextChat.cs`](Script/TextChat.cs) です。上から次の順で読むと流れが追えます。

1. `Start` — APIキー読込、`SystemInstruction.txt` → UI、送信ボタン／Enter の購読
2. `OnSendClicked` → `StartCoroutine(SendChatCoroutine)` — 送信の入口（直前に指示テキストを同期）
3. `SendChatCoroutine` — 通信の本体（コルーチン）  
   - 履歴に user 追加 → `BuildRequestJson` → `UnityWebRequest` で POST  
   - `yield return request.SendWebRequest()` で応答待ち（Status 点滅）  
   - Response 表示 → テキスト抽出 → 履歴に model 追加
4. `BuildRequestJson` — 指示があれば `systemInstruction`、会話履歴を `contents` に組み立てる（Toggle OFF なら今回の user のみ）
5. `TryExtractAssistantText` — レスポンス JSON から返答テキストを取り出す
6. `ShowRequest` / `ShowResponse` — 中央・右ペインへの可視化

吹き出し1件の見た目は [`Script/ChatBubble.cs`](Script/ChatBubble.cs)（Prefab: [`Prefab/MessageBubble.prefab`](Prefab/MessageBubble.prefab)）です。

フォルダの見方: 直下はシーンと README のみ。実装は `Script/`、Prefab は `Prefab/`（その他リソース用の `Resource/` は中身があるときだけ）。

## 改変のヒント

触ってみるとわかりやすい箇所だけ挙げます。

1. **事前指示を変える** — UI か `SystemInstruction.txt` を編集し、同じ質問で返答と中央 Request の差分を見る
2. **コンテキスト ON/OFF** — 名前を伝えたあと「私の名前は？」と聞き、Toggle で差を見る（Request の `contents` も確認）
3. **モデル名** — Inspector の `modelName` を変える（Docs で疎通確認済みの名前にする）
4. **バブルの色** — `Prefab/MessageBubble` の `ChatBubble` にある `userColor` / `assistantColor`
