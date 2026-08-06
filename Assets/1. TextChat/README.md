# 1. TextChat — Gemini テキストチャット（可視化付き）

シリーズ全体の位置づけ → [Docs/demo-series-overview.md](../../Docs/demo-series-overview.md)  
次のデモ（予定）: [`2. StructuredOutput`](../2.%20StructuredOutput/) — 決まった形（JSON）で返して Unity を動かす

## このデモで学べること

- Gemini Developer API（`generateContent`）へテキストを送り、返答を受け取る流れ
- 通常のチャット UI（吹き出し）と、リクエスト／レスポンスの生 JSON を同時に見る見方
- 複数ターンの会話が `contents` 配列として積み上がっていくこと
- **`systemInstruction`（事前指示）** を付けると、同じ質問でも返答の口調・形式が変わること

## 事前準備

1. APIキーの取得と `Assets/Common/APIKey.txt` への保管  
   → [Docs/gemini-ai-studio-setup.md](../../Docs/gemini-ai-studio-setup.md)
2. （任意）事前指示の編集 — [`Assets/Common/SystemInstruction.txt`](../Common/SystemInstruction.txt)  
   Play 開始時に左ペイン上部の入力欄へ読み込まれます。UI で編集してもこのファイルへ書き戻されます
3. Unity でこのプロジェクトを開いていること
4. シーン内の UI（Canvas / EventSystem）がそろっていること（同梱済み）

## シーンの開き方と動かし方

1. Project ウィンドウで `Assets/1. TextChat/TextChat.unity` を開く
2. Play を押す
3. 左ペイン上部の **System Instruction** を確認する（空にすると `systemInstruction` は送られない）
4. 左ペイン下部にメッセージを入力し、**送信** を押す
5. 左に会話、右に Status / Request / Response が出ることを確認する  
   → Request の JSON に `"systemInstruction"` があるか見ると、事前指示の有無が一目でわかります

うまくいかないとき:

- 右ペインに APIキー関連のエラー → Docs のキー手順を見直す
- HTTP 401 / 403 → キーの空白・種類（Auth）を確認
- HTTP 404 → モデル名が `gemini-3.6-flash` か確認（Inspector の `modelName`）

## 主要スクリプトの読み方

入口は [`Script/TextChat.cs`](Script/TextChat.cs) です。上から次の順で読むと流れが追えます。

1. `Start` — APIキー読込、`SystemInstruction.txt` → UI、送信ボタンの購読
2. `OnSendClicked` → `SendChatCoroutine` — 送信の本体（直前に指示テキストを同期）
3. `BuildRequestJson` — 指示があれば `systemInstruction`、会話履歴を `contents` に組み立てる
4. `UnityWebRequest` の POST — URL・ヘッダ（`x-goog-api-key`）・ボディ
5. `TryExtractAssistantText` — レスポンス JSON から返答テキストを取り出す
6. `ShowRequest` / `ShowResponse` — 右ペインへの可視化

吹き出し1件の見た目は [`Script/ChatBubble.cs`](Script/ChatBubble.cs)（Prefab: [`Prefab/MessageBubble.prefab`](Prefab/MessageBubble.prefab)）です。

フォルダの見方: 直下はシーンと README のみ。実装は `Script/`、Prefab は `Prefab/`（その他リソース用の `Resource/` は中身があるときだけ）。

## 改変のヒント

触ってみるとわかりやすい箇所だけ挙げます。

1. **事前指示を変える** — UI か `SystemInstruction.txt` を編集し、同じ質問で返答と右ペイン Request の差分を見る
2. **モデル名** — Inspector の `modelName` を変える（Docs で疎通確認済みの名前にする）
3. **バブルの色** — `Prefab/MessageBubble` の `ChatBubble` にある `userColor` / `assistantColor`
