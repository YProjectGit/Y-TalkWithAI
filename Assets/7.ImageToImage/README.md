# 7.ImageToImage

> **現状**: フォルダと概要のみ。シーン・スクリプトは未実装です。  
> シリーズ全体の位置づけ → [Docs/demo-series-overview.md](../../Docs/demo-series-overview.md)  
> 実装プラン → [Docs/7-image-to-image.plan.md](../../Docs/7-image-to-image.plan.md)

## このデモで学べること（予定）

- **画像変換**  
  カメラに映っている1枚と短い指示から、別の画像を得る
- **参照画像**  
  テキストだけの生成ではなく、今のカメラ画像を手がかりにする
- **ライブ映像と After**  
  変換の前後を見比べて、指示と結果の関係を確認する

## 処理の骨格

```text
カメラ1フレーム ＋ 指示テキスト
  → REST generateContent（IMAGE）
  → After に表示
```

入力は WebCam のシャッター1枚です。連続変換や Live API は使いません。

## 事前準備（実装後に使う想定）

1. Google AI Studio から Gemini の API にアクセスするための APIキーを取得し、`Assets/Common/APIKey.txt` に保管してください。  
   → [Docs/gemini-ai-studio-setup.md](../../Docs/gemini-ai-studio-setup.md)
2. PC にカメラがつながり、Unity から使える状態にしてください（OS のカメラ権限を含む）。
3. 画像モデルがキーで使えること（6 と同じ系統。無料枠は Lite テキストより少ないことが多い）

## 予定するファイル構成

実装時に、このフォルダへ次をそろえる想定です。

| 種類 | 例 | 役割 |
|------|-----|------|
| シーン | `ImageToImage.unity` | デモの入口 |
| スクリプト | `Script/ImageToImage.cs` | WebCam 取得〜変換リクエスト〜表示 |
| チュートリアル MD | 本 README（実装後に手順を追記） | 学生向け手順 |

## 主要スクリプトの読み方（実装後）

入口スクリプトを上から読む想定の流れです。

1. APIキー読込・WebCam 起動
2. 変換ボタンで今のフレームを JPEG にする
3. 指示テキストと画像をリクエストに載せる
4. 変換後画像の受信
5. After に表示（教材向けに Request の `inlineData` も見える）
