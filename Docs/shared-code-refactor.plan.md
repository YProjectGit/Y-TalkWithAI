# 共通ユーティリティ切り出し計画

> **実施済み（Phase 0〜3 すべて）。** 以下は計画時の記述をそのまま残す。実績との差は末尾「実施結果」を参照。

## 要点（サマリー）

- **何をするか**: デモ間で繰り返し現れる**純粋関数だけ**を `Assets/Common/Script/` に切り出す。それ以外は今までどおりデモごとにコピーのまま残す。
- **対象**: JSON エスケープ／整形、JSON 走査、WAV / PCM 変換、APIキー読込、レスポンスからのテキスト取り出し、HTTP 表示整形、テクスチャ縮小。**17 メソッド・約 2,700 行の重複**。
- **対象外**: WebSocket セッション、マイク制御、音声再生、Status 点滅、吹き出し、SystemInstruction 同期、カメラ矩形、Prefab。これらは**状態とライフサイクルを持つ＝デモの背骨**なので触らない。
- **効果**: ライブラリ約 570 行を新設し、デモ側から約 2,700 行を削除。**正味 約 2,100 行減（-15%）**。
- **リスク**: シーン変更なし・インスペクタ配線変更なし・PlayMode 検証不要。**検証は `compile` のみ**で完結する。
- **一言**: 葉っぱだけ共有する。背骨はコピーのまま残す。

---

## 1. スコープの決め方

「ユーティリティかどうか」は主観になるので、機械的に判定できる条件にする。

### 採用条件（全部満たすもののみ）

1. `static` で書ける（インスタンス状態を持たない）
2. `MonoBehaviour` のライフサイクル（`Start` / `Update` / `OnDestroy` / コルーチン）を必要としない
3. UI 参照（`TMP_Text` など）もシーン上のオブジェクトも受け取らない
4. 呼び出し側へ戻ってこない（コールバックもイベントもない）
5. **入力と出力だけで説明が完結する**

### しきい値

- **3 デモ以上で使われていること。** 2 デモだけの重複は放置する
- 例外は同族関数のみ。`CopyClipSamples`（2C・2D）と `FloatsToPcm16`（3B・3C）は 2 デモだが、`ConvertAudioClipToWav` / `Pcm16ToClip` と同じ音声変換ファイルに置くほうが探しやすいので含める

### この基準が効く理由

ユーティリティは**葉**である。`GeminiJson.Escape(x)` は名前で用が足りるので、デモを上から下まで読み通すのに一度も飛ばなくていい。一方 `LiveSession` のような**背骨**を共有すると、読むために必ず飛ぶ必要が出る。**「1 ファイル = 1 つの流れ」という教材の形は、葉を切り出しても壊れないが、背骨を抜くと壊れる。**

---

## 2. 現状の計測

対象は各デモのメインスクリプト。`2C` の `SherpaOnnx/` 配下はベンダー提供のバインディングなので除外。デモ合計 **14,438 行**。

### 採用するメソッド（実測）

