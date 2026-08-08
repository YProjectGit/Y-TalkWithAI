# STT 先行・A/B 番号へのデモ再構成

## 要点（サマリー）

- **何をするか**: 学習順を「入力モダリティ × 出力の形（Text / JSON）」の A/B に再編し、音声は STT を TTS より先にする。
- **推奨案（案C）**: 下記の番号体系。旧案A（平坦な 3=STT / 4=S2S）よりこちらを推奨。
- **触らないもの（この段階）**: 各デモの中身の新規実装。まずは概要・フォルダ名・overview の合意。
- **完了条件（並び替え適用時）**: overview とフォルダ／README の番号・題名・相互参照が案Cで一致すること。
- **一言**: テキストで Text→JSON を覚えたら、同じ型をマイク入力で melる。そのあと声の往復。

| 番号 | 題名 | 骨格 | 現状との対応 |
|------|------|------|----------------|
| 1A | TextToText | Text → LLM → Text | 旧 `1. TextChat` |
| 1B | TextToJSON | Text → LLM(JSON) → UI | 旧 `2. StructuredOutput` |
| 2A | SpeechToText | Mic → STT → LLM → Text | 新設（旧 3 TTS の位置を置換） |
| 2B | SpeechToJSON | Mic → STT → LLM(JSON) → UI | 新設 |
| 3 | SpeechToSpeech | Mic → STT → LLM → TTS → Audio | 旧 `4. SpeechToSpeech`（TTS 初出） |
| 4… | Vision / Screen / 画像 | 後述 | 旧 5〜8 を再配置 |

---

## なぜよいか

入力で段階を切り、各段階で **A=自由テキスト / B=構造化** を揃えるので、学生から見て対応関係が読みやすい。

```text
        出力: Text              出力: JSON
入力 Text   1A TextToText         1B TextToJSON
入力 Speech  2A SpeechToText       2B SpeechToJSON
入力+出力声  3  SpeechToSpeech（ここで TTS）
```

- STT が TTS より先（当初の要望を満たす）。
- 孤立 TTS（Text→TTS のみ）は置かない。TTS は 3 で「声の出口」として初出。
- 2B は「声で Unity を動かす」体験になり、ワークショップの山場にしやすい。

---

## パイプライン図（1A〜3）

```text
[1A] Text ──────────────► LLM ──────────────────────► Text
[1B] Text ──────────────► LLM (JSON) ───────────────► UI / 数値など
[2A] Mic ──► STT ─────► LLM ──────────────────────► Text
[2B] Mic ──► STT ─────► LLM (JSON) ───────────────► UI / 数値など
[3]  Mic ──► STT ─────► LLM ──► TTS ──────────────► Audio
```

### 各段で増えるもの

| デモ | 前から増える主な要素 |
|------|----------------------|
| 1A | API 送受信、可視化、コンテキスト |
| 1B | JSON スキーマ、パース、UI / パラメータ反映 |
| 2A | マイク録音、STT（出力は 1A と同型のテキスト） |
| 2B | 2A の入力 + 1B の JSON 反映（新しい API は増やさず組み合わせ） |
| 3 | TTS と再生（入力は 2A と同型） |

---

## 4 以降の伸ばし方（案）

A/B をむやみに増やさない。Vision 以降は「入力源」で番号を進める。

| 番号 | 題名（案） | 骨格 |
|------|------------|------|
| 4 | VisionToSpeech | Camera → Vision LLM → TTS → Audio |
| 5 | ScreenToSpeech | Screen → Vision LLM → TTS → Audio |
| 6 | TextToImage | Text → Image Gen → Image |
| 7 | ImageToImage | Image + Text → Image Edit → Image |

必要なら後から `4B VisionToJSON` などを足せるが、**初版は 4〜7 を平坦番号のまま**にする（学習ポイントの絞り込み）。

```text
[4] Camera ──► Vision LLM ──► TTS ──► Audio
[5] Screen ──► Vision LLM ──► TTS ──► Audio
[6] Text ───► Image Gen ────────────► Image
[7] Image＋Text ► Image Edit ───────► Image
```

