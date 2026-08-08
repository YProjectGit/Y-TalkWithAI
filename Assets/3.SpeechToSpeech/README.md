# 3.SpeechToSpeech

> **現状**: フォルダと概要のみ。シーン・スクリプトは未実装です。  
> シリーズ全体の位置づけ → [Docs/demo-series-overview.md](../../Docs/demo-series-overview.md)

## このデモで学べること（予定）

- マイク → STT → LLM までの流れ（`2A.SpeechToText` と同型）
- 返答テキストを TTS（Text-to-Speech）で音声にして再生する流れ
- 「声で話して声で返る」一気通貫のパイプライン

## 処理の骨格

```text
マイク入力（音声）
  → STT（ユーザー発話テキスト）
  → LLM（返答テキスト）
  → TTS（音声データ）
  → スピーカー再生
```

`2A.SpeechToText` の後段に **TTS と再生** が付いた形です。シリーズで TTS が初めて出るデモです。

## 事前準備（実装後に使う想定）

1. APIキーの取得と `Assets/Common/APIKey.txt` への保管  
   → [Docs/gemini-ai-studio-setup.md](../../Docs/gemini-ai-studio-setup.md)
2. Unity でマイク権限・入力デバイスが使えること
3. 静かな環境、またはヘッドセットマイクがあると確認しやすい

## 予定するファイル構成

実装時に、このフォルダへ次をそろえる想定です。

| 種類 | 例 | 役割 |
|------|-----|------|
| シーン | `SpeechToSpeech.unity` | デモの入口 |
| スクリプト | `SpeechToSpeech.cs` など | 録音〜STT〜LLM〜TTS〜再生 |
| チュートリアル MD | 本 README（実装後に手順を追記） | 学生向け手順 |

## 主要スクリプトの読み方（実装後）

入口スクリプトを上から読む想定の流れです。

1. APIキー読込・マイク初期化
2. 録音開始 / 停止（または押し話し）
3. STT で発話テキスト化・表示
4. LLM → 返答テキスト
5. TTS → `AudioClip` 化して再生
6.（教材向け）各段の Status / 中間テキストの可視化