| メソッド | 使用デモ数 | 1 本 | 合計 | 共有先ファイル |
|---|---:|---:|---:|---|
| `EscapeJson` | 13 | 36 | 434 | `GeminiJson` |
| `PrettyPrintJson` | 11 | 62 | 622 | `GeminiJson` |
| `TruncateForDisplay` | 4 | 9 | 29 | `GeminiJson` |
| `TruncateBase64ForDisplay` | 3 | 32 | 94 | `GeminiJson` |
| `ExtractNestedTextAfterKey` | 4 | 16 | 51 | `GeminiJsonScan` |
| `ExtractJsonStringFieldFrom` | 4 | 54 | 165 | `GeminiJsonScan` |
| `LoadApiKey` → `TryRead` | 13 | 32 | 273 | `GeminiKey` |
| `MaskApiKey` | 9 | 14 | 126 | `GeminiKey` |
| `BuildGenerateContentUrl` | 5 | 6 | 30 | `GeminiKey` |
| `TryExtractAssistantText` | 5 | 33 | 165 | `GeminiResponse` |
| `ConvertAudioClipToWav` | 3 | 46 | 137 | `AudioCodec` |
| `TrimClip` | 3 | 24 | 72 | `AudioCodec` |
| `Pcm16ToClip` | 4 | 19 | 59 | `AudioCodec` |
| `CopyClipSamples` | 2※ | 35 | 70 | `AudioCodec` |
| `FloatsToPcm16` | 2※ | 13 | 26 | `AudioCodec` |
| `FormatHttpRequestForDisplay` | 5 | 10 | 50 | `HttpDisplay` |
| `FormatHttpResponseForDisplay` | 5 | 8 | 40 | `HttpDisplay` |
| `ScaleTexture` | 3 | 19 | 57 | `TextureUtil` |
| **合計** | | | **約 2,700** | |

※ 同族関数の例外（1 節）。

`EscapeJson` は 13 デモすべてでコメントと空白を無視して**完全一致**。`PrettyPrintJson` は 11 本、`MaskApiKey` は 9 本が完全一致。

### 見送るメソッド（しきい値未満）

| メソッド | 使用デモ | 見送る理由 |
|---|---|---|
| `ToColor` | 1B・2B | 2 デモ・計 12 行 |
| `NormalizeInlineDataKeys` | 6・7 | 2 デモ・計 18 行 |
| `TryExtractStructuredJson` | 1B | 1 デモのみ |
| `IndexOfJsonKey` / `TryExtractJsonNumber` / `ExtractBalancedObject` / `ExtractArgsObjectNear` | 3C | 3C だけ。function call 用で 3C の主題に近い |

---

## 3. 共有ライブラリの構成

```text
Assets/Common/Script/
  ChatBubble.cs          （既存・移動しない）
  GeminiJson.cs          Escape / PrettyPrint / Truncate / TruncateBase64        約 140 行
  GeminiJsonScan.cs      NestedTextAfterKey / StringFieldFrom                    約  70 行
  GeminiKey.cs           TryRead / Mask / BuildGenerateContentUrl                約  50 行
  GeminiResponse.cs      TryExtractText（JsonUtility 用 DTO を同梱）             約  50 行
  AudioCodec.cs          ClipToWav / TrimClip / CopyClipSamples /
                         FloatsToPcm16 / Pcm16ToClip                             約 140 行
  HttpDisplay.cs         FormatRequest / FormatResponse                          約  30 行
  TextureUtil.cs         Scale                                                   約  25 行
                                                                    ライブラリ計 約 570 行
```

### 決めごと

1. **すべて `static class`。** `MonoBehaviour` は 1 つも増やさない
2. **名前空間は導入しない。** 教材で `using` 行を増やさない。`Gemini` / `Audio` / `Http` の接頭辞で衝突を防ぐ
3. **asmdef は作らない。** 現状このプロジェクトに `.asmdef` は 1 つもなく、全コードが `Assembly-CSharp` にある。切ると 2C / 2D の外部パッケージ参照を明示する手間が増えるだけ
4. **シーンもインスペクタも触らない。** 公開フィールドの名前・型は一切変えない
5. **外部パッケージに依存しない。** sherpa-onnx / whisper.unity への依存は 2C / 2D の中に閉じたまま
6. **コメント規約は Common にも適用する。** [WorkshopMaterial.mdc](../.cursor/rules/WorkshopMaterial.mdc) の日本語コメント規約に従い、各ファイル冒頭に「何のための道具か・どのデモが使うか」を書く

### `LoadApiKey` は分割する

現状の `LoadApiKey` は 13 デモすべてにあり、差はログ接頭辞とエラー表示先だけ。ただし失敗時に `SetStatus` と `responseText` を触るので、そのままでは採用条件 3 を満たさない。**純粋関数とエラー表示に割る。**

