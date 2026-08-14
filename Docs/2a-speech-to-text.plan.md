# 2A.SpeechToText 実装プラン

## 要点（サマリー）

- **何をするか**: `1A.TextToText` を土台に、入力だけ「Space 押し話し録音 → 音声データ → STT →（既存どおり）LLM → テキスト表示」へ拡張する。
- **作り方**: 共通基底クラスは作らない。1A のスクリプト／UI／Prefab を **コピーして派生**（教材方針どおり短く追える形）。
- **入力**: **旧 Input Manager**（`UnityEngine.Input`）の Space（押している間だけ `Microphone` 録音）。新 Input System（`UnityEngine.InputSystem`）は使わない。テキスト送信欄は置かない。
- **STT**: Gemini `generateContent` に音声（WAV / base64 `inlineData`）を送り文字起こし → そのテキストで 1A と同型のチャット送信。
- **README**: 「マイク → `AudioClip` → WAV バイト列 → Base64」を概念節と主要クラスの両方で追えるようにする。
- **触らないもの**: 1A / 1B の実装、2B 以降、共通 API キー手順の本体。
- **完了条件**: `SpeechToText` シーン＋スクリプト＋WorkshopMaterial 準拠 README。クラウドのため Editor 検証は省略（シーンは 1A 複製＋参照差し替えで用意）。

| | 1A.TextToText | 2A.SpeechToText（この実装） |
|---|---------------|---------------------------|
| ユーザー入力 | テキスト欄 + 送信 / Enter | **Space 押し話し** |
| 送信直前 | 文字列そのまま | **Mic → AudioClip → WAV → STT で文字列化** |
| その後 | LLM → 吹き出し | **同じ**（履歴・コンテキスト・三ペイン） |
| TTS | なし | なし（3 で初出） |

---

## 学習上の位置づけ

```text
[2A] Mic ──► STT ─────► LLM ──────────────────────► Text
```

- 増える要素は **録音と STT** だけ。出力は 1A と同型のテキスト。
- 学生が追う山場は「Unity 側で音声バイト列をどう作るか」と「それが Request JSON のどこに載るか」。

---

## 設計の骨格

### ファイル（予定）

| パス | 由来 |
|------|------|
| `Assets/2A.SpeechToText/SpeechToText.unity` | `1A` シーンを複製し、入力 UI とコンポーネントを差し替え |
| `Assets/2A.SpeechToText/Script/SpeechToText.cs` | `TextToText.cs` をコピーして拡張（新規クラス名） |
| `Assets/2A.SpeechToText/Script/ChatBubble.cs` | 1A と同型をコピー（デモ間参照を避ける） |
| `Assets/2A.SpeechToText/Prefab/MessageBubble.prefab` | 1A からコピー |
| `Assets/2A.SpeechToText/README.md` | WorkshopMaterial 準拠で本文化 |

共通基底や `Assets/Common` への録音ユーティリティ切り出しは **しない**（単一デモ内に閉じる）。

### 処理の流れ（`SpeechToText.cs`）

1. **Start** — APIキー / SystemInstruction / UI 初期化、マイクデバイス確認（`Microphone.devices`）
2. **Update** — Space の押下／解放を検知（旧 Input）、Status 点滅
3. **Space 押下** — `Microphone.Start` で録音開始、Status「録音中」
4. **Space 解放** — `Microphone.End` → 録音区間を `AudioClip` に切り出し → **WAV バイト列化** → Base64
5. **STT コルーチン** — `generateContent` に `inlineData`（`audio/wav`）＋「文字起こしだけ返す」指示 → 認識テキストを取得・表示
6. **チャットコルーチン** — 認識テキストを user として 1A と同じ `BuildRequestJson` / 履歴 / コンテキスト Toggle で LLM 呼び出し
7. **可視化** — 左: 吹き出し（user=認識文）、中央: STT Request と Chat Request（追記 or 区間表示）、右: 各 Response

### Space 押し話し（入力）

- **旧 Input Manager を使う**（新 Input System API は使わない）。
  - 押し始め: `Input.GetKeyDown(KeyCode.Space)`
  - 押している間: `Input.GetKey(KeyCode.Space)`（必要なら）
  - 離したとき: `Input.GetKeyUp(KeyCode.Space)`
- プロジェクトの Active Input Handling が Input System のみ（現状 `1`）だと旧 `Input` が効かないため、**Both（`2`）に変更する**（既存の Input System 資産は残しつつ旧 API を有効化）。
- **押し始め**で録音開始、**離したとき**に停止→変換→送信。タップ一発録音にはしない。
- 送信中（`isSending`）は録音開始しない。
- System Instruction 欄にフォーカス中は Space を録音に使わない（文字入力と衝突するため）。
- 最大録音秒数をインスペクタで持つ（例: 30 秒）。上限到達で自動停止して送信。

### マイク → 音声データ（README で厚く書く核）

Unity 側の段差をコードコメントと README 概念節の両方で固定する。

