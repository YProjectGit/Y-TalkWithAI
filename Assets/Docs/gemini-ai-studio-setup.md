# Google AI Studio のセットアップ

<br/>

Gemini API にアクセスするための準備です。
GoogleのAI関連の開発者向けサービス**Google AI Studio** でアカウントをつくります。
そこでGemini APIにアクセスするための**APIキー**を1本取得して、Unity から使えるようにします。

<br/>

---

## 必要なもの

<br/>

- Google アカウント（既存のもので結構です）
- 最初は無料枠で利用できます。無料枠の上限に達したら、有料枠に移行する必要があります。
-  支払いについては、[gemini-api-pricing.md](gemini-api-pricing.md) を見てください。

<br/>

---

## ログイン

<br/>

1. [https://aistudio.google.com](https://aistudio.google.com) を開いてください。
2. **GetStarted**などのボタンから、Google アカウントでログインしてください。
3. 初回は利用規約に同意してください。

<br/>

---

## APIキーをつくる

<br/>

1. 左サイドバー下部の **鍵アイコンのボタン（Get API Key）** を開いてください。
   （直リンク: [https://aistudio.google.com/app/apikey](https://aistudio.google.com/app/apikey)）

   ![image-20260826141526144](C:\Users\yugo\AppData\Roaming\Typora\typora-user-images\image-20260826141526144.png)

2. **Create API key** を押してください。

3. 初めてなら **Create API key in new project** を選んでください。

4. 発行されたキーを **Copy** してください。

この場で新しく作ってください。古いキーの使い回しは避けてください。

今作ると Auth キー（多くは `AQ.` 始まり）になります。`AIza...`（Standard）は不要です。一覧の Key Type が `Auth` なら OK です。

<br/>

---

## APIキーを保管する

<br/>

![image-20260826141717403](C:\Users\yugo\AppData\Roaming\Typora\typora-user-images\image-20260826141717403.png)

1. `Assets/Common/APIKey.txt` の `Paste Your APIKey Here…` を、自分のキー1行に置き換えてください（前後の空白に注意）。
2. 本物のキーは Git にコミットしないでください。

- APIキーは、**自分の課金で API に自由にアクセスできるチケット**です。絶対に他人に見せないでください（SNS・チャット・スクリーンショット・公開リポジトリ禁止）。アプリを公開することも絶対しないでください。容易にハッキングされます。

- もし APIキーが他人に漏れるような事態になれば、**Google AI Studio で APIキーを削除し、作り直してください**。

- このワークショップでは、学習のためキーを Unity（`Assets/Common/APIKey.txt`）のファイルに組み込みます。一般に公開するアプリでは必ずこの形は使わないでください。（キーは端末にも配布ビルドにも入れず、自前のサーバが短い寿命の仮の資格情報を発行し、クライアントはそのトークンだけを Gemini に渡す形が推奨されます）

<br/>

---

## API Keyが動くか確認する

<br/>

Unity に進む前に、キーが使えることを確認してください。モデルは **`gemini-3.1-flash-lite`** を使います。

### 1. Cursor などの AI Agent に任せる

上まで終わっていれば、Agent に疎通確認を頼めます。チャットで次のように依頼してください。

```
Assets/Common/APIKey.txt のキーが有効か、gemini-3.1-flash-lite で疎通確認して。
```

Agent が API を呼び、日本語などの応答が返れば **成功** です。失敗したら理由（ステータスコードの `401` / `429` / `404` など）も聞いてください。

### 2. Mac（ターミナル）

1. **テキストエディット**を開いてください（`Command` + `Space` → `テキストエディット`）。
2. メニュー **フォーマット → 標準テキストにする** を選んでください（コードが装飾されないようにするためです）。
3. 下のコマンドをコピーして、テキストエディットに貼ってください。
4. テキストエディット上で `ここにキーを貼る` を、自分の APIキーに置き換えてください。
5. 内容をすべて選択してコピーしてください（`Command` + `A` → `Command` + `C`）。
6. `Command` + `Space` → `ターミナル` でターミナルを開いてください。
7. ターミナルに貼り付け（`Command` + `V`）して `Enter` を押してください。

```bash
curl -s "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-flash-lite:generateContent" -H "x-goog-api-key: ここにキーを貼る" -H "Content-Type: application/json" -X POST -d '{"contents":[{"parts":[{"text":"Say hi in one word."}]}]}'
```

返答テキストを含む JSON が返れば **成功** です。

コマンドの実行に不安がある場合は、左メニューの **Playground** で話しかけて返事が返るか確認しても構いません。キー疎通を確実に確かめられるのは、上のコマンドです。

### 3. Windows（PowerShell）

1. **メモ帳**を開いてください（`Windows` キー → `メモ帳` と入力）。
2. 下のコードをコピーして、メモ帳に貼ってください。
3. メモ帳上で `ここにキーを貼る` を、自分の APIキーに置き換えてください（前後の `"` は消さない）。
4. メモ帳の内容をすべて選択してコピーしてください（`Ctrl` + `A` → `Ctrl` + `C`）。
5. `Windows` キー → `powershell` と入力し、**Windows PowerShell** を開いてください。
6. PowerShell に貼り付け（右クリック、または `Ctrl` + `V`）して `Enter` を押してください。

```powershell
$apiKey = "ここにキーを貼る"
$uri = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-flash-lite:generateContent"
$body = '{"contents":[{"parts":[{"text":"Say hi in one word."}]}]}'
Invoke-RestMethod -Uri $uri -Method Post -ContentType "application/json; charset=utf-8" -Headers @{ "x-goog-api-key" = $apiKey } -Body $body
```

`candidates` の中に返答テキスト（例: `Hi`）があれば **成功** です。



<br/>

---

## うまくいかないとき

<br/>

失敗したときは、返ってきた **HTTP ステータスコード** を見てください。

| ステータスコード/不具合 | 確認 |
|---|---|
| `401` / `403`<br />キーが無効、または拒否された | キーの空白・改行をチェック。<br />Key Type が `Auth` かどうか確認し、違っていたら新しく作り直す |
| `429`<br />回数が多すぎて止められた | 無料枠上限に達したので、有料枠をつくる必要がある。<br />[gemini-api-pricing.md](gemini-api-pricing.md) |
| `404`<br />URL やモデル名が見つからない | URL / モデル名が古くないか<br />（今回用いるのは`gemini-3.1-flash-lite`） |
| 応答が空 | リクエストJSON の入れ子構造が正しいかどうかチェック。 |

