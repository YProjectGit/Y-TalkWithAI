# 3C.SpeechToMotion 実装プラン

## 要点（サマリー）

- **何をするか**: 3B と同じ Live API セッションで声をやり取りし、`set_cube_motion` の function call でキューブの **符号付き角速度** と **サイズ** を変える。値は目標へ lerp（指数減衰）で漸近する。
- **学習の山場**: 1B/2B の「JSON が答え」ではなく、**toolCall ↔ toolResponse の往復**。向きと速さは別引数にせず、符号付き ω に一元化する。
- **入力**: Space 押し話し（3B の手動モードと同じ。VAD 自動は置かない）。
- **出力**: ネイティブ音声 ＋ transcription ＋ 左の 3D キューブ。
- **コピー派生**: 共通基底は作らない。シーンは 2B（キューブ＋4欄）、通信は 3B を短く移植。
- **触らないもの**: 1A〜3B / 4 以降の挙動。色の変更。Google Search。非同期 function call。
- **完了条件**: シーン・スクリプト・README・overview。クラウドのため Editor 検証は省略し、UI 構成イメージを出す。
- **一言**: 3B が「声のセッション」なら、3C は「その途中で Unity を動かす」。

| | 2B.SpeechToJSON | 3B.SpeechToSpeechLiveAPI | 3C.SpeechToMotion（本プラン） |
|---|-----------------|-------------------------|------------------------------|
| 通信 | REST ×2 | Live ×1 | **Live ×1** |
| モデル依頼 | 構造化 JSON（答え） | なし（会話のみ） | **function call（動作）** |
| 左 | 色が変わるキューブ | 吹き出し | **ω / size が漸近するキューブ** |
| 可視化 | 発生順 1〜4 | 送信 / 受信 | **Setup / toolCall / toolResponse / transcription** |

---

## 学習上の位置づけ

```text
[1B/2B] Text/Mic ──► LLM (JSON) ──► 色        … 答えの形
[3B]    Mic ══════► Live ═══════► Audio      … セッション
[3C]    Mic ══════► Live + tool ═► Audio + 運動
```

- 3B の直後。4 の映像より前に「Live の次の能力」として置く。
- 関数は 1 個。`angularVelocity`（度/秒、符号付き）と `size`（倍率、省略可）。

---

## 処理の骨格

```text
Play
  → APIキー / SystemInstruction / マイク / 3D
  → Live 接続（Setup: AUDIO + tools.set_cube_motion）
Space 押下〜解放
  → PCM realtimeInput（activityStart / End）
受信 toolCall
  → 目標 ω / size を更新（無いキーは維持、クランプする）
  → toolResponse（result + いまの目標）
  → 毎フレーム lerp で現在値を寄せて Rotate / localScale
  → モデルが声で確認 + transcription
```

---

## UI 構成

2B の 3 分割と 4 欄を、握手が見える題名に差し替える。

- 左: 3D キューブ、角速度 / サイズ（現在 → 目標）、Space、Status
- 1. Setup: functionDeclarations
- 2. toolCall: name / args
- 3. 送信: PCM / toolResponse
- 4. transcription: in / out

---

## 判断の固定

- 関数は `set_cube_motion` のみ。色は扱わない
- 向きと速さは符号付き角速度 1 つ
- 部分更新（省略したキーは維持）
- 現在値は目標へ `1 - exp(-k*dt)` で漸近
- VAD 自動・Search・非同期 function call は入れない
- 共通基底は作らない

実装済み（クラウドのため Editor 検証は省略）。UI 構成イメージ → [3c-speech-to-motion-ui.png](3c-speech-to-motion-ui.png)
