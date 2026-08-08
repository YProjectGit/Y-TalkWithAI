# 3B.SpeechToSpeechLiveAPI

> **現状**: フォルダと概要のみ。シーン・スクリプトは未実装です。  
> シリーズ全体の位置づけ → [Docs/demo-series-overview.md](../../Docs/demo-series-overview.md)

## このデモで学べること（予定）

- Live API では、声の入出力がどのように1セッションでつながるか？
- `3A.SpeechToSpeech`（REST の STT → LLM → TTS）と、どこが同じでどこが違うか？
- WebSocket で音声を流すとき、Unity 側では何をバッファする必要があるか？

## 処理の骨格（予定）

```text
マイク（PCM ストリーム）
  → Live API セッション（ネイティブ音声）
  → 返答音声（PCM）＋必要なら文字起こし
  → スピーカー再生
```

`3A` のように STT / Chat / TTS を三段の `generateContent` に分けず、**音声→音声を Live API 一発**で行うデモにする予定です。

## 事前準備（実装後に使う想定）

1. APIキーの取得と `Assets/Common/APIKey.txt` への保管  
   → [Docs/gemini-ai-studio-setup.md](../../Docs/gemini-ai-studio-setup.md)
2. Unity でマイク権限・入力デバイスが使えること
3. 双方向ストリーム（WebSocket）を扱える実装の準備

## 予定するファイル構成

| 種類 | 例 | 役割 |
|------|-----|------|
| シーン | `SpeechToSpeechLiveAPI.unity` | デモの入口 |
| スクリプト | Live API 接続・送受信・再生 | セッション管理 |
| チュートリアル MD | 本 README（実装後に手順を追記） | 学生向け手順 |

実装は後続タスクです。いまは `3A.SpeechToSpeech`（REST）を先に学びます。
