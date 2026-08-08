# STT 先行へのデモ並び替え

## 要点（サマリー）

- **何をするか**: 音声パートの学習順を「TTS（出力）→ STT（入力）」から「STT（入力）→ TTS（出力）」に入れ替える。
- **推奨案（案A）**: デモ数 8 を維持。`3` を `SpeechToText`、`4` を `SpeechToSpeech`（ここで TTS 初出）。孤立した `TextToSpeech` は廃止。
- **触らないもの**: `1. TextChat` / `2. StructuredOutput` の実装、`7`/`8` の画像デモの中身、API キー手順。
- **完了条件**: `Docs/demo-series-overview.md` と `3`〜`6` の概要 README・フォルダ名が新順に揃い、相互参照が矛盾しないこと（この段階ではシーン実装はしない）。
- **一言**: 声の入り口（マイク／STT）を先に学び、次で声の出口（TTS）を足して往復にする。

| | 現状 | 変更後（案A） |
|---|------|----------------|
| 3 | TextToSpeech（Text→LLM→TTS） | **SpeechToText**（Mic→STT→LLM→Text） |
| 4 | SpeechToSpeech（Mic→STT→LLM→TTS） | **SpeechToSpeech**（同上・ここで TTS 初出） |
| 孤立 TTS | 3 で学習 | 廃止（4 の一段として初出） |
| 5〜8 | Vision / Screen / 画像 | 番号・題名は維持（参照先だけ更新） |

---

## 背景と方針

現状の「感覚を足す」は、先に **出力モダリティ（TTS）** を足し、次に **入力モダリティ（STT）** を足す積み上げです。

```text
現状:  [3] Text → LLM → TTS → Audio
       [4] Mic → STT → LLM → TTS → Audio
```

今回の方向は対称に、先に **入力（STT）**、次に **出力（TTS）** です。

```text
案A:   [3] Mic → STT → LLM → Text
       [4] Mic → STT → LLM → TTS → Audio
```

`1` / `2` の「つながる」「形で動かす」は変えません。変更は主に概要ドキュメントと未実装デモ（`3`〜`6`）のラベル・相互参照です。

---

## 推奨: 案A（デモ数 8 維持）

### 変更後の一覧

| # | フォルダ | 入力 | 処理の骨格 | 出力 | 前から増える要素 |
|---|----------|------|------------|------|------------------|
| 1 | TextChat | テキスト | LLM | テキスト | （据え置き） |
| 2 | StructuredOutput | テキスト | LLM（JSON） | UI / パラメータ | （据え置き） |
| 3 | **SpeechToText** | マイク音声 | **STT → LLM** | **テキスト** | マイク録音、STT、認識結果の可視化 |
| 4 | SpeechToSpeech | マイク音声 | STT → LLM → **TTS** | 音声 | TTS と再生（3 の後段） |
| 5 | VisionToSpeech | カメラ画像 | Vision LLM → TTS | 音声 | 画像入力（TTS は 4 と同型） |
| 6 | ScreenToSpeech | 画面キャプチャ | Vision LLM → TTS | 音声 | 入力源が画面 |
| 7 | TextToImage | テキスト | 画像生成 | 画像 | （据え置き） |
| 8 | ImageToImage | 画像＋指示 | 画像変換 | 画像 | （据え置き） |

### パイプライン図（音声パート）

```text
[3]  Mic ──► STT ─────► LLM ──────────────────────► Text
[4]  Mic ──► STT ─────► LLM ──► TTS ──────────────► Audio
[5]  Camera ──────────► Vision LLM ──► TTS ───────► Audio
[6]  Screen ──────────► Vision LLM ──► TTS ───────► Audio
```

### 学習上の狙い

- 3 は「声で聞いて、画面上は TextChat と同じテキスト返答」に留め、STT とマイクだけに焦点を当てる。
- 4 は 3 に TTS を足すだけ、と説明できる（現状の 4 が「3 に STT を足す」だった関係の入れ替え）。
- Vision / Screen はこれまでどおり TTS 出力。参照先を旧 `3. TextToSpeech` から新 `4. SpeechToSpeech` に付け替える。

