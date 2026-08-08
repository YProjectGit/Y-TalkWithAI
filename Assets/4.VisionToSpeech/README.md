# 4.VisionToSpeech

> **現状**: フォルダと概要のみ。シーン・スクリプトは未実装です。  
> シリーズ全体の位置づけ → [Docs/demo-series-overview.md](../../Docs/demo-series-overview.md)  
> 実装プラン → [Docs/4-vision-to-speech.plan.md](../../Docs/4-vision-to-speech.plan.md)

## このデモで学べること（予定）

- **Live API の映像入力**  
  カメラ画像をセッションに流し、見た内容への返答を声でもらう
- **シャッターとストリーミング**  
  Space の1枚送信と、トグルによる連続送信（約1 FPS）の違い
- **送信フレーム**  
  動画ファイルではなく JPEG フレームを送っていること

## 処理の骨格

```text
カメラ入力（WebCam プレビュー）
  → Live API（JPEG フレーム → ネイティブ音声）
  → スピーカー再生
```

マイクは使いません。**見たもの**を Live セッションに渡し、**声で説明する**デモです。REST の Vision→TTS 二段は使いません（`3B` と同系統の Live API）。

UX の予定:

- **Space** … シャッター（1フレーム送信）。既定モード
- **Stream トグル** … ON で約1 FPS 連続送信、OFF で解除。ON 中は Space 無効

## 事前準備（実装後に使う想定）

1. APIキーの取得と `Assets/Common/APIKey.txt` への保管  
   → [Docs/gemini-ai-studio-setup.md](../../Docs/gemini-ai-studio-setup.md)
2. PC にカメラがつながっていること（権限の許可を含む）
3. 明るい被写体があると確認しやすい

## 予定するファイル構成

実装時に、このフォルダへ次をそろえる想定です。

| 種類 | 例 | 役割 |
|------|-----|------|
| シーン | `VisionToSpeech.unity` | デモの入口 |
| スクリプト | `Script/VisionToSpeech.cs` など | WebCam〜Live 送受信〜再生 |
| Prefab | `Prefab/MessageBubble.prefab` | 吹き出し（必要時） |
| チュートリアル MD | 本 README（実装後に手順を追記） | 学生向け手順 |

## 主要スクリプトの読み方（実装後）

入口スクリプトを上から読む想定の流れです。

1. APIキー読込・WebCam 起動（プレビュー）・Live 接続
2. Space シャッターで1フレーム送信（縮小→JPEG）
3. または Stream トグルで約1 FPS 連続送信（ON 中は Space 無効）
4. 受信 PCM の再生と transcription の吹き出し
5.（教材向け）送信／受信ログと Status の可視化
