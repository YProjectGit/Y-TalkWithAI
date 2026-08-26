# 3C.SpeechToFunction

![speech-to-function](../Docs/Image/speech-to-function.png)



会話の途中で、Gemini Live API がアプリの機能を呼び出します。会話がそのまま、アプリの操作になります。

シリーズ全体の位置づけ → [Assets/Docs/demo-series-overview.md](../Docs/demo-series-overview.md)

---

## このデモで学べること

- **Function calling**  
  呼んでほしい関数を宣言し、モデルからの呼び出しを受け取る
- **Tool response**  
  実行結果をセッションに返し、会話を続けさせる

---

## 事前準備

1. Google AI Studio から Gemini の API にアクセスするための APIキーを取得し、`Assets/Common/APIKey.txt` に保管してください。  
   手順 → [Assets/Docs/gemini-ai-studio-setup.md](../Docs/gemini-ai-studio-setup.md)
2. PC にマイクがつながり、Unity から使える状態にしてください（OS のマイク権限を含む）。
3. スピーカーまたはヘッドホンで再生音が聞こえる状態にしてください。

---

## 動かし方

Project ウィンドウで `Assets/3C.SpeechToFunction/SpeechToFunction.unity` を開き、Play を押してください。

### 1. 接続と初期の回転を見る

1. Status が「接続済み」になること、左のキューブがゆっくり回っていることを見てください。
2. 中央上の **1. Setup** に `systemInstruction`（事前指示の本文）と `functionDeclarations`（`set_cube_motion`）が出ていることを確認してください。
3. 左の **角速度 / サイズ** 欄で、いまの値（ωY）と目標値が同じ付近にあることを見てください（起動時は Y だけ回っています）。

### 2. Space で速さと大きさを変える

1. **Space を押したまま**、たとえば「もっと速く回して、大きくして」と話し、**離してください**。左の横棒が声に合わせて伸びます。
2. 右上 **2. toolCall** に `set_cube_motion` と引数（`angularVelocityY` / `size` / `sizeY` など）が出ることを見てください。
3. 中央下 **3. 送信** に `toolResponse` が増えること、キューブが目標へなめらかに寄っていくことを確認してください。
4. 返答が声で再生され、右下 **4. transcription** に文字起こしが出ることを聞いて／見てください。

### 3. 逆向きと停止を試す

1. 「逆に回して」と話し、ωY の符号が反転すること、自転が反対向きへ漸近することを見てください。
2. 「前に倒して」「横に傾けて」と話し、ωX / ωZ の目標が変わることを左の数値欄で見てください。
3. 「止めて」と話し、ωX / ωY / ωZ の目標が `0` になり、回転がゆっくり止まることを確認してください。
4. 「小さくして」**とだけ**言ったとき、sizeX / sizeY / sizeZ が同じように変わり、角速度は維持されることを見てください。
5. 「縦に長くして」と話し、sizeY だけが変わることを左の数値欄で見てください。

---

## Function calling

Function calling とは、モデルが自由文だけで答えるのではなく、**あらかじめ宣言した関数を「呼んでほしい」と依頼する**仕組みです。

1B / 2B の構造化出力は、JSON が**答えそのもの**でした。こちらは JSON が**動作の依頼**で、クライアントが実行し、結果を会話に戻します。このデモでは Setup の `systemInstruction` で「いつこの関数を使うか」を伝え、`tools.functionDeclarations` に `set_cube_motion` の形を載せます。声の指示はその引数になります。

宣言は [`SpeechToFunction.cs`](Script/SpeechToFunction.cs) の `FunctionDeclarationJson` です。説明文は英語で送り、中央の **1. Setup** に同じものが出ます。

### `set_cube_motion`

キューブの回転と大きさを変える関数です。このデモが宣言している関数はこれだけです。

関数の説明（`description`）:

> キューブの運動を設定する。angularVelocityX/Y/Z はワールド軸まわりの符号付き角速度（度/秒。Y=自転、X=前後に倒す、Z=左右に傾ける）。sizeX/Y/Z は軸ごとの倍率（1=初期）。size は3軸まとめて同じ値。無いキーは現状維持。

| 引数 | 型 | 説明 |
|---|---|---|
| `angularVelocityX` | NUMBER | ワールド X まわりの角速度（度/秒）。前後に倒す |
| `angularVelocityY` | NUMBER | ワールド Y まわりの角速度（度/秒）。水平の自転。正は起動時と同じ向き |
| `angularVelocityZ` | NUMBER | ワールド Z まわりの角速度（度/秒）。左右に傾ける |
| `sizeX` | NUMBER | ローカル X のサイズ倍率（横）。1 が初期の大きさ |
| `sizeY` | NUMBER | ローカル Y のサイズ倍率（高さ）。1 が初期の大きさ |
| `sizeZ` | NUMBER | ローカル Z のサイズ倍率（奥行き）。1 が初期の大きさ |
| `size` | NUMBER | sizeX / sizeY / sizeZ を同じ倍率にする |

