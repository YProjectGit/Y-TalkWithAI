# 3C. SpeechToFunction

![speech-to-function](../Docs/Image/speech-to-function.png)

<br/>

会話の途中で、**Gemini Live API**がアプリの機能を呼び出すアプリケーションです。

3Bと同じく**WebSocket**の1セッションで声を双方向に流します。そのうえで、モデルが `set_cube_motion` を呼び、キューブの回転と大きさが変わります。会話がそのまま、アプリの操作になります。

学習のため、接続時のSetupと、送受信される `toolCall` / `toolResponse` が見えるようになっています。

<br/>

---

## 学ぶこと

<br/>

- ### Function calling

  呼んでほしい関数をあらかじめ宣言し、モデルからの呼び出しを受け取る方法を学びます。

- ### toolCall / toolResponse

  モデルからの呼び出しを受け取り、実行結果を同じセッションに返して会話を続けさせる流れを学びます。

<br/>

---

## 動かしてみる

<br/>

Project ウィンドウで `Assets/3C.SpeechToFunction/SpeechToFunction.unity` を開き、Playしてください。

### 1. 接続を確認する

1. Status が「接続済み」になること、左のキューブがゆっくり回っていることを確認してください。
2. 中央上の **1. Setup** に、`systemInstruction`（事前指示）と `functionDeclarations`（`set_cube_motion`）が出ていることを見てください。
3. 左の **角速度 / サイズ** 欄で、いまの値と目標値が同じ付近にあることを確認してください（起動時は Y だけ回っています）。

### 2. Spaceを押して速さと大きさを変える

1. **Spaceキーを押したまま**、たとえば「もっと速く回して、大きくして」と話し、話し終えたらキーを**離して**ください。左の横棒が声に合わせて伸びます。
2. 右上 **2. toolCall** に `set_cube_motion` と引数（`angularVelocityY` / `size` など）が出ることを見てください。
3. 中央下 **3. 送信** に `toolResponse` が増えること、キューブが目標へなめらかに寄っていくことを確認してください。
4. 返答が声で再生され、右下 **4. transcription** に文字起こしが出ることを確認してください。

### 3. 逆向きと停止を試す

1. 「逆に回して」と話し、ωY の符号が反転すること、自転が反対向きへ寄っていくことを見てください。
2. 「前に倒して」「横に傾けて」と話し、ωX / ωZ の目標が変わることを左の数値欄で見てください。
3. 「止めて」と話し、ωX / ωY / ωZ の目標が `0` になり、回転がゆっくり止まることを確認してください。
4. 「小さくして」**とだけ**言ったとき、sizeX / sizeY / sizeZ が同じように変わり、角速度は維持されることを見てください。
5. 「縦に長くして」と話し、sizeY だけが変わることを左の数値欄で見てください。

声を変えたいときは、Playを止めてInspectorの **Voice Name**（`voiceName`）を変更し、もう一度Playしてください。声は接続時のSetupで一度だけ送るため、Play中の変更は次の接続まで反映されません。

<br/>

---

## 解説

<br/>

### Function calling

<br/>

**Function calling** とは、モデルが自由文だけで答えるのではなく、**あらかじめ宣言した関数を「呼んでほしい」と依頼する**仕組みです。

1B / 2B の構造化出力は、JSONが**答えそのもの**でした。こちらは JSON が**動作の依頼**で、クライアントが実行し、結果を会話に戻します。このデモでは Setup の `tools.functionDeclarations` に `set_cube_motion` の形を載せ、`systemInstruction` で「いつこの関数を使うか」を伝えます。声の指示はその引数になります。

宣言している関数は `set_cube_motion` だけです。キューブの回転（ワールド XYZ の角速度）と大きさ（ローカル XYZ の倍率）を変えます。書いていない引数は現状維持です。toolCall が決めるのは**目標**で、画面の値は毎フレーム目標へ寄っていくので、急に跳ねません。

<br/>

**接続時に送るSetupのJSON（抜粋）**

