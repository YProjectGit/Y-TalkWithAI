# STT 先行・A/B 番号へのデモ再構成

## 要点（サマリー）

- **何をするか**: 学習順を「入力モダリティ × 出力の形（Text / JSON）」の A/B に再編し、音声は STT を TTS より先にする。
- **確定案（案C）**: 下記。フォルダ表記は `1A.TextToText`（番号と題名のあいだはピリオドのみ、スペースなし）。
- **適用状況**: フォルダ改名・overview・README・命名規約・実装済み 1A〜3A まで反映済み。`3B`（Live API）以降のシーン実装は別タスク。
- **一言**: テキストで Text→JSON を覚えたら、同じ型をマイク入力で辿る。そのあと REST で声の往復（Live API は後続）。

| 番号 | 題名 | 骨格 | 備考 |
|------|------|------|------|
| 1A | TextToText | Text → LLM → Text | 旧 TextChat（実装済み） |
| 1B | TextToJSON | Text → LLM(JSON) → UI | 旧 StructuredOutput（実装済み） |
| 2A | SpeechToText | Mic → STT → LLM → Text | 実装済み |
| 2B | SpeechToJSON | Mic → STT → LLM(JSON) → UI | 実装済み |
| 3A | SpeechToSpeech | Mic → STT → LLM → TTS → Audio | REST・TTS 初出（実装済み） |
| 3B | SpeechToSpeechLiveAPI | Live API（音声↔音声） | 概要のみ（後続） |
| 4 | VisionToSpeech | Camera → Vision LLM → TTS | 概要のみ |
| 5 | ScreenToSpeech | Screen → Vision LLM → TTS | 概要のみ |
| 6 | TextToImage | Text → Image Gen | 概要のみ |
| 7 | ImageToImage | Image + Text → Image Edit | 概要のみ |

---

## フォルダ命名（確定）

```text
Assets/1A.TextToText/
Assets/1B.TextToJSON/
Assets/2A.SpeechToText/
Assets/2B.SpeechToJSON/
Assets/3A.SpeechToSpeech/
Assets/3B.SpeechToSpeechLiveAPI/
...
```

- 形式: `{番号}.{題名}`（スペースなし）
- README 標題も同型: `# 1A.TextToText`

---

## パイプライン図

```text
[1A] Text ──────────────► LLM ──────────────────────► Text
[1B] Text ──────────────► LLM (JSON) ───────────────► UI / 数値など
[2A] Mic ──► STT ─────► LLM ──────────────────────► Text
[2B] Mic ──► STT ─────► LLM (JSON) ───────────────► UI / 数値など
[3]  Mic ──► STT ─────► LLM ──► TTS ──────────────► Audio
[4]  Camera ──────────► Vision LLM ──► TTS ───────► Audio
[5]  Screen ──────────► Vision LLM ──► TTS ───────► Audio
[6]  Text ──────────────► Image Gen ────────────────► Image
[7]  Image ＋ Text ─────► Image Edit ───────────────► Image
```

---

## 残タスク（別）

- [ ] `2A` 以降のシーン・スクリプト実装
- [ ] 必要なら `1B.TextToJSON` README を WorkshopMaterial の章構成（学べることが問いだけ、など）へ揃える
