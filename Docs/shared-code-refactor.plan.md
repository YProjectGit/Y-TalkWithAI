# 共通コード共有へのリファクタリング計画

## 要点（サマリー）

- **何をするか**: 「デモごとに全部コピー」という現在の縛りを外し、**配管（plumbing）は `Assets/Common/` に寄せる／プロトコルと流れはデモに残す** の二層に整理する。
- **なぜ**: 13 デモ・約 14,400 行のうち、コメントと空白を無視して**完全一致するコピーだけで約 3,500 行**。名前が同じメソッドまで含めると各ファイルの **40〜67%** が他デモと重なっている。1 か所の修正が 13 か所に散る状態になっている。
- **判断の軸**: 「学生が読むべきコード」か「読まなくていいコード」か。JSON エスケープ・WAV 変換・WebSocket の後始末は後者。何を送って何が返るかは前者。
- **効果の見込み**: 共有ライブラリ約 1,500 行を新設し、デモ側から約 5,000 行を削除。メインスクリプト 1 本あたり **700〜1,600 行 → 350〜800 行**。
- **前提の変更**: 現行の規約（[WorkshopMaterial.mdc](../.cursor/rules/WorkshopMaterial.mdc)・[demo-series-overview.md](demo-series-overview.md)）は「共通基盤への寄せすぎは避ける」と明記している。**この計画はその条項の改訂を伴う**ので、Phase 0 で先に規約を直す。
- **一言**: 「全部コピー」から「配管は共有・主題はコピー」へ。学生が読む行数はむしろ減る。

---

## 1. 現状の計測

対象は各デモのメインスクリプト（`Assets/{番号}.{題名}/Script/*.cs`）。`2C` の `SherpaOnnx/` 配下はベンダー提供のバインディングなので除外。

| | 行数 | 他デモと同名メソッドが占める割合 |
|---|---:|---:|
| 1A.TextToText | 761 | 66% |
| 1B.TextToJSON | 784 | 61% |
| 2A.SpeechToText | 995 | 80% |
| 2B.SpeechToJSON | 1,038 | 72% |
| 2C.(SpeechToTextSherpa) | 919 | 80% |
| 2D.(SpeechToTextWhisper) | 993 | 74% |
| 3A.SpeechToSpeech | 1,367 | 65% |
| 3B.SpeechToSpeechLiveAPI | 1,569 | 79% |
| 3C.SpeechToMotion | 1,617 | ※ |
| 4.VisionToSpeech | 1,530 | 76% |
| 5.ScreenToSpeech | 885 | 77% |
| 6.TextToImage | 776 | 67% |
| 7.ImageToImage | 702 | 63% |
| **合計** | **14,438** | |

※ 3C は自動計測が途中で崩れたため未算出（`ExtractArgsObjectNear` 内の波括弧リテラルが原因）。手で追うと 3B とほぼ同じ比率。

**コメントと空白を無視して完全一致するコピーだけで約 3,527 行。** 代表例:

| メソッド | 完全一致しているデモ | 1 本あたり |
|---|---|---:|
| `EscapeJson` | 1A・1B・2A・2B・2C・2D・3A・3B・4・5・6・7（12 本） | 36 行 |
| `PrettyPrintJson` | 1A・1B・2A・2B・2C・2D・3A・3B・4・6（10 本） | 62 行 |
| `SetStatus` / `UpdateStatusBlink` | 11 本ずつ | 13 / 12 行 |
| `MaskApiKey` | 9 本 | 14 行 |
| `SystemInstruction` 同期一式（7 メソッド） | 1A・2A・2C・2D・3A（＋3B・4 が別系統） | 約 90 行 |
| `CloseSocket` / `ExtractJsonStringFieldFrom` | 3B・4・5 | 44 / 54 行 |
| `SendJsonCoroutine` / `SendTextAsync` / `Pcm16ToClip` / `ClearPlaybackQueue` | 3B・3C・4・5 | 18 / 9 / 19 / 12 行 |
| `ConvertAudioClipToWav` / `TrimClip` / `PostJsonCoroutine` | 2A・2B・3A（＋2C・2D） | 46 / 24 / 19 行 |
| カメラ矩形一式（`EnsureBackgroundClearCamera` ほか） | 1B・2B・3C | 約 130 行 |
| `MessageBubble.prefab` | 1A・2A・2C・2D・3A・3B・4（バイト単位で同一、GUID だけ 7 種） | — |

