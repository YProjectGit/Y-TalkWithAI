# 5.ScreenToSpeech

> **現状**: フォルダと概要のみ。シーン・スクリプトは未実装です。  
> シリーズ全体の位置づけ → [Docs/demo-series-overview.md](../../Docs/demo-series-overview.md)  
> 姉妹プラン（4） → [Docs/4-vision-to-speech.plan.md](../../Docs/4-vision-to-speech.plan.md)

## このデモで学べること（予定）

- **画面キャプチャ**  
  カメラではなく、アプリ／ゲームの画面を入力画像にする
- **Live API の映像入力**  
  その画面フレームをセッションに渡し、内容への返答を声でもらう
- **入力源の差し替え**  
  `4.VisionToSpeech` と同じ Live 骨格で、見る対象だけを変える

## 処理の骨格

```text
画面キャプチャ（画像フレーム）
  → Live API（JPEG フレーム → ネイティブ音声）
  → スピーカー再生
```

`4.VisionToSpeech` が **外の世界（WebCam）** を見るのに対し、ここでは **いま画面に出ているもの** を見るのがポイントです。通信は 4 と同じ Live API 想定です（REST の Vision→TTS は使いません）。

## 事前準備（実装後に使う想定）

1. APIキーの取得と `Assets/Common/APIKey.txt` への保管  
   → [Docs/gemini-ai-studio-setup.md](../../Docs/gemini-ai-studio-setup.md)
2. キャプチャ対象になる何かがシーン上にあること（UI・3D・色面など）

## 予定するファイル構成

実装時に、このフォルダへ次をそろえる想定です。

| 種類 | 例 | 役割 |
|------|-----|------|
| シーン | `ScreenToSpeech.unity` | デモの入口 |
| スクリプト | `Script/ScreenToSpeech.cs` など | キャプチャ〜Live 送受信〜再生 |
| チュートリアル MD | 本 README（実装後に手順を追記） | 学生向け手順 |

## 主要スクリプトの読み方（実装後）

入口スクリプトを上から読む想定の流れです。

1. APIキー読込・キャプチャ対象の準備・Live 接続
2. 画面／RenderTexture をフレーム化（縮小→JPEG）
3. Space シャッターまたは連続送信で Live へ送る
4. 受信 PCM の再生と transcription の表示
5.（教材向け）送信／受信ログと Status の可視化
