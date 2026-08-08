# 2B.SpeechToJSON

> **現状**: フォルダと概要のみ。シーン・スクリプトは未実装です。  
> シリーズ全体の位置づけ → [Docs/demo-series-overview.md](../../Docs/demo-series-overview.md)

## このデモで学べること（予定）

- マイク入力と STT で発話をテキストにする流れ（`2A.SpeechToText` と同型）
- そのテキストを LLM に渡し、JSON（構造化出力）で返す流れ（`1B.TextToJSON` と同型）
- 「声で指示して、Unity の見た目やパラメータを動かす」組み合わせ

## 処理の骨格

```text
マイク入力（音声）
  → STT（ユーザー発話テキスト）
  → LLM（JSON）
  → UI / パラメータへ反映
```

`2A` の入力に、`1B` の JSON 反映を足した形です。新しいモダリティ（TTS など）は増やしません。

## 事前準備（実装後に使う想定）

1. APIキーの取得と `Assets/Common/APIKey.txt` への保管  
   → [Docs/gemini-ai-studio-setup.md](../../Docs/gemini-ai-studio-setup.md)
2. Unity でマイク権限・入力デバイスが使えること
3. `1B.TextToJSON` と同様、反映先（色やオブジェクトなど）がシーンにあること

## 予定するファイル構成

実装時に、このフォルダへ次をそろえる想定です。

| 種類 | 例 | 役割 |
|------|-----|------|
| シーン | `SpeechToJSON.unity` | デモの入口 |
| スクリプト | `SpeechToJSON.cs` など | 録音〜STT〜LLM(JSON)〜反映 |
| チュートリアル MD | 本 README（実装後に手順を追記） | 学生向け手順 |

## 主要スクリプトの読み方（実装後）

入口スクリプトを上から読む想定の流れです。

1. APIキー読込・マイク初期化・反映先の参照
2. 録音開始 / 停止（または押し話し）
3. STT で発話テキスト化・表示
4. LLM（スキーマ付き）→ 構造化 JSON
5. パースして UI / パラメータへ反映
6.（教材向け）Schema / Response / Status の可視化