**「名前は同じだが中身が違う」ものもある**（後述の差異一覧）。ここが一括置換で事故る場所なので、共有化の前に潰す。

---

## 2. 方針 — 何を共有し、何を共有しないか

線引きは「教材としてそのコードを読ませたいか」で決める。

### 共有する（読まなくていい配管）

| 領域 | 理由 |
|---|---|
| JSON のエスケープ・整形・省略表示 | Gemini の話ではなく文字列処理。読んでも API の理解は進まない |
| API キーの読み込み・マスク | 全デモで同一。手順は `Docs/` 側で説明済み |
| SystemInstruction.txt と入力欄の同期 | ファイル I/O の都合。API の話ではない |
| Status の点滅・ペインへの追記・吹き出し追加 | 見せ方の実装であって通信ではない |
| WAV エンコード / PCM16 変換 / AudioClip 生成 | 音声フォーマットの定型処理 |
| マイク録音の開始・停止・切り出し | Unity の `Microphone` API の作法 |
| WebSocket の接続・受信ループ・メインスレッド復帰・後始末 | 非同期の定型。**送る中身は共有しない** |
| WebCamTexture の起動・停止・JPEG 化 | Unity の定型処理 |
| EventSystem / AudioListener の用意 | シーンの下準備 |
| `MessageBubble.prefab` | バイト単位で同一 |

### 共有しない（デモの主題そのもの）

| 領域 | 理由 |
|---|---|
| `BuildRequestJson` / `BuildSetupJson` / `BuildSttRequestJson` / `BuildChatRequestJson` | **何を送るか**が各デモの学習ポイント。ここを共通ビルダーに隠すとデモの意味が消える |
| レスポンスから「どのキーを取るか」 | 同上。取り出す作法（走査ヘルパー）だけ共有し、キー名はデモに書く |
| `responseSchema` / `tools` 宣言 | 1B・2B・3C の主題 |
| 各デモのメインコルーチン（流れ） | 上から読んで追える 1 本道であることが教材の価値 |
| `ShowError` / `SetSending` | デモごとに「何を止めるか」「どこに出すか」が違う。共有すると分岐だらけになる |
| ペインの番号づけと文言 | デモごとの説明の一部 |

### 原典デモの扱い（要判断）

`1A`（REST の原典）と `3B`（Live の原典）をどう扱うかで 2 案ある。

| | 案 A: 原典は完全自己完結のまま | 案 B: 原典も配管は共有 |
|---|---|---|
| 1A / 3B | 1 ファイル読めば全部わかる | 配管は Common、通信の骨格は自前 |
| 他デモ | 共有クラスを使う | 同左 |
| 利点 | 「まず 1A を通読する」導線が完璧に残る | 修正が 1 か所で済む。デモ間の一貫性が保てる |
| 欠点 | `EscapeJson` などが 1A と Common に二重に存在し、直し忘れが起きる | 1A でも `GeminiJson.Escape` を追う必要がある |

**推奨は案 B。** 二重管理は今回の目的に反する。案 B を採っても 1A の「送る → 待つ → 取り出す」の本体は 1A に残るので、通読の導線は失われない。

---

## 3. 共有ライブラリの構成案

