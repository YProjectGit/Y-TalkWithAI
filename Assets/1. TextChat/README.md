# 1. TextChat — Gemini テキストチャット（可視化付き）

シリーズ全体の位置づけ → [Docs/demo-series-overview.md](../../Docs/demo-series-overview.md)  
次のデモ（予定）: [`2. StructuredOutput`](../2.%20StructuredOutput/) — 決まった形（JSON）で返して Unity を動かす

## このデモで学べること

- Gemini Developer API（`generateContent`）へテキストを送り、返答を受け取る流れ
- 通常のチャット UI（吹き出し）と、リクエスト／レスポンスの生 JSON を同時に見る見方
- 複数ターンの会話が `contents` 配列として積み上がっていくこと

## 事前準備

1. APIキーの取得と `Assets/Common/APIKey.txt` への保管  
   → [Docs/gemini-ai-studio-setup.md](../../Docs/gemini-ai-studio-setup.md)
2. Unity でこのプロジェクトを開いていること
3. シーン内の UI（Canvas / EventSystem）がそろっていること（同梱済み）

## シーンの開き方と動かし方

1. Project ウィンドウで `Assets/1. TextChat/TextChat.unity` を開く
2. Play を押す
3. 左ペイン下部にメッセージを入力し、**送信** を押す
4. 左に会話、右に Status / Request / Response が出ることを確認する

うまくいかないとき:

- 右ペインに APIキー関連のエラー → Docs のキー手順を見直す
- HTTP 401 / 403 → キーの空白・種類（Auth）を確認
- HTTP 404 → モデル名が `gemini-3.6-flash` か確認（Inspector の `modelName`）

## 主要スクリプトの読み方

入口は [`TextChat.cs`](TextChat.cs) です。上から次の順で読むと流れが追えます。

1. `Start` — APIキー読込、送信ボタンの購読、初期表示
2. `OnSendClicked` → `SendChatCoroutine` — 送信の本体
3. `BuildRequestJson` — 会話履歴を Gemini 用 JSON に組み立てる
4. `UnityWebRequest` の POST — URL・ヘッダ（`x-goog-api-key`）・ボディ
5. `TryExtractAssistantText` — レスポンス JSON から返答テキストを取り出す
6. `ShowRequest` / `ShowResponse` — 右ペインへの可視化

吹き出し1件の見た目は [`ChatBubble.cs`](ChatBubble.cs)（Prefab: `MessageBubble.prefab`）です。

## 改変のヒント

触ってみるとわかりやすい箇所だけ挙げます。

1. **モデル名** — Inspector の `modelName` を変える（Docs で疎通確認済みの名前にする）
2. **バブルの色** — `MessageBubble` Prefab の `ChatBubble` にある `userColor` / `assistantColor`
3. **システムっぽい一文を足す** — 送信前に `turns` の先頭へ `role: "user"` の固定文を足す実験（API の `systemInstruction` ではなく、まずは contents で試すと追いやすい）