```csharp
// Common: 読むだけ。UI もログも触らない
public static bool TryRead(string relativePath, out string key, out string error)

// デモ側: 表示のしかたは今までどおりデモが決める
if (!GeminiKey.TryRead(apiKeyRelativePath, out apiKey, out string error))
{
    Debug.LogError("[TextToText] " + error);
    SetStatus("エラー", false);
    responseText.text = error;
}
```

`TryExtractAssistantText` も同様に、DTO ごと `GeminiResponse` へ移して純粋関数にする。

---

## 4. 共有しないもの

意図的に各デモへ残す。**ここを動かすと教材の形が変わる。**

| 領域 | 該当 | 残す理由 |
|---|---|---|
| Live セッション | `ConnectLiveSessionCoroutine` / `ReceiveLoopAsync` / `CloseSocket` / `EnqueueMain` ほか（3B・3C・4・5） | 状態とライフサイクルを持つ背骨。読むために飛ぶ必要が出る |
| 音声再生 | `PlaybackPumpCoroutine` / `ClearPlaybackQueue` / `EnsurePlaybackAudioSource` | コルーチンと `AudioSource` を持つ |
| マイク制御 | `SetupMicrophone` / `BeginRecording` / `PumpMicrophoneChunksIfStreaming` | デバイス状態を持つ |
| Status / ログ UI | `SetStatus` / `UpdateStatusBlink` / `AddBubble` / `AppendLog` | UI 参照と点滅の状態を持つ |
| SystemInstruction 同期 | 7 メソッド（1A・2A・2C・2D・3A・3B・4） | `TMP_InputField` とファイル更新時刻の状態を持つ |
| カメラ矩形 | `EnsureBackgroundClearCamera` ほか（1B・2B・3C） | カメラの状態を持つ |
| **リクエスト組み立て** | `BuildRequestJson` / `BuildSetupJson` / `BuildSttRequestJson` / `BuildChatRequestJson` | **何を送るかが各デモの学習ポイント**。共通ビルダーに隠すとデモの意味が消える |
| レスポンスのキー解釈 | `HandleServerMessage` / `TryExtractAndEnqueueAudio` | 同上。走査の道具だけ共有し、キー名はデモに書く |
| エラー表示 / 送信ロック | `ShowError` / `SetSending` | デモごとに「どこに出すか」「何を止めるか」が違う。共有すると分岐だらけになる |

**Live 系には約 1,300 行の重複が残る。** これは承知のうえでの判断。詳細は 9 節。

---

## 5. 削減見込み

| デモ | 現状 | 削減 | 後 |
|---|---:|---:|---:|
| 1A.TextToText | 761 | -177 | 584 |
| 1B.TextToJSON | 784 | -130 | 654 |
| 2A.SpeechToText | 995 | -297 | 698 |
| 2B.SpeechToJSON | 1,038 | -262 | 776 |
| 2C.(SpeechToTextSherpa) | 919 | -221 | 698 |
| 2D.(SpeechToTextWhisper) | 993 | -221 | 772 |
| 3A.SpeechToSpeech | 1,367 | -306 | 1,061 |
| 3B.SpeechToSpeechLiveAPI | 1,569 | -240 | 1,329 |
| 3C.SpeechToMotion | 1,617 | -222 | 1,395 |
| 4.VisionToSpeech | 1,530 | -246 | 1,284 |
| 5.ScreenToSpeech | 885 | -161 | 724 |
| 6.TextToImage | 776 | -133 | 643 |
| 7.ImageToImage | 702 | -76 | 626 |
| **デモ合計** | **14,438** | **-2,692** | **11,746** |
| Common | 38 | +570 | 608 |
| **総計** | **14,476** | | **12,354（-15%）** |

3B / 3C / 4 は 1,300〜1,400 行のまま残る。**大物デモは細くならない。** それが今回のスコープの意図した限界。

