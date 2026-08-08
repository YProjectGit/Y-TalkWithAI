# 5.ScreenToSpeech

> **現状**: フォルダと概要のみ。シーン・スクリプトは未実装です。  
> シリーズ全体の位置づけ → [Docs/demo-series-overview.md](../../Docs/demo-series-overview.md)

## このデモで学べること（予定）

- **画面キャプチャ**  
  カメラではなく、アプリ／ゲームの画面を入力画像にする
- **Vision 入力**  
  その画面を LLM に渡して内容を解釈してもらう
- **画面→音声**  
  解釈結果を TTS で声にして返す

## 処理の骨格

```text
画面キャプチャ（画像）
  → Vision LLM（解釈・返答テキスト）
  → TTS（音声データ）
  → スピーカー再生
```

`4.VisionToSpeech` が **外の世界（WebCam）** を見るのに対し、ここでは **いま画面に出ているもの** を見るのがポイントです。TTS〜再生は 3 / 4 と同型です。

## 事前準備（実装後に使う想定）

1. APIキーの取得と `Assets/Common/APIKey.txt` への保管  
   → [Docs/gemini-ai-studio-setup.md](../../Docs/gemini-ai-studio-setup.md)
2. キャプチャ対象になる何かがシーン上にあること（UI・3D・色面など）

## 予定するファイル構成

実装時に、このフォルダへ次をそろえる想定です。

| 種類 | 例 | 役割 |
|------|-----|------|
| シーン | `ScreenToSpeech.unity` | デモの入口 |
| スクリプト | `ScreenToSpeech.cs` など | キャプチャ〜Vision LLM〜TTS〜再生 |
| チュートリアル MD | 本 README（実装後に手順を追記） | 学生向け手順 |

## 主要スクリプトの読み方（実装後）

入口スクリプトを上から読む想定の流れです。

1. APIキー読込・キャプチャ対象の準備
2. 画面／RenderTexture を静止画化
3. 画像＋短い指示文を LLM に送信
4. 返答テキストの抽出・表示
5. TTS → 再生
6.（教材向け）Status / 中間テキストの可視化