---

## フォルダ命名（ルール更新が必要）

現行 WorkshopMaterial は `Assets/{番号}. {題名}/`（例: `1. TextChat`）。

案C では次に揃える。

| フォルダ例 | README 標題例 |
|------------|----------------|
| `Assets/1A. TextToText/` | `# 1A.TextToText` |
| `Assets/1B. TextToJSON/` | `# 1B.TextToJSON` |
| `Assets/2A. SpeechToText/` | `# 2A.SpeechToText` |
| `Assets/2B. SpeechToJSON/` | `# 2B.SpeechToJSON` |
| `Assets/3. SpeechToSpeech/` | `# 3.SpeechToSpeech` |

- シーン名・メインスクリプト名は題名に合わせる（`TextToText.cs` 等）。
- 既存実装の改名: `TextChat` → `TextToText`、`StructuredOutput` → `TextToJSON`（スクリプト／シーン／Prefab 参照の更新が発生）。GUID は維持してフォルダ改名する。

---

## リスク・トレードオフ

| 点 | 内容 |
|----|------|
| 改名コスト | 実装済みの 1 / 2 をフォルダ・クラス・シーンごと改名する必要がある |
| 2B の密度 | STT + JSON の組み合わせデモ。1B・2A をやった前提なら一段足すだけ、と説明できる |
| 番号の慣れ | `1A`/`1B` は教材では分かりやすいが、ルールと overview の表記を先に直す必要あり |
| 旧名 TextChat | パイプライン名（TextToText）に揃える利点あり。愛称として README 本文で「チャット」と呼ぶのは可 |

---

## 廃止・採用しない案

- **旧案A**（平坦 3=SpeechToText / 4=SpeechToSpeech、1/2 名は据え置き）: A/B 対称が弱いので案C を優先。
- **旧案B**（孤立 TextToSpeech を挟んで 9 本）: 案C では採らない。
- 番号付きデモとしての孤立 TextToSpeech は置かない。

---

## 作業タスク（方針確定後）

1. WorkshopMaterial のフォルダ命名規約を `1A` / `1B` 形式に更新
2. `Docs/demo-series-overview.md` を案Cの表・図に差し替え
3. フォルダ改名（GUID 維持）
   - `1. TextChat` → `1A. TextToText`（クラス／シーン名も追随するかは実装タスクで分割可）
   - `2. StructuredOutput` → `1B. TextToJSON`
   - `3. TextToSpeech` → 削除または `2A. SpeechToText` に転用
   - `4. SpeechToSpeech` → `3. SpeechToSpeech`
   - `2B. SpeechToJSON` を新設（概要 README）
   - Vision 以降を 4〜7 に振り直し
4. 各概要 README の相互参照を更新
5. （別タスク）未実装デモのシーン／スクリプト、および 1A/1B の識別子リネーム

### 識別子リネームの分割（推奨）

- **パス1（ドキュメントのみ）**: フォルダ名と README / overview だけ案Cに。中のクラス名は旧名のまま一時許容、README で対応を書く。
- **パス2（実装追随）**: `TextChat` → `TextToText` 等のコード改名とシーン配線。

初回適用はパス1でもよい。

---

## 完了条件（並び替え適用時）

- [ ] overview が 1A〜3 と 4〜7 の表・図で一致
- [ ] フォルダ名が `1A` / `1B` / `2A` / `2B` / `3`… になっている
- [ ] 旧 `TextToSpeech` が先頭音声デモとして残っていない
- [ ] WorkshopMaterial の命名例が新形式になっている

---

## 判断待ち

1. **案C（本番号体系）で確定してよいか**
2. 4 以降は上表（Vision→Screen→画像、A/B なし）でよいか
3. 既存 `TextChat` / `StructuredOutput` のクラス名改名はすぐやるか、パス1で後回しするか
4. 2B の題名は `SpeechToJSON` でよいか（`SpeechToStructured` 等の別名があるか）
