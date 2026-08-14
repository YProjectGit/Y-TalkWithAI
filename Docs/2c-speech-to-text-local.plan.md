# 2C.SpeechToTextLocal 実装プラン

## 要点（サマリー）

- **何をするか**: `2A.SpeechToText` と同じ「Space 押し話し → 文字起こし → Gemini チャット」を、STT だけ **sherpa-onnx（ローカル）** に差し替える。Chat は今どおり Gemini。
- **学ぶこと**: STT は LLM でなくてもよい。専用ASRを端末で回すと、文字まではクラウド STT より速い。
- **エンジン**: 既定は **ReazonSpeech Zipformer int8**（日本語）。SenseVoice は載せ替え余地だけ残し、初回実装では出さない。
- **つなぎ方**: **プロセス内**（公式 C API ＋薄い C# ラッパ）。localhost サーバも Unity-Sherpa-ONNX プラグインも使わない。
- **入力 / 出力**: 2A と同じ。Space 押し話し、テキスト返答。ストリーミング・VAD・TTS は入れない。
- **触らないもの**: 1A〜2B / 3A 以降の挙動。2A の Gemini STT は残す（対比用）。
- **完了条件**: シーン・スクリプト・WorkshopMaterial 準拠 README・モデル配置手順（`Docs/`）。クラウドのため Editor 検証は省略。
- **一言**: 2A が「音声を Gemini に載せる」なら、2C は「文字起こしだけ端末でやる」。

| | 2A.SpeechToText | 2C.SpeechToTextLocal（この実装） |
|---|---------------|--------------------------------|
| ユーザー入力 | Space 押し話し | **同じ** |
| STT | Gemini `generateContent`（Audio） | **sherpa-onnx OfflineRecognizer（端末）** |
| その後 | Gemini Chat → 吹き出し | **同じ** |
| STT の可視化 | HTTP Request / Response | **モデル名・経過ms / RTF・認識テキスト** |
| Chat の可視化 | HTTP Request / Response | **同じ** |
| APIキー | STT と Chat の両方 | **Chat だけ**（STT は不要） |

---

## 学習上の位置づけ

```text
[2A] Mic ──► Gemini STT ─────► Gemini Chat ──► Text
[2C] Mic ──► sherpa STT ─────► Gemini Chat ──► Text
[3B] Mic ════════════► Live API ══════════════════► Audio
```

- 増える要素は **STT の実行場所** だけ。録音と Chat は 2A のコピー。
- 学生が追う山場は「音声が HTTP の `inlineData` に載らない」ことと、「専用ASRの呼び出しが Chat の前に終わる」こと。
- 3B（Live）より速くはならない。速くなるのは **文字が出るまで**。声の往復は 3B の領域、と README では書かない（次デモ誘導禁止）。プラン上の境界だけここに記す。

シリーズ概要（`Docs/demo-series-overview.md`）の学習順は `2A` → **`2C`** → `2B` にはしない。`2C` は 2A の対比デモとして、表では 2A の直後に置く（2B の JSON とは独立）。

---

## 設計の骨格

### 判断の固定

| 項目 | 採用 | 採用しない |
|------|------|------------|
| 番号・題名 | `2C.SpeechToTextLocal` | 2A の置換、`2C.SpeechToTextSherpa` |
| モデル | ReazonSpeech Zipformer **int8**（2024-08-01） | Whisper Large、Sentis Whisper Tiny、初回の SenseVoice |
| 認識モード | **offline**（録り終わり一括） | online ストリーミング、VAD 自動区切り |
| つなぎ方 | プロセス内（C API） | localhost HTTP、Unity-Sherpa-ONNX 一式 |
| ラッパ | デモ内の薄い 1 クラス | 共通基盤、DI、エンジンプール |
| Chat | 2A と同じ Gemini REST | ローカル LLM |
| モデルファイル | リポジトリに入れない。`Docs/` で配置 | Git LFS で onnx をコミット |

### ファイル（予定）

| パス | 由来 |
|------|------|
| `Assets/2C.SpeechToTextLocal/SpeechToTextLocal.unity` | `2A` シーンを複製し、コンポーネントと欄タイトルを差し替え |
| `Assets/2C.SpeechToTextLocal/Script/SpeechToTextLocal.cs` | `SpeechToText.cs` をコピー。STT 段だけ差し替え |
| `Assets/2C.SpeechToTextLocal/Script/SherpaOfflineAsr.cs` | 新規。モデル読み込みと offline 認識だけ持つ |
| `Assets/2C.SpeechToTextLocal/Prefab/MessageBubble.prefab` | 2A からコピー（`ChatBubble` は `Assets/Common` を参照） |
| `Assets/2C.SpeechToTextLocal/README.md` | WorkshopMaterial 準拠 |
| `Assets/2C.SpeechToTextLocal/Resource/models/` | 配置先（中身は gitignore。`.gitkeep` のみ） |
| `Assets/2C.SpeechToTextLocal/Resource/Plugins/` | ネイティブライブラリ配置先（中身は gitignore） |
| `Docs/sherpa-onnx-setup.md` | モデルとネイティブ lib の取得・配置 |
| `Docs/demo-series-overview.md` | 2C の行を追加 |