```text
Assets/Common/
  APIKey.txt                       （既存・コミットしない）
  SystemInstruction.txt            （既存）
  Prefab/
    MessageBubble.prefab           ← 7 デモの同一 prefab を 1 本に統合
  Script/
    Ui/
      ChatBubble.cs                （既存を移動。GUID は .meta が持つのでシーン参照は切れない）
      ChatLog.cs                   吹き出し追加 + 下端スクロール
      StatusLabel.cs               Status 文言 + 応答待ちの点滅
      PaneLog.cs                   ペインへの追記・プレースホルダ・Base64 の省略表示
      SceneBootstrap.cs            EventSystem / AudioListener の用意
    Gemini/
      GeminiKey.cs                 APIキー読込・マスク・generateContent URL 組み立て
      GeminiJson.cs                Escape / PrettyPrint / Truncate
      GeminiJsonScan.cs            Live 用の素朴な走査（キー検索・文字列取り出し・数値・波括弧対応）
      GeminiTextResponse.cs        candidates[0].content.parts[0].text を取り出す DTO と抽出
      GeminiRestPost.cs            POST コルーチン + HttpResult + Request/Response の表示整形
      SystemInstructionField.cs    SystemInstruction.txt ↔ TMP_InputField の同期
    Live/
      LiveSession.cs               WebSocket 接続・setup 送信・受信ループ・メインスレッド復帰・切断
      LiveAudioPlayer.cs           受信 PCM のキュー再生
    Media/
      MicRecorder.cs               押し話し録音 → AudioClip 切り出し
      MicStreamer.cs               連続録音 → PCM16 チャンク
      WavCodec.cs                  AudioClip ⇄ WAV / PCM16 ⇄ AudioClip
      WebcamFrame.cs               WebCamTexture 起動・停止・JPEG 化・縮小
      GeneratedImageView.cs        inlineData → Texture2D 表示と解放
    Stage/
      CubeStage.cs                 1B / 2B / 3C のカメラ矩形合わせ・背景クリアカメラ
```

### 設計上の決めごと

1. **シーンの再配線を起こさない。** 各デモの `public TMP_Text statusText` などのインスペクタ公開フィールドは**名前も型も変えない**。共有クラスには呼び出し側が参照を渡す。
   ```csharp
   // デモ側（インスペクタ配線はこれまでどおり）
   public TMP_Text statusText;
   readonly StatusLabel status = new StatusLabel();

   void Start()  { status.Bind(statusText); status.Set("待機中", false); }
   void Update() { status.Tick(); }
   ```
2. **`MonoBehaviour` を増やさない。** `LiveSession` も `MicRecorder` もプレーンクラス。コルーチンは呼び出し側の `MonoBehaviour` が回す。
   ```csharp
   yield return live.ConnectRoutine(apiKey, modelName, BuildSetupJson());  // setup の中身はデモが作る
   ```
3. **名前空間は導入しない。** 教材で `using` 行が増えるのを避ける。代わりに `Gemini` / `Live` / `Mic` / `Wav` の接頭辞で衝突を防ぐ。
4. **asmdef は作らない。** 現状このプロジェクトに `.asmdef` は 1 つもなく、全コードが `Assembly-CSharp` にある。Common を切り出すと 2C / 2D の外部パッケージ参照を明示的に足す必要が出て、得より面倒が勝つ。
5. **共有層は外部パッケージに依存しない。** sherpa-onnx / whisper.unity への依存は 2C / 2D の中に閉じる。
6. **入れ子 private クラスの整理。** 現在 `ChatTurn` / `HttpResult` / `GeminiResponse` は各ファイルの入れ子 private クラスなので衝突していない。共有版に移すデモからは入れ子を削除する（移行途中は両方残さない）。

---

## 4. 共有化の前に潰す差異

「名前は同じだが中身が違う」もの。一括置換すると挙動が変わる。

| メソッド | 差異 | 対処 |
|---|---|---|
| `LoadApiKey` | 12 本すべて別実装だが、差はログ接頭辞（`[TextToText]` など）とエラー表示先だけ | 接頭辞を引数に。エラー表示は呼び出し側に返す |
| `SetStatus` | 5 のみ 1 引数版（点滅なし） | 共有版は 2 引数。5 は `Set(text, false)` に統一 |
| `LoadSystemInstructionFromFile` | 3C だけ `LoadSystemInstruction` という別名で、リロード機能を持たない | 共有版に寄せて 3C も同挙動にする（挙動が変わる点を README に書く） |
| `ReloadSystemInstructionFromFileIfChanged` | 2C・2D 版と 1A・2A・3A 版で分岐が違う | 機能の多い側へ統一 |
| `BeginRecording` / `EndRecording` | 6 系統。押し話しと Live ストリームで役割が違う | `MicRecorder`（押し話し）と `MicStreamer`（常時）に分ける |
| `ShowError` | 12 本すべて別実装。吹き出しに出すか・Response 欄に足すかが違う | **共有しない**。各デモに残す |
| `SetSending` | 3 系統。無効化する UI が違う | **共有しない**。各デモに残す |
| `Update` | 8 系統 | **共有しない**。呼ぶ Tick が違うだけ |
| `ReceiveLoopAsync` | 3B・4 が同一、3C・5 が別 | 差分を確認して最も安全な版へ統一 |
| `TryExtractAndEnqueueAudio` | 3B・4 が同一、5 が別 | 同上 |