引数はすべて任意です。無いキーは現状維持です。

### systemInstruction

いつ・どう呼ぶかの補足は [`SystemInstruction.txt`](Resource/SystemInstruction.txt) です。本文は日本語で、中央の **1. Setup** にも出ます。要点は次のとおりです。

- `set_cube_motion` だけでキューブを操作する。ほかの関数は作らない
- 自転・傾き・大きさを変えるときにこの関数を呼ぶ
- 角速度はワールド XYZ（Y=自転、X=前後に倒す、Z=左右に傾ける）。正の Y は起動時と同じ向き
- サイズはローカル XYZ の倍率（X=横、Y=高さ、Z=奥行き）。`size` は 3 軸まとめて同じ値
- 書いていない引数は変えない
- 「逆に」だけなら Y を反転。軸を指定した逆向きはその軸の符号を反転（いまの目標は直近の tool response を見る）
- 「止めて」は角速度 3 軸を 0
- 関数の結果のあとは、日本語で短く確認する

試し方: 1. Setup の事前指示と関数宣言を読む。発話のあと、宣言と 2. toolCall の `name` / 引数が対応しているかを見比べる。

---

## Tool response

Tool response とは、モデルから来た `toolCall` を実行したあと、**同じ Live セッションに結果を返す**メッセージです。

いま使っている Live モデルは同期の function call なので、`toolResponse` を返すまで次の発話を始めません。返した内容（このデモでは `result` と、いまの目標 ωXYZ / sizeXYZ）を踏まえて、モデルが声で「速くしたよ」などと続けます。

試し方: 2. toolCall の直後に 3. 送信へ `toolResponse` が出て、そのあと声と 4. transcription が来る、という順を追う。

---

## 符号付き角速度と漸近

角速度とは、どれだけ速く回るかです。このデモではワールド XYZ の 3 軸で、**符号が向き、絶対値が速さ** です。Y は水平の自転（正が起動時と同じ Y+）、X は前後に倒す、Z は左右に傾ける、`0` はその軸の停止です。サイズもローカル XYZ の倍率で、X は横、Y は高さ、Z は奥行き、`1` が初期の大きさです。「大きくして」は 3 軸まとめて、`size` で同じ値を書きます。

toolCall が決めるのは**目標**です。画面の値は毎フレーム、目標へ lerp（指数減衰）で寄っていくので、急に跳ねずなめらかに変わります。左欄の `現在 → 目標` が、その途中経過です。

試し方: 「速くして」のあと、ωY の左側（現在）が右側（目標）に追いつく様子を見る。「前に倒して」では ωX、「縦に長くして」では sizeY が動く。

---

## 主要クラス

### SpeechToFunction（[`SpeechToFunction.cs`](Script/SpeechToFunction.cs)）

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
   引数に無い項目は現状維持
6. **目標へ漸近させる**  
   `StepMotion` — `1 - exp(-k*dt)` の lerp で現在値を寄せ、`Rotate` と `localScale` に書く
7. **受信 PCM を再生する**  
   `PlaybackPumpCoroutine` — キューから `AudioClip` 化して `AudioSource.Play`

### 共通スクリプト（`Assets/Common/Script/`）

このデモが使っている共通の道具です。どれも入力と出力だけの小さな関数なので、**上の流れを追うときに中身を読む必要はありません。**

| ファイル | 中身 |
|---|---|
| [`GeminiJson`](../Common/Script/GeminiJson.cs) | JSON のエスケープ・整形・省略表示 |
| [`GeminiJsonScan`](../Common/Script/GeminiJsonScan.cs) | Live API の受信 JSON からキーを頼りに文字列を拾う |
| [`GeminiKey`](../Common/Script/GeminiKey.cs) | APIキーの読込・マスク・generateContent の URL |
| [`AudioCodec`](../Common/Script/AudioCodec.cs) | AudioClip ⇄ WAV / PCM16 の変換 |
| [`MicLevel`](../Common/Script/MicLevel.cs) | マイク直近窓の RMS → 横棒の 0〜1 |

これらは他のデモも使っています。挙動を変えたくなったら Common を直さず、そのファイルをこのデモの `Script/` にコピーしてクラス名を変えてください。