---

## 6. 段階計画

1 Phase = 1 コミット。各 Phase 単独でコンパイルが通り、単独で巻き戻せる。

### Phase 0 — 規約の追記（コード変更なし）

現行の [WorkshopMaterial.mdc](../.cursor/rules/WorkshopMaterial.mdc) は「共通基盤への過度な寄せすぎは避ける（必要最小限の共有のみ）」と書いてある。**この方針自体は維持し、「必要最小限」の中身を具体化する形で追記する。**

- `WorkshopMaterial.mdc` に追記:
  - 1 節の採用条件 5 つと「3 デモ以上」のしきい値
  - 「状態とライフサイクルを持つものは共有しない」の一行
  - `Assets/Common/Script/` に置いてよいものの例と、置いてはいけないものの例
- `Docs/demo-series-overview.md`「設計の骨格」に、共有／非共有の線引きを一行追記

### Phase 1 — 純粋関数の切り出し（本体）

`GeminiJson` / `GeminiJsonScan` / `AudioCodec` / `HttpDisplay` / `TextureUtil` を新設し、13 デモから該当メソッドを削除して呼び出しに置換。

- 状態を持たない `static` メソッドのみ。シーンもインスペクタも触らない
- 削減 約 1,960 行

### Phase 2 — 分割が要る 2 つ

`GeminiKey` / `GeminiResponse` を新設。

- `LoadApiKey` を `TryRead` ＋ デモ側のエラー表示に割る（3 節）
- `TryExtractAssistantText` を DTO ごと移す。移行したデモから入れ子 `GeminiResponse` / `GeminiCandidate` / `GeminiContent` / `GeminiPart` を削除する（残すと名前が衝突する）
- 削減 約 730 行

### Phase 3 — ドキュメント追従

- Common の各ファイル冒頭に「何のための道具か・どのデモが使うか」
- 各デモ README の「主要クラス」節に、Common に出した関数がある場合のみ一行触れる
- `Docs/demo-series-overview.md` に Common の一覧を追記

---

## 7. 着手前に潰す差異

「名前は同じだが中身が違う」もの。一括置換すると挙動が変わる。**Phase 1 の前に実 diff で確認する。**

| メソッド | 差異 | 対処 |
|---|---|---|
| `LoadApiKey` | 13 本すべて別実装。差はログ接頭辞（`[TextToText]` など）とエラー表示先 | 3 節の分割で吸収。接頭辞はデモ側に残る |
| `TruncateForDisplay` | 3B・4 が同一、3A が別 | 実装を突き合わせ、機能の多い側へ統一 |
| `FormatHttpRequestForDisplay` | 2A・2B・3A 版と 2C・2D 版で分岐が違う | 同上 |
| `TryExtractAssistantText` | 5 本すべて別実装（ログ接頭辞のみの差の可能性が高い） | 実 diff で確認してから統一 |
| `Pcm16ToClip` | 3B・4・5 が完全一致、3C は未確認 | 3C を突き合わせる |

`EscapeJson`（13 本）・`PrettyPrintJson`（11 本）・`MaskApiKey`（9 本）は**完全一致を確認済み**なので、そのまま置換してよい。

---

## 8. リスクと緩和策

| リスク | 影響 | 緩和策 |
|---|---|---|
| 名前衝突（全コードが `Assembly-CSharp` 単一） | 中 | 接頭辞で回避。Phase 2 で入れ子 DTO を必ず削除する |
| 「同名だが別実装」の取りこぼし | 中 | 7 節を実 diff で埋めてから着手 |
| 学生や AI が Common を書き換えて全デモを壊す | 中 | **eject の逃げ道を規約に明記する**（下記） |
| 教材の読みやすさの低下 | 小 | 採用条件により、切り出すのは葉のみ。デモを通読するのに Common へ飛ぶ必要は生じない |
| シーン参照の破損 | **なし** | 公開フィールドを一切変えないため |