```text
Microphone.Start(device, loop, lengthSec, frequency)
  → 録音中は AudioClip にサンプルが書き込まれる
Microphone.End(device)
  → 実際に録れたサンプル数だけ切り出す（GetData / 新 AudioClip）
AudioClip の float サンプル
  → 16-bit PCM に量子化 + WAV ヘッダを付与 → byte[]（WAV）
byte[]
  → Base64 文字列
  → Request JSON の parts[].inlineData.data（mimeType: "audio/wav"）
```

README の「マイク入力と音声データとは？」節で、一般的な意味 → このデモでの見え方（Status / Request に載る場所）→ 試し方、の順で書く。

### STT と LLM を分ける理由

パイプライン図どおり **2 リクエスト**にする。

| 段 | 送るもの | 目的 | 左ペイン |
|----|----------|------|----------|
| STT | 音声 `inlineData` + 短い指示 | 発話をテキスト化 | （まだ出さない／Status のみ） |
| Chat | 認識テキスト（1A と同型の `contents`） | 返答生成 | You = 認識文、Gemini = 返答 |

- マルチターン時、履歴に載せる user は **必ずテキスト**（音声を毎回載せない）。
- 中央 Request には STT 用 JSON と Chat 用 JSON の両方を出せるようにする（学習用）。キーはマスク。

STT 用プロンプトはコード内の定数（日本語で「音声を文字起こしし、本文だけ返す」）。モデルは 1A と同じ `gemini-3.1-flash-lite` を既定にする。

### UI（1A からの差分）

残す:

- 左: System Instruction、吹き出し一覧、コンテキスト Toggle、Status
- 中央: Request、右: Response

変える:

- Message 入力欄 + 送信ボタン → **録音ガイド**（例:「Space を押しているあいだ録音」）と必要なら録音中表示
- フォーカス可能なテキスト入力を下部から外し、Space が録音に使えるようにする

見た目のレイアウト（三ペイン、フォント、色）は 1A を踏襲。

### シーン作成方針（クラウド）

- uloop / Editor は使えない前提。
- `TextToText.unity` を `SpeechToText.unity` として複製し、スクリプト参照・UI オブジェクト名・不要コンポーネントを YAML 上で差し替える。
- 差し替え後、旧 `propertyPath` / 旧クラス名が残っていないことを grep で確認する。
- 完了報告に「クラウドのため PlayMode 未実施」と書く。

---

## README 構成（WorkshopMaterial 準拠）

1. `# 2A.SpeechToText`
2. overview への1行リンク
3. **学べること**（問いだけ・3問前後）  
   例: 声が文字になる前に Unity の中では何が起きているか？ / AI が声を直接「聞いている」ように見えるのは何故か？ / …
4. **事前準備** — APIキー + マイクが使えること（動作指示）
5. **動かし方** — シーンを開く → Space で話す → 認識文と返答を見る → コンテキスト Option など
6. **概念節**
   - **マイク入力と音声データとは？**（必須・厚め: Mic→Clip→WAV→Base64）
   - STT（Speech-to-Text）とは？
   - （必要なら）Request に音声が載るとは？
7. **主要クラス** — `SpeechToText` を読む順（録音 → 変換 → STT → Chat）。`ChatBubble` は短く

書かない: 改変ヒント、次デモ誘導、フォルダ構成の説明。

---

## 実装タスク順

1. 1A から Script / Prefab を 2A へコピーし、クラス名を `SpeechToText` に変更
2. 録音・WAV 化・Space 押し話し・STT リクエストを追加（Chat 部分は 1A ロジックを維持）
3. シーンを 1A から複製し、UI 差し替え・参照配線
4. README を本文化（マイク→音声データ節を必ず含める）
5. overview の「2A は概要のみ」表記を実装済みに更新
6. コミット / push / PR 更新（クラウドのため compile / PlayMode は省略と明記）

---

## リスク・注意

| 点 | 扱い |
|----|------|
| マイク権限・デバイス無し | Start 時に検出して Status / エラー表示。無い環境では動作確認不可 |
| 短すぎる録音 | 最低秒数（例: 0.3s）未満は送らず Status で案内 |
| Request 肥大 | WAV+Base64 は大きい。中央ペインは全文表示しつつ、学習上は `inlineData` の位置が分かればよい |
| System Instruction フォーカス中の Space | 録音に使わない |
| Active Input Handling | Both へ変更（Input System のみのままだと旧 `Input` が無効） |
| シーン YAML | 破損しやすいので複製後は参照・クラス名を grep で確認 |

---

## 触らないもの

- `1A.TextToText` / `1B.TextToJSON` の挙動変更
- 2B / 3 の実装
- 共通ライブラリ化、TTS

---

## 判断の固定（このプラン内）

- STT と Chat は **2 リクエスト**（パイプラインを隠さない）
- 入力は **Space 押し話しのみ**（テキスト送信は付けない）
- Space 検知は **旧 Input Manager**（`Input.GetKeyDown` / `GetKeyUp`）。新 Input System は使わない
- Active Input Handling は **Both** に変更して旧 `Input` を有効化する
- コードは **1A コピー派生**（継承・共通化しない）

この方針で実装に進めてよいか確認する。