共通基底や `Assets/Common` への録音／ASR 切り出しは **しない**。`SherpaOfflineAsr` はこのデモの Script に閉じる。

### 処理の流れ

```text
Play
  → APIキー読込（Chat 用）・SystemInstruction・マイク確認
  → SherpaOfflineAsr.Initialize（モデルが無ければ Status で Docs を案内）

Space 押下
  → Microphone.Start（2A と同じ）

Space 解放
  → Microphone.End → サンプル切り出し
  → float[] を SherpaOfflineAsr.Recognize（バックグラウンド）
  → 認識テキストを 1. / 2. 欄へ（経過ms / RTF）
  → 2A と同じ Chat（3. / 4.）
```

`SpeechToTextLocal.cs` の読む順:

1. **Start** — キー / 事前指示 / マイク / sherpa 初期化
2. **Update** — Space 押し話し（旧 Input）、Status 点滅
3. **BeginRecording / EndRecordingAndSend** — 2A と同じ録音。WAV / Base64 は **作らない**
4. **RecognizeThenChatCoroutine** — ローカル STT → 認識文を吹き出し → Gemini Chat
5. **Chat** — 2A の `BuildChatRequestJson` / `PostJsonCoroutine` を維持

### sherpa の入れ方（`SherpaOfflineAsr`）