### eject の逃げ道

学生が自分のデモをデバッグしていて Common を直すと、13 デモ全部が壊れる。AI コーディングを使う場合は、AI が呼び出し連鎖を遡って大元を直しに行くため、さらに起きやすい。規約に一行入れる。

> `Assets/Common/` は変更しない。挙動を変えたくなったら、そのファイルを自分のデモの `Script/` にコピーしてクラス名を変える。

AI は書かれたルールにはよく従うので、これだけでも事故が自分のフォルダ内に閉じる。

### AI コーディング前提での補足

今回のスコープなら、共有 API は 8 ファイル・十数個の static メソッドで、名前も `GeminiJson.Escape(string)` のように推測が当たる形になる。**AI が API をハルシネーションするリスクは低い**ので、API 目録の整備は必須ではない。

ただし別件として、この repo には `CLAUDE.md` も `AGENTS.md` も `.github/copilot-instructions.md` もなく、**教材規約は `.cursor/rules/*.mdc` 経由で Cursor を使う学生にしか届いていない**。Claude Code や Codex を使う学生にはフォルダ規約もコメント規約も渡っていないので、Phase 0 のついでに `CLAUDE.md` / `AGENTS.md` を置いて `.cursor/rules` と内容を揃えておくとよい。これは本計画とは独立に価値がある。

---

## 9. 見送るもの（将来の宿題）

判断として除外するが、理由と再検討の条件を残す。

| 見送るもの | 重複量 | 再検討する条件 |
|---|---:|---|
| **Live セッション**（3B・3C・4・5 の WebSocket 一式） | 約 700 行 | Live デモがもう 1 つ増えたとき。`CloseSocket` は 3 本が完全一致で、切断漏れは学生が最も踏みやすいバグなので、優先度は高い |
| **音声再生**（3B・4・5） | 約 300 行 | 同上 |
| **マイク制御**（押し話し 5 本 / 常時 2 本） | 約 500 行 | 押し話しと常時で役割が違うため、分けて考える |
| **`MessageBubble.prefab`**（7 デモでバイト単位に同一） | — | 統合には 7 シーンの GUID 書き換えが必要。得られるのは「吹き出しの見た目を変えるとき 7 回直さずに済む」だけなので割に合わない。吹き出しのデザインを本格的に変えるときに再検討 |
| **カメラ矩形**（1B・2B・3C） | 約 380 行 | 3 デモで一致しているが、カメラ状態を持つため採用条件を満たさない |

**この計画で取れるのは重複全体の約 4 割。** 残り 6 割は意図的に残す。

---

## 10. 検証と完了条件

各 Phase 共通:

- `compile` の ErrorCount が 0
- 削除したメソッドの参照が残っていないこと（`grep` で確認）
- 変更したデモのシーンを開き、Missing 参照が出ないこと（公開フィールドを変えないので出ないはずだが、念のため）

**PlayMode 検証も `run-tests` も不要。** 切り出すのは純粋関数だけで、非同期もライフサイクルも触らないため。ただし Phase 2 の `LoadApiKey` 分割だけは失敗時の表示が変わりうるので、1 デモで**キーファイルを一時的にリネームしてエラー表示を目視確認**する。

クラウド環境で作業する場合は [UnityDev.mdc](../.cursor/rules/UnityDev.mdc) の方針どおりコード変更まで。Editor 検証を省略した旨を報告に一行書く。

全体の完了条件:

- 13 デモすべてが従来どおり動く
- `Assets/Common/Script/` の各ファイルが日本語コメント規約を満たしている
- Phase 0 の規約追記と実態が一致している

---

## 11. やらないこと

