# 2B.SpeechToJSON 実装プラン

## 要点（サマリー）

- **何をするか**: `2A` の声入力（Space 押し話し → Audio generateContent）に、`1B` の構造化 JSON → キューブ／背景色反映を組み合わせる。
- **パイプライン**: Mic → WAV → **1→2 Audio（文字起こし）** → **3→4 Text（スキーマ付き JSON）** → パースして 3D に反映。
- **UI**: 左は 1B 型（3D＋録音案内）、中央／右は 2A 型の発生順 1〜4（Request/Response × Audio/Text）。
- **コピー派生**: 共通基底は作らない。`SpeechToText` と `TextToJSON` から必要な部分をコピーして短く追える1ファイルにまとめる。
- **触らないもの**: 1A/1B/2A の挙動、3 以降、TTS。
- **完了条件**: シーン・`SpeechToJSON.cs`・WorkshopMaterial 準拠 README。クラウドのため Editor 検証は省略し、**UI 構成イメージを画像で出す**。

| | 2A.SpeechToText | 1B.TextToJSON | 2B.SpeechToJSON（本プラン） |
|---|-----------------|---------------|---------------------------|
| 入力 | Space 押し話し | テキスト送信 | **Space 押し話し** |
| 1→2 | generateContent（Audio） | なし | **同じ（文字起こし）** |
| 3→4 / 本処理 | generateContent（Text）自由文 | generateContent（Text）＋schema | **schema 付き Text** |
| 左ペイン | チャット吹き出し | 3D キューブ | **3D キューブ＋認識テキスト** |
| コンテキスト | 常時ON（履歴） | 単発 | **単発（1B と同じ）** |

---

## 処理の骨格

```text
Space 押し話し
  → Microphone → AudioClip → WAV → Base64
  → 1. Request  GenerateContent（Audio）
  → 2. Response GenerateContent（Audio）  … 認識テキスト
  → 3. Request  GenerateContent（Text）   … responseSchema 付き
  → 4. Response GenerateContent（Text）   … 構造化 JSON
  → パース → キューブ色 / 背景色
```

タイトル表記（2A と同じ規則）:

1. `Request - GenerateContent（Audio）`
2. `Response - GenerateContent（Audio）`
3. `Request - GenerateContent（Text）`
4. `Response - GenerateContent（Text）`

---

## UI 構成

```text
┌─────────────────┬──────────────────────────┬──────────────────────────┐
│ Left            │ Center                   │ Right                    │
│ 3D キューブ     │ 1. Request（Audio）      │ 2. Response（Audio）     │
│ ＋背景          │    音声 inlineData       │    文字起こし            │
│                 ├──────────────────────────┼──────────────────────────┤
│ 認識テキスト    │ 3. Request（Text）       │ 4. Response（Text）      │
│ Space 案内      │    schema 付き JSON      │    構造化 JSON           │
│ Status          │                          │                          │
└─────────────────┴──────────────────────────┴──────────────────────────┘
```

- **左**: 1B のプレビュー（`cubeRenderer` / `targetCamera` / `previewArea`）を踏襲。テキスト入力・送信ボタンは置かない。代わりに 2A の Space 案内と Status。直近の認識文を1行（または短い欄）で出す。
- **中央／右**: 2A と同じ上下2段×Request/Response。番号は発生順 1〜4。
- **Schema**: 1B のような独立ペインは置かない。`3. Request` の JSON 内 `generationConfig.responseSchema` で見えるようにする（必要なら 3 の Description に「スキーマ付き」と書く）。
- **System Instruction / コンテキスト Option**: 置かない（1B に合わせ単発）。
- **入力**: 旧 Input Manager の Space 押し話し（2A と同じ。Active Input Handling は Both 済み）。

---

## 設計の骨格（コード）

### ファイル

| パス | 由来 |
|------|------|
| `Assets/2B.SpeechToJSON/SpeechToJSON.unity` | 1B シーンを土台に、2A の Request/Response 4欄を足す |
| `Assets/2B.SpeechToJSON/Script/SpeechToJSON.cs` | 2A の録音〜Audio 段 ＋ 1B の schema/パース/反映 |
| `Assets/2B.SpeechToJSON/Resource/` | 1B のキューブ用マテリアル等をコピー |
| `Assets/2B.SpeechToJSON/README.md` | WorkshopMaterial 準拠で本文化 |

### `SpeechToJSON.cs` の流れ

1. `Start` — APIキー、マイク確認、Schema 定数の準備、3D 参照、案内文言
2. `Update` — Space 押し話し（System Instruction 欄は無いのでフォーカス衝突は少ない）
3. 録音終了 → WAV → Base64
4. **1→2** Audio generateContent（文字起こしのみ）→ 認識テキストを左に表示
5. **3→4** Text generateContent（`ResponseSchemaJson` は 1B と同型: `cubeColor` / `backgroundColor`）
6. `TryParseAndApply` — キューブと背景色へ反映
7. 各欄へ Request/Response を番号どおり表示（長い Base64 は表示だけ短縮）

### 1B / 2A から流用するもの

| 流用元 | 内容 |
|--------|------|
| 2A | Space、`Microphone`、WAV 化、Audio リクエスト組み立て、4欄表示、旧 Input |
| 1B | `ResponseSchemaJson`、`BuildRequestJson`（schema 付き）、`TryParseAndApply`、カメラ／プレビュー矩形の扱い |

会話履歴（turns）は持たない。毎回「いまの発話 → JSON」の単発。

---

## README（実装時）

章構成は WorkshopMaterial 準拠。

- 学べること（問い）: 声で色が変わる前に何が起きているか／1→4 のどれが文字起こしでどれが JSON か、など
- 動かし方: Space → キューブ色が変わる → 1〜4 を順に見る
- 概念節: マイクと音声データ（2A と同趣旨・短く）、構造化出力／スキーマ／パース（1B と同趣旨）
- 主要クラス: `SpeechToJSON` を発生順 1〜4 で読む

---

## 実装タスク順

1. 1B からシーン／Resource を 2B へコピーし、入力 UI を Space 案内に差し替え
2. 2A の Request/Response 4欄レイアウトを中央・右へ移植（標題は 1〜4 の GenerateContent 表記）
3. `SpeechToJSON.cs` を実装（録音＋Audio 段＋schema Text 段＋反映）
4. README 本文化、overview の実装済み表記を更新
5. **UI 構成イメージを画像出力**（クラウドルール）
6. コミット / push（Editor 検証は省略と明記）

---

## 判断の固定

- 出力は 1B と同じ色2項目（scale / 回転は発展課題に回すなら README で触れる程度）
- 履歴なし・System Instruction なし
- 標題・番号規則は 2A と同一（発生順 1〜4）
- 共通クラス化はしない

この方針で実装に進めてよいか確認する。