公式 [sherpa-onnx C# API](https://k2-fsa.github.io/sherpa/onnx/csharp-api/index.html) の **offline transducer** だけを使う。NuGet は Unity と相性が悪いので、次をデモに同梱する。

- 公式の C# バインディングから、offline 認識に必要なファイルだけを `Script/` に置く（生成物の機械編集はしない。使う API 面を短く保つ）
- ネイティブは prebuilt（`sherpa-onnx-c-api` と依存 `.dll` / `.dylib` / `.so`）
- 対象プラットフォーム（初回）: **Windows x64** と **macOS arm64**（ワークショップ想定）。Linux x64 は配置手順だけ Docs に書く
- 認識はメインスレッドを止めない（`Task` / スレッドプール → コルーチンで待つ）
- 初期化は Play 時に一度。2回目以降の発話はロードしない

認識 API のイメージ（実装時に公式に合わせる）:

```text
OfflineRecognizerConfig
  tokens / encoder / decoder / joiner のパス
  numThreads = 1 または 2
CreateStream → AcceptWaveform(sampleRate, float[]) → Decode → GetResult().Text
```

入力は Unity の `AudioClip.GetData` の float（-1〜1）。16-bit WAV 化はしない。サンプルレートは 2A と同じ 16000。

モデル（配置後のファイル名。公式アーカイブのまま）:

| 役割 | ファイル |
|------|----------|
| encoder | `encoder-epoch-99-avg-1.int8.onnx` |
| decoder | `decoder-epoch-99-avg-1.onnx`（公式例どおり fp32 decoder） |
| joiner | `joiner-epoch-99-avg-1.int8.onnx` |
| 語彙 | `tokens.txt` |

配布元:  
`https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-zipformer-ja-reazonspeech-2024-08-01.tar.bz2`

ライセンス: sherpa-onnx / ReazonSpeech とも Apache 2.0。`Docs/sherpa-onnx-setup.md` に出典とライセンスを1段落書く。

### 可視化（三ペイン）

2A の四欄は残す。STT 側の中身だけ変える。

| 欄 | 2A | 2C |
|----|----|----|
| 1 | Request - GenerateContent（Audio） | **Local STT（sherpa-onnx）** … モデル名・ファイル・スレッド数・サンプル数 / 秒 |
| 2 | Response - GenerateContent（Audio） | **認識結果** … テキスト、経過ms、RTF（経過 / 音声秒） |
| 3 | Request - GenerateContent（Text） | 同じ（Gemini Chat） |
| 4 | Response - GenerateContent（Text） | 同じ |

左ペイン（System Instruction、吹き出し、Status、録音案内）は 2A を踏襲。案内文だけ「離すとローカルで文字起こし → 返信」に変える。

Status の例: `モデル読み込み中` → `待機中（Space で録音）` → `録音中` → `ローカル STT 中` → `3. Request 送信中` → `完了`。

### 事前準備（学生）

README の事前準備は動作指示だけ。詳細は Docs へ。

1. Gemini APIキーを `Assets/Common/APIKey.txt` に保管（Chat 用。STT には使わない）
2. `Docs/sherpa-onnx-setup.md` に従い、モデルとネイティブ lib を配置
3. マイクが使えること

モデル未配置でもシーンは開ける。Play 時にパスを確認し、Status と 2. 欄に配置手順への案内を出す（例外で落とさない）。

### シーン作成方針（クラウド）

- uloop / Editor は使えない前提。
- `SpeechToText.unity` を複製し、スクリプト参照・欄タイトル・クラス名を YAML 上で差し替える。
- 旧 `propertyPath` / 旧クラス名が残っていないことを grep で確認する。
- UI 骨格は 2A と同じ（ペイン分割は変えない）ので、構成イメージ画像は出さない。
- 完了報告に「クラウドのため Editor / PlayMode 未実施」と書く。ネイティブ lib 未配置のクラウドでは **コンパイルまで** が検証上限。

---

## README 構成（WorkshopMaterial 準拠）

1. `# 2C.SpeechToTextLocal`
2. overview への1行リンク
3. **学べること**（トピック＋短い説明、3つ）
   - **ローカル STT** … 音声の文字起こしを、クラウドではなく端末のエンジンで行う
   - **専用 ASR** … 文章生成モデルではなく、音声認識専用のモデルを使う
   - **音声→テキスト会話** … 入口だけ差し替えて、あとのチャットは 2A と同じ
4. **事前準備** — APIキー（Chat 用）＋ sherpa 配置（Docs リンク）＋ マイク
5. **動かし方** — シーンを開く → Space で話す → 1./2. でローカル認識、3./4. で Chat
6. **概念節**
   - **ローカル STT とは？**（一般 → このデモでの見え方 → 試し方。2A との対比はここだけ）
   - **専用 ASR とは？**（LLM に音声を載せるのではない、という差）
7. **主要クラス** — `SpeechToTextLocal` を入口、`SherpaOfflineAsr` を認識役

書かない: 改変ヒント、次デモ誘導、フォルダ構成、SenseVoice / Whisper との性能表、Status 点滅の説明。

---

## Docs/sherpa-onnx-setup.md（用意する手順）

動作指示で、次を固定する。

1. 公式 ASR アーカイブをダウンロードして展開する
2. 上表の 4 ファイルを `Assets/2C.SpeechToTextLocal/Resource/models/` に置く
3. 自分の OS 向け prebuilt から `sherpa-onnx-c-api` 一式を `Resource/Plugins/` に置く（Unity のプラットフォーム設定は実装時に合わせて書く）
4. 戻って Unity にフォーカスし、Play する

`.gitignore` に次を足す（トークンやスクリプトは残す）:

- `Assets/2C.SpeechToTextLocal/Resource/models/*.onnx`
- `Assets/2C.SpeechToTextLocal/Resource/Plugins/**/*.dll`
- `Assets/2C.SpeechToTextLocal/Resource/Plugins/**/*.dylib`
- `Assets/2C.SpeechToTextLocal/Resource/Plugins/**/*.so`

---

## 実装タスク順

1. `.gitignore` と `Resource/models/.gitkeep`、`Docs/sherpa-onnx-setup.md` を先に置く
2. 2A から Script / Prefab / シーンを 2C へコピーし、クラス名を `SpeechToTextLocal` に変更
3. `SherpaOfflineAsr` を追加し、STT 段をローカル認識に差し替え（WAV / Base64 / STT HTTP を削除）
4. 1. / 2. 欄の表示をローカル推論ログに変更。3. / 4. は 2A のまま
5. README と overview を更新
6. コミット / push / PR。クラウドのため compile はスクリプトのみ。PlayMode は省略と明記

---

## リスク・注意

| 点 | 扱い |
|----|------|
| モデル / ネイティブ未配置 | Play 時に検出。Status と 2. 欄で Docs を案内。例外で落とさない |
| 初回ロードが数秒 | Status「モデル読み込み中」。2回目の発話では再ロードしない |
| メインスレッド停止 | 認識はバックグラウンド。UI 更新だけメインへ戻す |
| 日本語以外 | ReazonSpeech は日本語のみ。英語は誤る。README では「日本語で話す」と動作指示する |
| プラットフォーム | 初回は Win x64 / macOS arm64。それ以外は Docs に「未検証」と書く |
| シーン YAML | 2A 複製後、旧クラス名 / 旧 `propertyPath` を grep |
| 2A との混同 | フォルダ・クラス・シーン名を `SpeechToTextLocal` で揃える |
| 性能の見せ方 | 2. 欄に ms / RTF を出す。2A との同時計測 UI は作らない（1デモ1山場） |

---

## 触らないもの

- `2A.SpeechToText` / `2B.SpeechToJSON` / `3A` の Gemini STT
- 3B Live、Sentis、Whisper Large
- 共通 ASR ライブラリ化
- ストリーミング、VAD、TTS、ローカル LLM
- 画像デモ（7B など）

---

## このプランで実装に進めてよいか

進めるときの作業単位は「2C 一式（シーン・Script・README・配置 Docs）」だけ。SenseVoice 差し替えや 2B/3A への横展開は、このデモが動いてから別プランにする。
