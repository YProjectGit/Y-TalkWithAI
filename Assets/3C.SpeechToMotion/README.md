# 3C.SpeechToMotion

会話の途中で、Gemini Live API がアプリの機能を呼び出します。会話がそのまま、アプリの操作になります。

シリーズ全体の位置づけ → [Docs/demo-series-overview.md](../../Docs/demo-series-overview.md)

---

## このデモで学べること

- **Function calling**  
  呼んでほしい関数を宣言し、モデルからの呼び出しを受け取る
- **Tool response**  
  実行結果をセッションに返し、会話を続けさせる

---

## 事前準備

1. Google AI Studio から Gemini の API にアクセスするための APIキーを取得し、`Assets/Common/APIKey.txt` に保管してください。  
   手順 → [Docs/gemini-ai-studio-setup.md](../../Docs/gemini-ai-studio-setup.md)
2. PC にマイクがつながり、Unity から使える状態にしてください（OS のマイク権限を含む）。
3. スピーカーまたはヘッドホンで再生音が聞こえる状態にしてください。

---

## 動かし方

Project ウィンドウで `Assets/3C.SpeechToMotion/SpeechToMotion.unity` を開き、Play を押してください。

### 1. 接続と初期の回転を見る

1. Status が「接続済み」になること、左のキューブがゆっくり回っていることを見てください。
2. 中央上の **1. Setup** に `systemInstruction`（事前指示の本文）と `functionDeclarations`（`set_cube_motion`）が出ていることを確認してください。
3. 左の **角速度 / サイズ** 欄で、いまの値（ω）と目標値が同じ付近にあることを見てください。

### 2. Space で速さと大きさを変える

1. **Space を押したまま**、たとえば「もっと速く回して、大きくして」と話し、**離してください**。
2. 右上 **2. toolCall** に `set_cube_motion` と引数（`angularVelocity` / `size`）が出ることを見てください。
3. 中央下 **3. 送信** に `toolResponse` が増えること、キューブが目標へなめらかに寄っていくことを確認してください。
4. 返答が声で再生され、右下 **4. transcription** に文字起こしが出ることを聞いて／見てください。

### 3. 逆向きと停止を試す

1. 「逆に回して」と話し、ω の符号が反転すること、回転が反対向きへ漸近することを見てください。
2. 「止めて」と話し、ω の目標が `0` になり、回転がゆっくり止まることを確認してください。
3. 「小さくして」**とだけ**言ったとき、サイズは変わり角速度は維持されることを、左の数値欄で見比べてください。

---

## Function calling

Function calling とは、モデルが自由文だけで答えるのではなく、**あらかじめ宣言した関数を「呼んでほしい」と依頼する**仕組みです。

1B / 2B の構造化出力は、JSON が**答えそのもの**でした。こちらは JSON が**動作の依頼**で、クライアントが実行し、結果を会話に戻します。このデモでは Setup の `systemInstruction` で「いつこの関数を使うか」を伝え、`tools.functionDeclarations` に `set_cube_motion` の形を載せます。声の指示はその引数になります。

試し方: 1. Setup の事前指示と関数宣言を読む。発話のあと、宣言と 2. toolCall の `name` / 引数が対応しているかを見比べる。

---

## Tool response

Tool response とは、モデルから来た `toolCall` を実行したあと、**同じ Live セッションに結果を返す**メッセージです。

いま使っている Live モデルは同期の function call なので、`toolResponse` を返すまで次の発話を始めません。返した内容（このデモでは `result` と、いまの目標 ω / size）を踏まえて、モデルが声で「速くしたよ」などと続けます。

試し方: 2. toolCall の直後に 3. 送信へ `toolResponse` が出て、そのあと声と 4. transcription が来る、という順を追う。

---

## 符号付き角速度と漸近

角速度とは、どれだけ速く回るかです。このデモでは **符号が向き、絶対値が速さ** です。正は起動時と同じ向き（Y 軸プラスまわり）、負は逆向き、`0` は停止です。サイズは回転とは別で、`1` が初期の大きさです。

toolCall が決めるのは**目標**です。画面の値は毎フレーム、目標へ lerp（指数減衰）で寄っていくので、急に跳ねずなめらかに変わります。左欄の `現在 → 目標` が、その途中経過です。

試し方: 「速くして」のあと、ω の左側（現在）が右側（目標）に追いつく様子を見る。

---

## 主要クラス

### SpeechToMotion（[`SpeechToMotion.cs`](Script/SpeechToMotion.cs)）

デモの本体です。上から、接続〜発話〜 toolCall 実行〜漸近の順に追うとわかりやすいです。

通信は **ClientWebSocket**（双方向のソケット）です。受信はバックグラウンドのループで行い、UI・再生・キューブ更新だけメインスレッドのキュー経由で戻します。Space の押し話し検知は `Update` と **旧 Input Manager**（`Input.GetKeyDown` / `GetKeyUp`）です。

1. **Live セッションに接続する**  
   `ConnectLiveSessionCoroutine` — WSS（WebSocket Secure）接続 → Setup（`systemInstruction` と `tools` と AUDIO）→ `setupComplete` 待ち。1. Setup 欄は `RefreshSetupPanel` で事前指示の本文と関数宣言を出す
2. **Space 押し話しを検知する**  
   `UpdatePushToTalk` — 押しているあいだ PCM 送信、離したら `activityEnd`
3. **PCM チャンクを送る**  
   `PumpMicrophoneChunksIfStreaming` — マイク差分 → 16-bit PCM → Base64 → `realtimeInput.audio`
4. **サーバメッセージを振り分ける**  
   `HandleServerMessage` — `toolCall` / 音声 / transcription
5. **関数を実行して結果を返す**  
   `HandleToolCallOnMain` → `ApplyMotionArgs` → `toolResponse`  
   引数に無い項目は現状維持。角速度とサイズは上下限で丸める（クランプ）
6. **目標へ漸近させる**  
   `StepMotion` — `1 - exp(-k*dt)` の lerp で現在値を寄せ、`Rotate` と `localScale` に書く
7. **受信 PCM を再生する**  
   `PlaybackPumpCoroutine` — キューから `AudioClip` 化して `AudioSource.Play`