**着手前に、この表を実際の diff で埋め直すこと。** ここを飛ばすと Phase 4・5 で挙動が変わる。

---

## 5. 削減の見込み

自動計測に基づく概算（3C は 3B からの類推）。

| 領域 | 現状の合計 | 共有後（Common 1 本） | 削減 |
|---|---:|---:|---:|
| JSON 文字列ユーティリティ | 1,173 | 110 | -1,060 |
| Live 用 JSON 走査 | 約 410 | 90 | -320 |
| API キー | 426 | 60 | -366 |
| SystemInstruction 同期 | 781 | 110 | -670 |
| Status / ログ UI | 540 | 120 | -420 |
| REST POST と表示整形 | 183 | 40 | -143 |
| マイク録音（押し話し） | 525 | 120 | -405 |
| マイク常時ストリーム | 226 | 130 | -96 |
| Live セッション | 約 860 | 230 | -630 |
| Live 音声再生 | 453 | 160 | -293 |
| カメラ / フレーム取得 | 218 | 100 | -118 |
| キューブ舞台 | 約 380 | 140 | -240 |
| レスポンス解析 | 198 | 40 | -158 |
| 画像表示 | 120 | 60 | -60 |
| シーン下準備 | 60 | 20 | -40 |
| **合計** | **約 6,550** | **約 1,530** | **約 -5,020** |

デモ側の見込み:

| | 現状 | 見込み |
|---|---:|---:|
| 1A.TextToText | 761 | 約 430 |
| 1B.TextToJSON | 784 | 約 460 |
| 2A.SpeechToText | 995 | 約 470 |
| 2B.SpeechToJSON | 1,038 | 約 560 |
| 2C.(SpeechToTextSherpa) | 919 | 約 460 |
| 2D.(SpeechToTextWhisper) | 993 | 約 535 |
| 3A.SpeechToSpeech | 1,367 | 約 815 |
| 3B.SpeechToSpeechLiveAPI | 1,569 | 約 670 |
| 3C.SpeechToMotion | 1,617 | 約 800 |
| 4.VisionToSpeech | 1,530 | 約 700 |
| 5.ScreenToSpeech | 885 | 約 370 |
| 6.TextToImage | 776 | 約 530 |
| 7.ImageToImage | 702 | 約 455 |
| **デモ合計** | **14,438** | **約 7,900** |
| Common | 38 | 約 1,530 |
| **総計** | **14,476** | **約 9,430**（-35%） |

**学生が 1 デモを読むときの行数は 700〜1,600 行から 350〜800 行に半減する。** これが今回いちばん大きい効果。

---

## 6. 段階計画

1 Phase = 1 コミット。各 Phase は単独でコンパイルが通り、単独で巻き戻せる状態にする。

### Phase 0 — 規約の改訂（コード変更なし）

**先にここを直さないと、以降の全 Phase が既存ルール違反になる。**

- `.cursor/rules/WorkshopMaterial.mdc`
  - 「共通基盤への過度な寄せすぎは避ける（必要最小限の共有のみ）」→ 「配管は `Assets/Common/` に寄せる。プロトコルと流れはデモに残す」へ書き換え
  - 「フォルダ構成（デモ単位）」節に `Assets/Common/Script/` のサブフォルダ規約（`Ui` / `Gemini` / `Live` / `Media` / `Stage`）を追記
  - 実装時のチェックリストに「新しい処理は、配管なら Common・主題ならデモ、の判断をしたか」を追加
- `Docs/demo-series-overview.md`
  - 「設計の骨格」の「共通基盤への寄せすぎはしない」を差し替え、共有／非共有の線引き表を載せる