- **背骨の共有** — Live セッション / マイク / 音声再生 / UI / SystemInstruction 同期（4 節・9 節）
- **`BuildRequestJson` の抽象化** — 何を送るかは各デモの主題
- **Prefab の統合** — シーンの GUID 書き換えに見合わない
- **asmdef の導入** — 現状ゼロ。切ると 2C / 2D の依存記述が増えるだけ
- **名前空間の導入** — 教材で `using` を増やさない
- **JSON ライブラリの導入**（Newtonsoft など）— 「素朴な文字列処理で JSON を組む」ことは教材の一部
- **UI レイアウトの変更・デモの追加削除** — 構成は現状維持

---

## 実施結果

### 行数（実績）

| | 計画 | 実績 |
|---|---:|---:|
| デモ合計 | 11,746 | **11,596** |
| Common | 570 | **675**（日本語コメントを厚く書いたぶん増） |
| 総計 | 12,354 | **12,271** |
| 元の総計 | 14,476 | 14,476 |
| 削減 | -2,122 | **-2,205（-15.2%）** |

デモ別（実績）: 1A 761→569 / 1B 784→665 / 2A 995→670 / 2B 1,038→769 / 2C 919→673 /
2D 993→747 / 3A 1,367→1,030 / 3B 1,569→1,328 / 3C 1,617→1,392 / 4 1,530→1,286 /
5 885→729 / 6 776→649 / 7 702→634

### 計画から変えた点

- **`GeminiResponse` → `GeminiTextResponse` に改名。** 1B / 2B / 6 / 7 が同名の入れ子 DTO を今も使っており（それぞれ別用途で共有対象外）、Common 側と名前が重なるため。計画 8 節で挙げた「名前衝突」がそのまま出た形で、対象外のデモを触らずに済む Common 側の改名で回避した。
- **`HttpDisplay.FormatRequest` に Base64 省略長の引数を足した。** 2A / 2B / 3A は音声を送るので `"data":"..."` を省略表示するが、2C / 2D は端末で文字起こしするため省略が不要だった（7 節の差異のうち唯一、表示だけでなく処理が違ったもの）。`0` を渡すと省略しない仕様にして 1 実装にまとめた。

### 挙動が変わった箇所（意図的）

| 箇所 | 変更前 | 変更後 |
|---|---|---|
| `GeminiJson.Truncate` の末尾表記（3A のみ） | `…(123 chars total)` | `…(123 chars)`（3B / 4 の表記に統一） |
| APIキーが空のときの表示（1A / 1B / 2A / 2B / 3A） | `APIキーが空です。Docs/… を参照してください。` | 同左＋末尾にパスを括弧書きで追加 |
| APIキーファイルが無いときの表示（1A / 1B） | `APIキーファイルが見つかりません:\n{path}` | `APIキーファイルがありません: {path}`（ログと同一文言に統一） |

いずれも失敗時の文言のみで、成功時の挙動は変えていない。

### 検証

クラウド環境のため Unity Editor での `compile` は実施していない（[UnityDev.mdc](../.cursor/rules/UnityDev.mdc) の方針）。代わりに静的検査を行った。

- 旧メソッド名の残存: 0 件
- 波括弧・丸括弧・角括弧の対応: 31 ファイルすべて整合
- メソッドシグネチャ直後が `{` でない箇所: 0 件
- Common への呼び出し 137 箇所の引数個数が宣言と一致
- 変更で未使用になった `using System.IO;` を 1B / 2B / 6 / 7 から削除（3B の `System.Collections.Generic` は変更前から未使用のため据え置き）
- 各 README に書いた依存クラスと、実際のコードの参照が全 13 デモで一致
- 新規 7 ファイルの `.meta` を生成（GUID 重複なし）

**Unity Editor が使える環境で `compile` を 1 度通すこと。** 特に次を確認する。

- [ ] `compile` の ErrorCount が 0
- [ ] 13 デモのシーンを開き Missing 参照が出ない
- [ ] APIキーファイルを一時的にリネームし、1A でエラー表示が出ることを目視確認（`LoadApiKey` の分割箇所）
