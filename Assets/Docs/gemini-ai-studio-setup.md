# Google AI Studio のセットアップ

<br/>

Gemini API にアクセスするための準備です。
GoogleのAI関連の開発者向けサービス **Google AI Studio** でアカウントをつくります。
そこでGemini APIにアクセスするための**APIキー**を1本取得して、Unity から使えるようにします。

<br/>

---

## 必要なもの

<br/>

- Google アカウント（既存のもので結構です）

最初は無料枠で利用できます。上限に達したときの進め方は、下の [無料枠を使い切ったとき](#無料枠を使い切ったとき) を見てください。

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
   <br/>
   
   ![gemini-ai-studio-setup-get-api-key](Image/gemini-ai-studio-setup-get-api-key.png)
   
   <br/>
   
2. **APIキーを作成** を選択してください。
   
   <br/>![gemini-ai-studio-setup-create-api-key](Image/gemini-ai-studio-setup-create-api-key.png)
   
   <br/>
   
3. プロジェクトを新規作成したのち、そのプロジェクトのAPIキーを作成してください。名前は適当で結構です。
   <br/>

   ![gemini-ai-studio-setup-new-project](Image/gemini-ai-studio-setup-new-project.png)

4. 発行されたキーを **Copy** してください。
   <br/>
   
   ![gemini-ai-studio-setup-copy-key](Image/gemini-ai-studio-setup-copy-key.png)

<br/>

---

## APIキーを保管する

<br/>

![gemini-ai-studio-setup-unity-apikey](Image/gemini-ai-studio-setup-unity-apikey.png)

1. `Assets/Common/APIKey.txt` の `Paste Your APIKey Here…` を、自分のキー1行に置き換えてください（前後の空白に注意）。
2. 本物のキーは Git にコミットしないでください。

- APIキーは、**自分の課金で API に自由にアクセスできるチケット**です。絶対に他人に見せないでください（SNS・チャット・スクリーンショット・公開リポジトリ禁止）。アプリを公開することも絶対しないでください。容易にハッキングされます。

- もし APIキーが他人に漏れるような事態になれば、**Google AI Studio で APIキーを削除し、作り直してください**。

- このワークショップでは、学習のためキーを Unity（`Assets/Common/APIKey.txt`）のファイルに組み込みます。一般に公開するアプリでは必ずこの形は使わないでください。（キーは端末にも配布ビルドにも入れず、自前のサーバが短い寿命の仮の資格情報を発行し、クライアントはそのトークンだけを Gemini に渡す形が推奨されます）

<br/>

---

## APIキーが動くか確認する

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
2. 下のコマンドをコピーして、メモ帳に貼ってください。
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

## 無料枠を使い切ったとき

<br/>

- APIキーを取った直後は **無料枠** です。料金はかかりませんが、モデルごとに「1日に何回まで」という回数の上限があります（この教材で主に使う `gemini-3.1-flash-lite` は1日 500回程度）。
- 上限に達するとステータスコード `429` が返り、以降、リクエストを送信できなくなります。日付が変われば回数は元に戻るので、翌日まで待つのがいちばん簡単な対処です。
- もっと制限なく使いたい場合は、**有料枠**に切り替えてください。使った分だけ支払う従量課金で、この教材の使い方なら**1日あたり数十円〜200円程度**です。APIキーを作り直す必要はありません。

<br/>

### 有料枠のクレジットを購入する

1. [https://aistudio.google.com/app/apikey](https://aistudio.google.com/app/apikey) を開き、いま使っているプロジェクトの **お支払い情報を設定** を押してください。
2. 支払い方法（クレジットカード）などを入力してください。
3. **クレジットを購入**を選択し、適切な金額のクレジットを購入してください。
   選択肢は5000円からと高いですが、自由入力欄で**1500円程度**を指定すればそれで十分です。
   **自動チャージ（auto-reload）オプションはオフ**のままにしてください。
4. Unity 側の作業はありません。`Assets/Common/APIKey.txt` のキーのままで大丈夫です。

<br/>

### 値段の目安

1ドル150円として、**学生1人が半日ワークショップで触ったとき**のだいたいの金額です。

| 使い方 | だいたいの金額 |
|---|---|
| テキストの送受信（1A / 1B） | 1回 0.1円未満。半日でも数円〜20円 |
| 文字起こし＋音声の返答（2A / 3A） | 20〜80円 |
| Live で10〜15分の会話（3B 以降） | 追加で 50〜150円 |
| 半日まるごと（Live も触る） | 100〜200円 |

$10（約1500円）をチャージしておけば、この授業で使い切ることはなかなかないと思います。
唯一気をつけたいのが、3Bで紹介する**Live をつないだままの放置**です。無言でも接続しているあいだは課金されるので、使い終わったら Play を止めてください。

正確な単価は [公式の料金表](https://ai.google.dev/gemini-api/docs/pricing) を見てください（上の目安は2026年8月時点のものです）。

<br/>

---

## うまくいかないとき

<br/>

失敗したときは、返ってきた **HTTP ステータスコード** を見てください。

| ステータスコード/不具合 | 確認 |
|---|---|
| `401` / `403`<br />キーが無効、または拒否された | キーの空白・改行をチェック。<br />直らなければ APIキーを作り直す |
| `429`<br />回数が多すぎて止められた | 無料枠の1日の回数を使い切っている。<br />翌日まで待つか、[無料枠を使い切ったとき](#無料枠を使い切ったとき) を見て有料枠に切り替える |
| `404`<br />URL やモデル名が見つからない | URL / モデル名が古くないか<br />（今回用いるのは`gemini-3.1-flash-lite`） |
| 応答が空 | リクエストJSON の入れ子構造が正しいかどうかチェック。 |