### Phase 1 — 純関数（リスク最小）

`GeminiJson` / `GeminiJsonScan` / `WavCodec` を新設し、13 デモから該当メソッドを削除して呼び出しに置換。

- 状態を持たない `static` メソッドのみ。シーンもインスペクタも触らない
- 対象: `EscapeJson`・`PrettyPrintJson`・`TruncateForDisplay`・`TruncateBase64ForDisplay`・`ExtractNestedTextAfterKey`・`ExtractJsonStringFieldFrom`・`IndexOfJsonKey`・`TryExtractJsonNumber`・`ExtractBalancedObject`・`ConvertAudioClipToWav`・`FloatsToPcm16`・`Pcm16ToClip`・`TrimClip`・`CopyClipSamples`
- 削減 約 1,500 行

### Phase 2 — 設定と UI ヘルパー

`GeminiKey` / `SystemInstructionField` / `StatusLabel` / `PaneLog` / `ChatLog` / `SceneBootstrap` を新設。

- 公開フィールド名は据え置き。シーンの再配線なし
- 差異一覧（4 節）の `LoadApiKey`・`SetStatus`・`LoadSystemInstruction` の統一をここで実施
- `ShowError` / `SetSending` / `Update` は各デモに残す
- 削減 約 1,500 行

### Phase 3 — Prefab 統合

`MessageBubble.prefab` を `Assets/Common/Prefab/` に 1 本化し、7 デモのコピーを削除。

- **唯一シーンの書き換えを伴う Phase**。7 つの `.unity` の該当 GUID を Common 版へ置換する
- 手順: 統合先の GUID を控える → 各シーンの旧 GUID を `sed` で置換 → 旧 prefab と `.meta` を削除 → Editor で全シーンを開いて Missing 参照がないか確認
- 単独コミットにして、問題があればここだけ戻せるようにする

### Phase 4 — REST 系（対象: 2A・2B・2C・2D・3A、および 1A・1B・6・7 の POST 部）

`GeminiRestPost` / `GeminiTextResponse` / `MicRecorder` を新設。

- `PostJsonCoroutine`・`HttpResult`・`FormatHttpRequestForDisplay`・`FormatHttpResponseForDisplay`・`BuildGenerateContentUrl`・`TryExtractAssistantText`・`SetupMicrophone`・`BeginRecording` を移動
- `BuildSttRequestJson` / `BuildChatRequestJson` / `BuildRequestJson` は**移動しない**
- 削減 約 900 行

### Phase 5 — Live 系（対象: 3B・3C・4・5）

`LiveSession` / `LiveAudioPlayer` / `MicStreamer` を新設。**いちばん重い Phase。**

- `ConnectLiveSessionCoroutine` の接続部・`SendTextAsync`・`SendJsonCoroutine`・`ReceiveLoopAsync`・`CloseSocket`・`EnqueueMain`・`DrainMainThreadActions`・`PlaybackPumpCoroutine`・`ClearPlaybackQueue`・`EnsurePlaybackAudioSource`・`PumpMicrophoneChunksIfStreaming` を移動
- `BuildSetupJson`・`HandleServerMessage`・`TryExtractAndEnqueueAudio` の**キー名の解釈部分**は各デモに残す
- 3C の function call（`HandleToolCallOnMain` / `BuildFunctionResponseJson`）は 3C 専用なので移動しない
- 削減 約 1,000 行
- **PlayMode での実動確認が必要な唯一の Phase**（接続・音声再生・切断）

### Phase 6 — 表示系（対象: 1B・2B・3C・4・5・6・7）

`WebcamFrame` / `CubeStage` / `GeneratedImageView` を新設。

- `SetupWebcam`・`StopWebcam`・`TryCaptureJpeg`・`ScaleTexture`・`EnsureBackgroundClearCamera`・`UpdateCameraViewportToPreview`・`RestoreCameraViewport`・`TryShowGeneratedImage`・`ReleaseGeneratedTexture`・`NormalizeInlineDataKeys` を移動
- 削減 約 500 行

### Phase 7 — ドキュメント追従