```json
{
  "setup": {
    "model": "models/gemini-3.1-flash-live-preview",
    "systemInstruction": {
      "parts": [
        {
          "text": 
        "あなたは Unityアプリ内のキューブを set_cube_motion 関数で操作するアシスタントです。
		回転・傾き・大きさを変える依頼には、必ず set_cube_motion を呼んでください。
		呼び出しは1回にまとめ、変える引数だけを書きます。
		書かなかった引数はそのまま残ります。
		値は、直近の tool response に入っている「いまの目標」を基準に決めます。
		「もっと速く」「少し小さく」のような相対的な依頼は、その値を増減させてください。
		「逆に」は角速度の符号を反転します（軸の指定がなければ Y）。
		「止めて」は角速度を 3 軸とも 0 にします。
		速さの目安は、ゆっくり 10、ふつう 30、速い 120、とても速い 300（度/秒）です。
		大きさの目安は、小さい 0.5、ふつう 1、大きい 2 です。
		極端な値は避けてください。
		関数を呼んだあとは、何をどう変えたかを日本語で一言だけ伝えてください。
		キューブの操作と関係のない話には、関数を呼ばずに短く答えてください。"
        }
      ]
    },
      
    "tools": [
      {
        "functionDeclarations": [
          {
            "name": "set_cube_motion",
            "description": "Set cube motion. ...",
            "parameters": {
              "type": "OBJECT",
              "properties": {
                "angularVelocityX": { "type": "NUMBER" },
                "angularVelocityY": { "type": "NUMBER" },
                "angularVelocityZ": { "type": "NUMBER" },
                "sizeX": { "type": "NUMBER" },
                "sizeY": { "type": "NUMBER" },
                "sizeZ": { "type": "NUMBER" },
                "size": { "type": "NUMBER" }
              }
            }
          }
        ]
      }
    ],
      
    "generationConfig": {
      "responseModalities": ["AUDIO"]
    }
  }
}
```

1. **`systemInstruction`**  
   いつ・どう関数を呼ぶかの事前指示です。本文は [`SystemInstruction.txt`](Resource/SystemInstruction.txt) で、中央の **1. Setup** にも出ます。
2. **`tools.functionDeclarations`**  
   モデルが呼んでよい関数の名前・説明・引数です。説明文は英語で送ります。
3. **`set_cube_motion` の引数**  
   `angularVelocityX/Y/Z` は符号付きの角速度（度/秒。符号が向き、絶対値が速さ）。`sizeX/Y/Z` は軸ごとの倍率（`1` が初期）。`size` は3軸を同じ値にします。
4. **`responseModalities`**  
   返答を音声にする指定です。3Bと同じ `AUDIO` です。

<br/>

### toolCall / toolResponse

<br/>

- Function calling は「宣言しておく」だけでは動きません。実際のやり取りは、**サーバ (Gemini)から届く `toolCall`** と、**クライアント(Unity)が返す `toolResponse`** の2通のメッセージで進みます。Live API では、この2つが音声とは別のメッセージとして流れます。

- **`toolCall`** は、Geminiモデルからの「この関数を、この引数で実行してほしい」という依頼です。モデル自身が関数を実行するわけではなく、実行するのはクライアント（Unity）です。

- **`toolResponse`** は、上記のtoolCallを受けて、Unity側で実行した結果を**同じセッションへ返す**メッセージです。toolCallのidと同じidを含めることで、会話の一貫性を保ちます。

- 画面では、右上 **2. toolCall** の直後に、中央下 **3. 送信** 内に**toolResponse**が出て、そのあと声と文字起こし( **4. transcription**) が来る、という順で見えます。

  
  
  ![speech-to-function-toolcall](../Docs/Image/speech-to-function-toolcall.png)

<br/>

**受信する toolCall の例**

```json
{
  "toolCall": {
    "functionCalls": [
      {
        "id": "...",
        "name": "set_cube_motion",
        "args": {
          "angularVelocityY": 40,
          "size": 1.4
        }
      }
    ]
  }
}
```