### 捨てるもの

- 番号付きデモとしての **孤立 TextToSpeech**（Text→LLM→TTS のみ）。
  - TTS 単体を先に見せたい場合は下記「案B」へ。

---

## 代替: 案B（一段ずつ厳密・デモ数 9）

| # | フォルダ | 骨格 |
|---|----------|------|
| 3 | SpeechToText | Mic → STT → LLM → Text |
| 4 | TextToSpeech | Text → LLM → TTS → Audio |
| 5 | SpeechToSpeech | Mic → STT → LLM → TTS → Audio |
| 6〜9 | Vision / Screen / TextToImage / ImageToImage | 現行 5〜8 を +1 |

- 長所: 入出力を完全に一段ずつ分離できる。
- 短所: シリーズが 9 本になり、Vision 以降の番号がすべてずれる。ワークショップ時間も伸びる。

**既定は案A。** 案B にする場合は実装前に明示する。

---

## 設計の骨格（教材方針との整合）

- 1 デモ = 1 フォルダ。共通基盤への寄せすぎはしない（現行どおり）。
- パイプラインは隠さない（Status / 中間テキスト可視化は TextChat と同じ発想）。
- 3 の出力はテキストのままなので、左チャット＋ Request / Response の三ペインを流用しやすい。
- 4 で初めて `AudioClip` 再生が入る。
- 具体的なエンドポイント・モデル名は各デモ実装時に決める（今回の並び替えでは固定しない）。

---

## 作業タスク（ドキュメント並び替え）

実装（シーン／スクリプト）は別タスク。このプランの適用作業は次のみ。

1. **フォルダ改名**
   - `Assets/3. TextToSpeech/` → `Assets/3. SpeechToText/`
   - `Assets/4. SpeechToSpeech/` は題名維持（中身の説明だけ更新）
   - `.meta` の GUID は維持（削除して作り直さない）
2. **`Docs/demo-series-overview.md`**
   - 学習順表・ASCII 図・「増えるもの」表を案Aに更新
   - 「いま実装済みは 1 のみ」等の現状注記は事実に合わせて維持／修正
3. **概要 README の差し替え／更新**
   - 新 `3. SpeechToText/README.md`（骨格・学べること・まだやらないことに TTS を「次」と書く）
   - `4. SpeechToSpeech/README.md`（「3 の後段に TTS」と書き直し。旧「3 の前段に STT」を削除）
   - `5. VisionToSpeech` / `6. ScreenToSpeech` の「TTS は 3 と同型」→「4 と同型」など参照更新
4. **旧 TextToSpeech README の廃止**
   - フォルダ改名に伴い内容を SpeechToText 用に置き換える（履歴に残るので別ファイル退避は不要）

### 触らないもの

- `Assets/1. TextChat/` のコード・シーン
- `Assets/2. StructuredOutput/` の実装（README に音声デモへの誘導があれば参照だけ直す）
- `Assets/7.*` / `Assets/8.*`（番号維持）
- `Docs/gemini-ai-studio-setup.md`
- ルールファイル（並びが確定してから必要なら追記）

---

## 完了条件

- [ ] 案A（または明示した案B）で overview の表と図が一致している
- [ ] `Assets/3. SpeechToText/` が存在し、旧 `3. TextToSpeech/` が残っていない
- [ ] `3`〜`6` README の相互参照に旧順（「3=TTS」「4 の前段が STT」）が残っていない
- [ ] シーン／スクリプトの新規実装は含まない（概要段階のまま）

---

## 判断待ち（このプラン適用前）

1. **案A（8本・孤立 TTS なし）でよいか、案B（9本）か**
2. 3 の題名は `SpeechToText` でよいか（別名案: `SpeechChat` / `VoiceToText`）