- 各デモ README の「主要クラス」節に **「このデモが自前で持つもの / Common に任せているもの」の 2 列表**を追加
- `Docs/demo-series-overview.md` に Common の構成図を追加
- Common 内の各ファイル冒頭に「何のための配管か / どのデモが使うか」を日本語で書く（教材のコメント規約は Common にも適用する）

---

## 7. リスクと緩和策

| リスク | 影響 | 緩和策 |
|---|---|---|
| **教材価値の低下**。「1 ファイル読めば全部わかる」が崩れる | 大 | 2 節の線引きを厳守する。主題（送る中身・返る中身・流れ）は絶対に Common へ移さない。README に「自前 / Common」の表を置き、Common 側にも「どのデモが使う配管か」を書く |
| シーン参照の破損（Missing Script / Missing Prefab） | 大 | 公開フィールドの名前・型を変えない。Prefab 統合は Phase 3 に隔離し、全シーンを開いて確認してからコミット |
| 名前衝突（全コードが `Assembly-CSharp` 単一） | 中 | 接頭辞で回避。共有版へ移したデモからは入れ子 private クラスを必ず削除する |
| 「同名だが別実装」の取りこぼしで挙動が変わる | 中 | 4 節の差異一覧を実 diff で埋めてから着手。統一で挙動が変わるもの（3C の SystemInstruction リロードなど）は README に明記 |
| 2C / 2D の外部パッケージ依存が Common に漏れる | 中 | 共有層は sherpa-onnx / whisper.unity を参照しない。ローカル STT の呼び出しはデモ側に閉じる |
| Live 系の非同期リグレッション（切断漏れ・音声の途切れ） | 中 | Phase 5 は PlayMode で 4 デモすべて実動確認。`OnDestroy` での切断も確認する |
| Phase が大きくなりすぎて戻せない | 中 | 1 Phase = 1 コミット。Phase 5 はさらに「セッション」「再生」「マイク」の 3 コミットに割ってよい |
| クラウド環境では Unity Editor 検証ができない | 小 | [UnityDev.mdc](../.cursor/rules/UnityDev.mdc) の方針どおり、クラウドではコード変更まで。Phase 3 と 5 はローカル Editor のあるときに実施する |

---

## 8. 検証と完了条件

各 Phase 共通:

- `compile` の ErrorCount が 0
- 変更したデモのシーンを開き、Missing 参照が出ないこと
- 削除したメソッドの参照が残っていないこと（`grep` で確認）

Phase ごとの追加確認:

| Phase | 追加確認 |
|---|---|
| 1・2 | コンパイルのみ（静的変更） |
| 3 | 7 シーンすべてを開き、`MessageBubble` 参照が Common 版を指していること |
| 4 | 2A で 1 往復（録音 → STT → Chat）。1B・6 で 1 往復 |
| 5 | 3B・3C・4・5 で接続 → 発話 → 音声再生 → 切断まで PlayMode 実行 |
| 6 | 4・7 でカメラ、1B・2B・3C でキューブ表示 |

全体の完了条件:

- 13 デモすべてが従来どおり動く
- `Assets/Common/Script/` 配下がコメント規約を満たしている
- 規約ドキュメント（Phase 0）と README（Phase 7）が実態と一致している

---

## 9. やらないこと

- **asmdef の導入** — 現状ゼロ。切ると 2C / 2D の依存記述が増えるだけ
- **名前空間の導入** — 教材で `using` を増やさない
- **JSON ライブラリの導入**（Newtonsoft など）— 「素朴な文字列処理で JSON を組む」ことは教材の一部
- **UI レイアウトの作り直し** — 今回は配線を変えないことが前提
- **`2C/Script/SherpaOnnx/` への手入れ** — ベンダー提供コード
- **デモの追加・削除・並べ替え** — 構成は現状維持
- **共通ビルダーによる `BuildRequestJson` の抽象化** — これをやると教材が壊れる

---

## 10. 進め方の提案

Phase 0 の規約改訂だけ先に確定させ、Phase 1・2 を 1 セット（コンパイルのみで検証でき、約 3,000 行減る）で実施するのが費用対効果が高い。Phase 3 以降は Editor 検証が必要なので、ローカルで Unity を開けるタイミングに合わせる。
