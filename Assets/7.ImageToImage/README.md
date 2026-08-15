# 7.ImageToImage

シリーズ全体の位置づけ → [Docs/demo-series-overview.md](../../Docs/demo-series-overview.md)

---

## このデモで学べること

- **画像変換**  
  カメラに映っている1枚と短い指示から、別の画像を得る
- **参照画像**  
  テキストだけの生成ではなく、今のカメラ画像を手がかりにする
- **同じ generateContent に画像を載せる**  
  6 と同じ REST で、指示テキストとカメラ JPEG を並べて送る

---

## 事前準備

1. Google AI Studio から Gemini の API にアクセスするための APIキーを取得し、`Assets/Common/APIKey.txt` に保管してください。  
   手順 → [Docs/gemini-ai-studio-setup.md](../../Docs/gemini-ai-studio-setup.md)  
   無料枠で 429 が出たら、有料への移り方と値段の目安 → [Docs/gemini-api-pricing.md](../../Docs/gemini-api-pricing.md)
2. PC にカメラがつながり、Unity から使える状態にしてください（OS のカメラ権限を含む）。

このデモは 6 と同じ画像生成モデル（既定は `gemini-3.1-flash-image`）を使います。無料枠も課金も、テキスト用の Lite とは別に数えます。

---

## 動かし方

Project ウィンドウで `Assets/7.ImageToImage/ImageToImage.unity` を開き、Play を押してください。

### 1. カメラの映像を見る

1. 左の Camera にライブ映像が出ることを確認してください。
2. 右の After は、まだ変換していないので空です。

### 2. 今の1枚を変換する

1. 指示欄の文をそのまま使うか書き換えて、**変換** を押してください（Enter でも送信できます。Shift+Enter で改行です）。
2. 数秒待って、右の After に変換後の絵が出ることを確認してください。

### 3. もう一度変換する

1. カメラを動かすか指示を変えて、もう一度 **変換** を押してください。
2. After が上書きされることを見てください。Camera のライブ映像は、待ちのあいだも動き続けます。

---

## 参照画像とは？

生成の手がかりとして送る元画像です。6 は言葉だけで絵を作ります。ここでは **今カメラに映っている1フレーム** を JPEG にして、指示文と一緒に送ります。

たとえ話で言うと、「この写真を、こう変えて」と絵葉書を預けるイメージです。返ってくるのも絵です。

試し方: 左の Camera と右の After を見比べる。

---

## シャッター1枚とは？

変換ボタンを押した瞬間のフレームを、1回だけ送ることです。プレビューは動き続けますが、API に載るのはその1枚です。

連続で送り続けることはしません。画像の生成は1枚あたり数秒かかるので、毎フレーム変換しても待ちと回数が増えるだけです。4 の Stream（およそ 1 FPS）とも違い、出口が声ではなく絵である以上、押した瞬間の1枚で足ります。

試し方: 変換のあと Status が「応答待ち」になるあいだ、Camera は動き続けること、After は1枚だけ更新されることを見る。

---

## inlineData（参照画像）とは？

JSON の中に、画像そのものを Base64 という文字の列で埋め込んだものです。6 では **返ってきた絵** が `inlineData` でした。ここでは **送る側** にも同じ入れ物でカメラ JPEG を載せます。

Unity では WebCam の画素を `EncodeToJPG` して Base64 にします。返ってきた画像も、6 と同じく `Texture2D.LoadImage` で After に出します。

試し方: 変換後、After に絵が出ることと、Camera の今の映像がそのままではないことを見比べる。

---

## 主要クラス

### ImageToImage（[`ImageToImage.cs`](Script/ImageToImage.cs)）

デモの本体です。上から、変換ボタンを押したあとの流れを追うとわかりやすいです。

通信は **UnityWebRequest**（HTTP の送受信）と **コルーチン**（`IEnumerator` + `yield`）による **非同期処理** です。コルーチンは `Update` などのメインスレッドの処理とは独立した時間軸で進むので、応答待ちのあいだもプレビューが動き続けます。

1. **起動時の準備をする**  
   `Start` — APIキー読込、シーンの UI、WebCam 起動、変換ボタン／Enter の購読
2. **変換を始める**  
   `OnConvertClicked` → `TryCaptureJpeg` → `StartCoroutine(SendImageCoroutine)` — 送信の入口
3. **API と通信する**  
   `SendImageCoroutine` — 通信本体  
   - `BuildRequestJson` → `UnityWebRequest` で POST  
   - `yield return request.SendWebRequest()` で応答待ち  
   - 画像抽出 → After に `Texture2D` 表示
4. **リクエスト JSON を組み立てる**  
   `BuildRequestJson` — 指示テキストとカメラ JPEG を `parts` に並べ、`responseModalities` / `imageConfig` を載せる
5. **画像バイトを取り出す**  
   `TryExtractInlineImage` — レスポンス JSON から `inlineData` とテキスト part を取り出す