1. **`name`**  
   呼んでほしい関数です。このデモでは `set_cube_motion` だけを実行します。
2. **`args`**  
   引数です。変更する引数だけを挙げています。
3. **`id`**  
   この呼び出しの識別子です。`toolResponse` に同じ `id` を付けて返します。

<br/>

**送信する toolResponse のJSON**

```json
{
  "toolResponse": {
    "functionResponses": [
      {
        "id": "...",
        "name": "set_cube_motion",
        "response": {
          "result": "ok",
          "angularVelocityY": 40,
          "sizeX": 1.4,
          "sizeY": 1.4,
          "sizeZ": 1.4
        }
      }
    ]
  }
}
```

1. **`toolResponse`**  
   関数を実行したあとに、同じセッションへ返す結果です。

2. **`id`**  
   識別子です。元となるtoolCallのidと同一にすることで対応関係を名確認します。

3. **`response`**  
   実行結果です。`result` に加えて、現在の各変数の状態を返します。

   

<br/>

---

## コードの解説

<br/>

### SpeechToFunction（[`SpeechToFunction.cs`](Script/SpeechToFunction.cs)）

<br/>

デモの本体です。上から、接続から関数実行、再生までの流れを追うとわかりやすいです。

通信は **ClientWebSocket**（双方向のソケット）です。受信はバックグラウンドのループで行い、UI・再生・キューブ更新だけメインスレッドのキュー経由で戻します。Spaceキーの押し離しの検知は `Update` の中で、**旧 Input Manager**（`Input.GetKeyDown` / `GetKeyUp`）を使っています。

<br/>

1. **Liveセッションに接続する**  
   `ConnectLiveSessionCoroutine` — WSS接続 → `BuildSetupJson` でSetup（`systemInstruction` と `tools` と AUDIO）を送信 → `setupComplete` を待つ
   <br/>
2. **Spaceキーの押し離しを見る**  
   `UpdatePushToTalk` — 押した瞬間に `activityStart`、離した瞬間に `activityEnd`
   <br/>
3. **PCMチャンクを送る**  
   `PumpMicrophoneChunksIfStreaming` — マイクの差分 → 16-bit PCM → Base64 → `realtimeInput.audio`
   <br/>
4. **サーバメッセージを振り分ける**  
   `HandleServerMessage` — `toolCall` / 音声 / transcription
   <br/>
5. **関数を実行して結果を返す**  
   `HandleToolCallOnMain` → `ApplyMotionArgs` → `toolResponse`。引数に無い項目は現状維持
   <br/>
6. **目標へ漸近させる**  
   `StepMotion` — 現在値を目標へ寄せ、`Rotate` と `localScale` に書く
   <br/>
7. **受信PCMを再生する**  
   `PlaybackPumpCoroutine` — キューから `AudioClip` 化して `AudioSource.Play`

<br/>

### 共通ライブラリ（`Assets/Common/Script/`）

<br/>

このデモが使っている共通のライブラリです。シンプルなユーティリティクラスなので、**上の流れを追うときに中身を読む必要はありません。**

| ファイル | 中身 |
|---|---|
| [`GeminiJson`](../Common/Script/GeminiJson.cs) | JSONのエスケープ・整形・省略表示 |
| [`GeminiJsonScan`](../Common/Script/GeminiJsonScan.cs) | Live APIの受信JSONからキーを頼りに文字列を拾う |
| [`GeminiKey`](../Common/Script/GeminiKey.cs) | APIキーの読込・マスク |
| [`AudioCodec`](../Common/Script/AudioCodec.cs) | AudioClip ⇄ WAV / PCM16 の変換 |
| [`MicLevel`](../Common/Script/MicLevel.cs) | マイクの直近の音の大きさを0〜1にする |
| [`ResponseTime`](../Common/Script/ResponseTime.cs) | 送信から返信までの経過時間をConsoleへ表示 |

これらは他のデモも使っています。挙動を変えたくなったらCommonを直さず、そのファイルをこのデモの `Script/` にコピーしてクラス名を変えてください。
