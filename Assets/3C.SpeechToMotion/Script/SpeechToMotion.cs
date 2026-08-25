// SpeechToMotion.cs
// 3B の Live API（WebSocket・音声↔音声）に、function calling でキューブの運動を載せるデモ。
// 声の指示 → toolCall（set_cube_motion）→ 目標の角速度 / サイズを更新 → toolResponse → 声で確認。
// 画面上の値は目標へ lerp（指数減衰）で漸近する。向きと速さは符号付き角速度 1 つ。
//
// 上からの流れ:
//   Start → APIキー・systemInstruction・マイク・AudioSource・3D プレビュー・Live 接続（Setup に tools）
//   Space 押下 → activityStart → PCM を realtimeInput で送信
//   Space 解放 → activityEnd
//   受信 → toolCall なら実行して toolResponse / 音声は再生 / transcription は右下

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Live API の function calling で、声からキューブの角速度とサイズを更新する。
/// </summary>
public class SpeechToMotion : MonoBehaviour
{
    // ===== インスペクタ: 設定 =====

    public string modelName = "gemini-3.1-flash-live-preview"; // Live モデル名（Setup の models/ 以下）
    public string apiKeyRelativePath = "Common/APIKey.txt"; // Assets/ からの相対パス
    public string systemInstructionRelativePath = "3C.SpeechToMotion/Resource/SystemInstruction.txt"; // このデモ用の事前指示
    public int sampleRate = 16000; // 送信 PCM のサンプルレート（Hz）
    public int maxRecordingSeconds = 30; // Space 押し話しの上限秒数
    public float minRecordingSeconds = 0.3f; // これより短い発話は送らない
    public string voiceName = "Kore"; // Setup の prebuilt 声色名
    public int playbackSampleRate = 24000; // 受信 PCM の想定サンプルレート（Hz）

    // ===== インスペクタ: 3D 反映先（2B と同型） =====

    public Renderer cubeRenderer; // 回す・拡大縮小するキューブ
    public Camera targetCamera; // Preview 用カメラ（未設定なら Camera.main）
    public RectTransform previewArea; // キューブ描画エリア
    public float initialAngularVelocity = 18f; // 起動時の角速度（度/秒）。正が Y+
    public float initialSize = 1f; // 起動時のサイズ倍率（1 = シーン上の初期スケール）
    public float motionLerpSpeed = 4f; // 目標への追従の速さ（大きいほど速い。指数減衰の係数）
    public float maxAbsAngularVelocity = 180f; // 角速度の絶対値上限（度/秒）
    public float minSize = 0.3f; // サイズ倍率の下限
    public float maxSize = 3f; // サイズ倍率の上限

    // ===== インスペクタ: 左ペイン =====

    public TMP_Text recordHintText; // Space 押し話しの案内
    public Image levelMeterFill; // マイク音量の横棒（Image Type = Filled）
    public TMP_Text motionStateText; // 現在値 → 目標値（角速度 / サイズ）
    public TMP_Text statusText; // 接続・録音・返答待ちなど

    // ===== インスペクタ: 中央／右（発生順 1〜4） =====

    public TMP_Text setupHeaderText; // 1. Setup（systemInstruction 本文 + functionDeclarations）
    public TMP_Text outboundLogText; // 3. 送信（PCM / toolResponse）
    public TMP_Text inboundLogText; // 2. toolCall（受信）
    public TMP_Text transcriptionText; // 4. transcription

    // ===== 内部状態: Live =====

    string apiKey; // APIキー（画面・ログに全文は出さない）
    string systemInstruction; // Setup に載せる事前指示
    ClientWebSocket socket; // Live セッション用 WebSocket
    CancellationTokenSource receiveCts; // 受信ループ停止用
    readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>(); // 受信スレッド→メイン
    readonly ConcurrentQueue<byte[]> playbackPcmQueue = new ConcurrentQueue<byte[]>(); // 再生待ち PCM
    readonly StringBuilder outboundLog = new StringBuilder(); // 送信ログ本文
    AudioSource playbackAudioSource; // 受信音声の再生先

    bool setupComplete; // setupComplete 受信済みか
    bool isConnected; // ソケットが Open か
    bool isMicStreaming; // マイクから PCM を送り続けているか
    string microphoneDevice; // マイク名
    AudioClip micClip; // ループ録音用クリップ
    float displayedLevel; // 横棒の現在値
    readonly float[] meterSamples = new float[MicLevel.WindowSamples]; // 直近サンプルの読み出し先
    int lastMicSamplePos; // 前回送ったサンプル位置
    float recordingStartedTime; // 手動録音の開始時刻
    long outboundTotalBytes; // 送信累計バイト
    string inputTranscriptBuffer; // 入力 transcription の蓄積
    string outputTranscriptBuffer; // 出力 transcription の蓄積
    bool playbackCoroutineRunning; // 再生コルーチン稼働中か
    bool statusBlink; // Status 点滅中か
    float replyWaitStarted = -1f; // 送信完了（activityEnd）時刻。未計測は -1

    // ===== 内部状態: 運動（目標へ漸近） =====

    float currentAngularVelocity; // いまの角速度（度/秒）。Rotate に使う
    float targetAngularVelocity; // toolCall で受け取った目標角速度
    float currentSize; // いまのサイズ倍率
    float targetSize; // toolCall で受け取った目標サイズ
    float baseScale; // シーン上の初期 localScale.x（size=1 のときこれ）
    Camera backgroundClearCamera; // Preview 以外を背景色で塗る
    bool cameraViewportOwned; // Camera.rect を上書き中か

    const int MicClipSeconds = 2; // マイクのリングバッファ長さ（秒）
    const int MaxLogChars = 8000; // ログ欄の上限
    const float StatusBlinkSpeed = 6f; // 点滅の速さ
    const string FunctionName = "set_cube_motion"; // Setup と実行で使う関数名
    const string WsEndpoint =
        "wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent";

    // ファイルが無いときの事前指示（Resource の txt を優先）
    const string DefaultSystemInstruction =
        "You control a Unity cube through the set_cube_motion function. "
        + "When the user asks to change spin or size, call that function. "
        + "angularVelocity is signed degrees per second: positive keeps the initial Y+ direction, "
        + "negative reverses, 0 stops. size is a uniform scale multiplier (1 = initial size). "
        + "Omit a field to leave it unchanged. To reverse, send the negated current angularVelocity "
        + "(the latest tool response includes current targets). "
        + "After the tool result, confirm briefly in Japanese. Do not invent other functions.";

    // Setup に載せる関数宣言（中央ペインにも同じものを出す）
    const string FunctionDeclarationJson =
        "{"
        + "\"name\":\"set_cube_motion\","
        + "\"description\":\"Set cube motion. angularVelocity is signed deg/s (positive=Y+, negative=opposite, 0=stop). size is uniform scale (1=initial). Omit a field to keep it.\","
        + "\"parameters\":{"
        + "\"type\":\"OBJECT\","
        + "\"properties\":{"
        + "\"angularVelocity\":{\"type\":\"NUMBER\",\"description\":\"Signed angular velocity in degrees per second.\"},"
        + "\"size\":{\"type\":\"NUMBER\",\"description\":\"Uniform scale multiplier. 1 is the initial size.\"}"
        + "}"
        + "}"
        + "}";

    // ----- エントリポイント -----

    // 起動時: キー・指示・マイク・3D・Live 接続を順に用意する
    void Start()
    {
        LoadApiKey();
        LoadSystemInstruction();
        SetupMicrophone();
        EnsurePlaybackAudioSource();
        InitMotionFromScene();
        InitPanelTexts();

        if (recordHintText != null)
        {
            recordHintText.text = "Space を押しているあいだ話す（離すとキューブが変わる）";
        }

        TMP_InputField input = motionStateText != null
            ? motionStateText.GetComponentInParent<TMP_InputField>()
            : null;
        if (input != null)
        {
            input.interactable = false;
            input.readOnly = true;
        }

        RefreshMotionStateText();
        EnsureBackgroundClearCamera();
        Canvas.ForceUpdateCanvases();
        UpdateCameraViewportToPreview();

        if (string.IsNullOrEmpty(apiKey) || microphoneDevice == null)
        {
            SetStatus("準備エラー（キーまたはマイク）", false);
            return;
        }

        StartCoroutine(ConnectLiveSessionCoroutine());
        StartCoroutine(PlaybackPumpCoroutine());
    }

    // 受信キュー、Space、マイク送信、点滅、運動の漸近
    void Update()
    {
        DrainMainThreadActions();
        UpdatePushToTalk();
        PumpMicrophoneChunksIfStreaming();
        UpdateLevelMeter();
        UpdateStatusBlink();
        StepMotion();
    }

    // Preview 矩形へカメラを追従させる
    void LateUpdate()
    {
        UpdateCameraViewportToPreview();
    }

    void OnDisable()
    {
        RestoreCameraViewport();
    }

    void OnDestroy()
    {
        StopMicStreamingInternal();
        CloseSocket(true);
    }

    // ----- 運動（符号付き角速度 + サイズ、lerp で漸近） -----

    // シーンの初期スケールを 1 倍として覚え、目標を初期値にする
    void InitMotionFromScene()
    {
        baseScale = 1f;
        if (cubeRenderer != null)
        {
            float sx = cubeRenderer.transform.localScale.x;
            if (sx > 0.0001f)
            {
                baseScale = sx;
            }
        }

        currentAngularVelocity = initialAngularVelocity;
        targetAngularVelocity = initialAngularVelocity;
        currentSize = initialSize;
        targetSize = initialSize;
        ApplyCurrentScale();
    }

    // 毎フレーム、現在値を目標へ指数減衰で寄せてから回転・スケールする
    void StepMotion()
    {
        float dt = Time.deltaTime;
        currentAngularVelocity = SmoothTowards(currentAngularVelocity, targetAngularVelocity, dt);
        currentSize = SmoothTowards(currentSize, targetSize, dt);

        if (cubeRenderer != null)
        {
            if (Mathf.Abs(currentAngularVelocity) > 0.0001f)
            {
                cubeRenderer.transform.Rotate(0f, currentAngularVelocity * dt, 0f, Space.World);
            }

            ApplyCurrentScale();
        }

        RefreshMotionStateText();
    }

    // t = 1 - exp(-k*dt) の lerp。フレームレートに依らず同じ速さで漸近する
    float SmoothTowards(float current, float target, float dt)
    {
        if (motionLerpSpeed <= 0f)
        {
            return target;
        }

        if (Mathf.Abs(current - target) < 0.0001f)
        {
            return target;
        }

        float t = 1f - Mathf.Exp(-motionLerpSpeed * dt);
        return Mathf.Lerp(current, target, t);
    }

    void ApplyCurrentScale()
    {
        if (cubeRenderer == null)
        {
            return;
        }

        float s = baseScale * currentSize;
        cubeRenderer.transform.localScale = new Vector3(s, s, s);
    }

    // toolCall の引数を目標へ書く。無いキーは現状維持
    void ApplyMotionArgs(string argsJson)
    {
        float parsed;
        if (TryExtractJsonNumber(argsJson, "angularVelocity", out parsed)
            || TryExtractJsonNumber(argsJson, "angular_velocity", out parsed))
        {
            targetAngularVelocity = Mathf.Clamp(parsed, -maxAbsAngularVelocity, maxAbsAngularVelocity);
        }

        if (TryExtractJsonNumber(argsJson, "size", out parsed))
        {
            targetSize = Mathf.Clamp(parsed, minSize, maxSize);
        }

        RefreshMotionStateText();
    }

    void RefreshMotionStateText()
    {
        if (motionStateText == null)
        {
            return;
        }

        motionStateText.text =
            "ω  " + currentAngularVelocity.ToString("0.0")
            + "  →  " + targetAngularVelocity.ToString("0.0") + " deg/s\n"
            + "size  " + currentSize.ToString("0.00")
            + "  →  " + targetSize.ToString("0.00");
    }

    // ----- 接続（Setup に tools） -----

    // WebSocket を開き、tools 付き Setup を送って setupComplete を待つ
    IEnumerator ConnectLiveSessionCoroutine()
    {
        SetStatus("接続中…", true);

        Uri uri = new Uri(WsEndpoint + "?key=" + Uri.EscapeDataString(apiKey));
        socket = new ClientWebSocket();
        receiveCts = new CancellationTokenSource();

        Task connectTask = socket.ConnectAsync(uri, receiveCts.Token);
        while (!connectTask.IsCompleted)
        {
            yield return null;
        }

        if (connectTask.IsFaulted || socket.State != WebSocketState.Open)
        {
            string err = connectTask.Exception != null
                ? connectTask.Exception.GetBaseException().Message
                : "WebSocket を開けませんでした。";
            ShowError("接続失敗: " + err);
            yield break;
        }

        string setupJson = BuildSetupJson();
        RefreshSetupPanel();

        Task sendSetup = SendTextAsync(setupJson);
        while (!sendSetup.IsCompleted)
        {
            yield return null;
        }

        if (sendSetup.IsFaulted)
        {
            ShowError("Setup 送信失敗: " + sendSetup.Exception.GetBaseException().Message);
            yield break;
        }

        _ = ReceiveLoopAsync(receiveCts.Token);

        float wait = 0f;
        while (!setupComplete && wait < 15f)
        {
            wait += Time.deltaTime;
            yield return null;
        }

        if (!setupComplete)
        {
            ShowError("setupComplete がタイムアウトしました。モデル名とキーを確認してください。");
            yield break;
        }

        isConnected = true;
        SetStatus("接続済み（Space で送信）", false);
        AppendOutboundLog("setupComplete 受信");
        Debug.Log("[SpeechToMotion] Live セッション準備完了（tools: set_cube_motion）");
    }

    // Setup: 手動 VAD + AUDIO + transcription + functionDeclarations
    string BuildSetupJson()
    {
        StringBuilder sb = new StringBuilder(1024);
        sb.Append("{\"setup\":{");
        sb.Append("\"model\":\"models/");
        sb.Append(GeminiJson.Escape(modelName));
        sb.Append("\",");
        sb.Append("\"realtimeInputConfig\":{\"automaticActivityDetection\":{\"disabled\":true}},");
        sb.Append("\"inputAudioTranscription\":{},");
        sb.Append("\"outputAudioTranscription\":{},");

        if (!string.IsNullOrEmpty(systemInstruction))
        {
            sb.Append("\"systemInstruction\":{\"parts\":[{\"text\":\"");
            sb.Append(GeminiJson.Escape(systemInstruction));
            sb.Append("\"}]},");
        }

        sb.Append("\"tools\":[{\"functionDeclarations\":[");
        sb.Append(FunctionDeclarationJson);
        sb.Append("]}],");

        sb.Append("\"generationConfig\":{");
        sb.Append("\"responseModalities\":[\"AUDIO\"],");
        sb.Append("\"speechConfig\":{\"voiceConfig\":{\"prebuiltVoiceConfig\":{\"voiceName\":\"");
        sb.Append(GeminiJson.Escape(voiceName));
        sb.Append("\"}}}");
        sb.Append('}');
        sb.Append("}}");
        return sb.ToString();
    }

    // ----- Space 押し話し -----

    // 旧 Input Manager で Space の押し／離しを見る
    void UpdatePushToTalk()
    {
        if (!isConnected || !setupComplete)
        {
            return;
        }

        if (!isMicStreaming && Input.GetKeyDown(KeyCode.Space))
        {
            BeginRecording();
            return;
        }

        if (!isMicStreaming)
        {
            return;
        }

        if (Time.time - recordingStartedTime >= maxRecordingSeconds)
        {
            EndRecording();
            return;
        }

        if (Input.GetKeyUp(KeyCode.Space))
        {
            EndRecording();
        }
    }

    // Space 押し始め: マイク開始 + activityStart
    void BeginRecording()
    {
        if (!TryStartMicrophoneStreaming())
        {
            return;
        }

        recordingStartedTime = Time.time;
        SetStatus("録音送信中", true);
        StartCoroutine(SendJsonCoroutine("{\"realtimeInput\":{\"activityStart\":{}}}"));
        AppendOutboundLog("activityStart");
    }

    // Space 解放: 残りサンプル + activityEnd
    void EndRecording()
    {
        if (!isMicStreaming)
        {
            return;
        }

        float elapsed = Time.time - recordingStartedTime;
        PumpMicrophoneChunksIfStreaming();
        StopMicStreamingInternal();

        if (elapsed < minRecordingSeconds)
        {
            SetStatus("短すぎます（もう一度 Space）", false);
            return;
        }

        StartCoroutine(SendJsonCoroutine("{\"realtimeInput\":{\"activityEnd\":{}}}"));
        AppendOutboundLog("activityEnd");
        replyWaitStarted = Time.realtimeSinceStartup; // 送信完了。返信までの計測用
        SetStatus("返答待ち", true);
    }

    bool TryStartMicrophoneStreaming()
    {
        if (isMicStreaming)
        {
            return true;
        }

        if (microphoneDevice == null || socket == null || socket.State != WebSocketState.Open)
        {
            ShowError("録音できません（未接続またはマイクなし）。");
            return false;
        }

        inputTranscriptBuffer = string.Empty;
        outputTranscriptBuffer = string.Empty;

        micClip = Microphone.Start(microphoneDevice, true, MicClipSeconds, sampleRate);
        if (micClip == null)
        {
            ShowError("Microphone.Start に失敗しました。");
            return false;
        }

        lastMicSamplePos = 0;
        isMicStreaming = true;
        return true;
    }

    void StopMicStreamingInternal()
    {
        isMicStreaming = false;
        if (microphoneDevice != null && Microphone.IsRecording(microphoneDevice))
        {
            Microphone.End(microphoneDevice);
        }

        micClip = null;
    }

    // リングバッファの新規サンプルを 16-bit PCM にして realtimeInput で送る
    void PumpMicrophoneChunksIfStreaming()
    {
        if (!isMicStreaming || micClip == null || socket == null || socket.State != WebSocketState.Open)
        {
            return;
        }

        int pos = Microphone.GetPosition(microphoneDevice);
        if (pos < 0 || pos == lastMicSamplePos)
        {
            return;
        }

        int samplesToSend;
        if (pos > lastMicSamplePos)
        {
            samplesToSend = pos - lastMicSamplePos;
        }
        else
        {
            samplesToSend = micClip.samples - lastMicSamplePos + pos;
        }

        if (samplesToSend <= 0)
        {
            return;
        }

        float[] floats = new float[samplesToSend * micClip.channels];
        if (pos > lastMicSamplePos)
        {
            micClip.GetData(floats, lastMicSamplePos);
        }
        else
        {
            int first = micClip.samples - lastMicSamplePos;
            float[] a = new float[first * micClip.channels];
            float[] b = new float[pos * micClip.channels];
            micClip.GetData(a, lastMicSamplePos);
            if (pos > 0)
            {
                micClip.GetData(b, 0);
            }

            Array.Copy(a, 0, floats, 0, a.Length);
            Array.Copy(b, 0, floats, a.Length, b.Length);
        }

        lastMicSamplePos = pos;
        byte[] pcm = AudioCodec.FloatsToPcm16(floats);
        if (pcm.Length == 0)
        {
            return;
        }

        string b64 = Convert.ToBase64String(pcm);
        string json =
            "{\"realtimeInput\":{\"audio\":{\"mimeType\":\"audio/pcm;rate="
            + sampleRate
            + "\",\"data\":\""
            + b64
            + "\"}}}";
        StartCoroutine(SendJsonCoroutine(json));

        outboundTotalBytes += pcm.Length;
        AppendOutboundLog("+chunk " + pcm.Length + "B / total " + outboundTotalBytes + "B");
    }

    // ----- 送受信 -----

    IEnumerator SendJsonCoroutine(string json)
    {
        if (socket == null || socket.State != WebSocketState.Open)
        {
            yield break;
        }

        Task task = SendTextAsync(json);
        while (!task.IsCompleted)
        {
            yield return null;
        }

        if (task.IsFaulted)
        {
            EnqueueMain(() => ShowError("送信エラー: " + task.Exception.GetBaseException().Message));
        }
    }

    async Task SendTextAsync(string json)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            receiveCts != null ? receiveCts.Token : CancellationToken.None).ConfigureAwait(false);
    }

    async Task ReceiveLoopAsync(CancellationToken token)
    {
        byte[] buffer = new byte[64 * 1024];
        try
        {
            while (socket != null && socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token)
                            .ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            EnqueueMain(() =>
                            {
                                isConnected = false;
                                SetStatus("切断されました", false);
                            });
                            return;
                        }

                        ms.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    HandleServerMessage(Encoding.UTF8.GetString(ms.ToArray()));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止
        }
        catch (Exception e)
        {
            EnqueueMain(() => ShowError("受信ループ例外: " + e.Message));
        }
    }

    // サーバ JSON を振り分ける。toolCall は実行してすぐ toolResponse を返す（同期 function call）
    void HandleServerMessage(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        if (json.IndexOf("\"setupComplete\"", StringComparison.Ordinal) >= 0)
        {
            EnqueueMain(() => { setupComplete = true; });
            return;
        }

        if (json.IndexOf("\"toolCall\"", StringComparison.Ordinal) >= 0
            || json.IndexOf("\"tool_call\"", StringComparison.Ordinal) >= 0)
        {
            EnqueueMain(() => HandleToolCallOnMain(json));
        }

        TryExtractAndEnqueueAudio(json);

        if (json.IndexOf("\"inputTranscription\"", StringComparison.Ordinal) >= 0)
        {
            string t = GeminiJsonScan.NestedTextAfterKey(json, "inputTranscription");
            if (!string.IsNullOrEmpty(t))
            {
                EnqueueMain(() => OnInputTranscription(t));
            }
        }

        if (json.IndexOf("\"outputTranscription\"", StringComparison.Ordinal) >= 0)
        {
            string t = GeminiJsonScan.NestedTextAfterKey(json, "outputTranscription");
            if (!string.IsNullOrEmpty(t))
            {
                EnqueueMain(() => OnOutputTranscription(t));
            }
        }

        if (json.IndexOf("\"turnComplete\":true", StringComparison.Ordinal) >= 0
            || json.IndexOf("\"turnComplete\": true", StringComparison.Ordinal) >= 0)
        {
            EnqueueMain(OnTurnComplete);
        }

        if (json.IndexOf("\"interrupted\":true", StringComparison.Ordinal) >= 0)
        {
            EnqueueMain(ClearPlaybackQueue);
        }
    }

    // toolCall を実行し、結果を同じセッションへ返す（返さないと 3.1 Live は次の発話を始めない）
    void HandleToolCallOnMain(string json)
    {
        if (inboundLogText != null)
        {
            inboundLogText.text = GeminiJson.PrettyPrint(GeminiJson.Truncate(json, 1200));
        }

        List<string> responses = new List<string>();
        int searchFrom = 0;
        while (true)
        {
            int nameKey = IndexOfJsonKey(json, "name", searchFrom);
            if (nameKey < 0)
            {
                break;
            }

            string name = GeminiJsonScan.StringFieldFrom(json, nameKey);
            int idKey = IndexOfJsonKey(json, "id", Mathf.Max(0, nameKey - 80));
            string id = idKey >= 0 ? GeminiJsonScan.StringFieldFrom(json, idKey) : string.Empty;
            string argsJson = ExtractArgsObjectNear(json, nameKey);
            searchFrom = nameKey + 6;

            if (name != FunctionName)
            {
                continue;
            }

            ApplyMotionArgs(argsJson);
            responses.Add(BuildFunctionResponseJson(id, name, "ok"));
            AppendOutboundLog("tool: " + FunctionName + " → ω=" + targetAngularVelocity.ToString("0.0")
                              + " size=" + targetSize.ToString("0.00"));
        }

        if (responses.Count == 0)
        {
            return;
        }

        string payload = "{\"toolResponse\":{\"functionResponses\":[" + string.Join(",", responses.ToArray()) + "]}}";
        AppendOutboundLog("toolResponse 送信");
        StartCoroutine(SendJsonCoroutine(payload));
        SetStatus("関数を実行（漸近中）", true);
    }

    string BuildFunctionResponseJson(string id, string name, string result)
    {
        StringBuilder sb = new StringBuilder(256);
        sb.Append("{\"id\":\"");
        sb.Append(GeminiJson.Escape(id ?? string.Empty));
        sb.Append("\",\"name\":\"");
        sb.Append(GeminiJson.Escape(name ?? string.Empty));
        sb.Append("\",\"response\":{");
        sb.Append("\"result\":\"");
        sb.Append(GeminiJson.Escape(result));
        sb.Append("\",\"angularVelocity\":");
        sb.Append(targetAngularVelocity.ToString("0.###"));
        sb.Append(",\"size\":");
        sb.Append(targetSize.ToString("0.###"));
        sb.Append("}}");
        return sb.ToString();
    }

    // name キーの近くにある args / arguments オブジェクトを取り出す
    static string ExtractArgsObjectNear(string json, int nameKey)
    {
        int windowStart = Mathf.Max(0, nameKey - 200);
        int windowEnd = Mathf.Min(json.Length, nameKey + 400);
        string window = json.Substring(windowStart, windowEnd - windowStart);
        int argsKey = IndexOfJsonKey(window, "args", 0);
        if (argsKey < 0)
        {
            argsKey = IndexOfJsonKey(window, "arguments", 0);
        }

        if (argsKey < 0)
        {
            return "{}";
        }

        int brace = window.IndexOf('{', argsKey);
        if (brace < 0)
        {
            return "{}";
        }

        string obj = ExtractBalancedObject(window, brace);
        return string.IsNullOrEmpty(obj) ? "{}" : obj;
    }

    void TryExtractAndEnqueueAudio(string json)
    {
        if (json.IndexOf("\"inlineData\"", StringComparison.Ordinal) < 0
            && json.IndexOf("\"inline_data\"", StringComparison.Ordinal) < 0)
        {
            return;
        }

        if (json.IndexOf("audio", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return;
        }

        const string marker = "\"data\":\"";
        int searchFrom = 0;
        while (true)
        {
            int dataIndex = json.IndexOf(marker, searchFrom, StringComparison.Ordinal);
            if (dataIndex < 0)
            {
                break;
            }

            int valueStart = dataIndex + marker.Length;
            int valueEnd = json.IndexOf('"', valueStart);
            if (valueEnd < 0)
            {
                break;
            }

            string b64 = json.Substring(valueStart, valueEnd - valueStart);
            searchFrom = valueEnd + 1;
            if (b64.Length < 64)
            {
                continue;
            }

            try
            {
                byte[] pcm = Convert.FromBase64String(b64);
                if (pcm.Length >= 2)
                {
                    playbackPcmQueue.Enqueue(pcm);
                }
            }
            catch (FormatException)
            {
                // 非 Base64 は無視
            }
        }
    }

    void OnInputTranscription(string fragment)
    {
        inputTranscriptBuffer = (inputTranscriptBuffer ?? string.Empty) + fragment;
        AppendTranscription("in: " + fragment);
    }

    void OnOutputTranscription(string fragment)
    {
        outputTranscriptBuffer = (outputTranscriptBuffer ?? string.Empty) + fragment;
        AppendTranscription("out: " + fragment);
    }

    void OnTurnComplete()
    {
        if (replyWaitStarted >= 0f)
        {
            ResponseTime.Log("合計", replyWaitStarted);
            replyWaitStarted = -1f;
        }

        string userText = (inputTranscriptBuffer ?? string.Empty).Trim();
        string modelText = (outputTranscriptBuffer ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(userText) || !string.IsNullOrEmpty(modelText))
        {
            AppendTranscription("--- turn ---");
            if (!string.IsNullOrEmpty(userText))
            {
                AppendTranscription("You: " + userText);
            }

            if (!string.IsNullOrEmpty(modelText))
            {
                AppendTranscription("Gemini: " + modelText);
            }
        }

        inputTranscriptBuffer = string.Empty;
        outputTranscriptBuffer = string.Empty;
        SetStatus(isConnected ? "接続済み（Space で送信）" : "切断", false);
    }

    // ----- 再生 -----

    IEnumerator PlaybackPumpCoroutine()
    {
        playbackCoroutineRunning = true;
        while (playbackCoroutineRunning)
        {
            byte[] pcm;
            if (!playbackPcmQueue.TryDequeue(out pcm))
            {
                yield return null;
                continue;
            }

            EnsurePlaybackAudioSource();
            AudioClip clip = AudioCodec.Pcm16ToClip(pcm, playbackSampleRate);
            if (clip == null)
            {
                continue;
            }

            playbackAudioSource.clip = clip;
            playbackAudioSource.Play();
            while (playbackAudioSource != null && playbackAudioSource.isPlaying)
            {
                yield return null;
            }

            Destroy(clip);
        }
    }

    void ClearPlaybackQueue()
    {
        byte[] ignored;
        while (playbackPcmQueue.TryDequeue(out ignored))
        {
        }

        if (playbackAudioSource != null && playbackAudioSource.isPlaying)
        {
            playbackAudioSource.Stop();
        }
    }

    // ----- 3D Preview（2B と同じカメラ矩形） -----

    void EnsureBackgroundClearCamera()
    {
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null)
        {
            return;
        }

        if (backgroundClearCamera == null)
        {
            Transform existing = cam.transform.Find("BackgroundClearCamera");
            if (existing != null)
            {
                backgroundClearCamera = existing.GetComponent<Camera>();
            }
        }

        if (backgroundClearCamera == null)
        {
            GameObject go = new GameObject("BackgroundClearCamera");
            go.transform.SetParent(cam.transform, false);
            backgroundClearCamera = go.AddComponent<Camera>();
        }

        backgroundClearCamera.CopyFrom(cam);
        backgroundClearCamera.ResetProjectionMatrix();
        backgroundClearCamera.rect = new Rect(0f, 0f, 1f, 1f);
        backgroundClearCamera.cullingMask = 0;
        backgroundClearCamera.clearFlags = CameraClearFlags.SolidColor;
        backgroundClearCamera.backgroundColor = cam.backgroundColor;
        backgroundClearCamera.depth = cam.depth - 1f;
        backgroundClearCamera.enabled = true;
    }

    void UpdateCameraViewportToPreview()
    {
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null || previewArea == null)
        {
            return;
        }

        Canvas.ForceUpdateCanvases();

        Canvas canvas = previewArea.GetComponentInParent<Canvas>();
        Camera eventCam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            eventCam = canvas.worldCamera;
        }

        Vector3[] corners = new Vector3[4];
        previewArea.GetWorldCorners(corners);
        Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(eventCam, corners[0]);
        Vector2 topRight = RectTransformUtility.WorldToScreenPoint(eventCam, corners[2]);

        if (cameraViewportOwned)
        {
            cam.rect = new Rect(0f, 0f, 1f, 1f);
        }

        Rect pixelRect = cam.pixelRect;
        if (pixelRect.width <= 1f || pixelRect.height <= 1f)
        {
            return;
        }

        float xMin = (Mathf.Min(bottomLeft.x, topRight.x) - pixelRect.x) / pixelRect.width;
        float xMax = (Mathf.Max(bottomLeft.x, topRight.x) - pixelRect.x) / pixelRect.width;
        float yMin = (Mathf.Min(bottomLeft.y, topRight.y) - pixelRect.y) / pixelRect.height;
        float yMax = (Mathf.Max(bottomLeft.y, topRight.y) - pixelRect.y) / pixelRect.height;

        float x = Mathf.Clamp01(xMin);
        float y = Mathf.Clamp01(yMin);
        float w = Mathf.Clamp01(xMax - xMin);
        float h = Mathf.Clamp01(yMax - yMin);
        if (x + w > 1f)
        {
            w = 1f - x;
        }

        if (y + h > 1f)
        {
            h = 1f - y;
        }

        if (w < 0.05f || h < 0.05f)
        {
            return;
        }

        cam.ResetProjectionMatrix();
        cam.rect = new Rect(x, y, w, h);
        cameraViewportOwned = true;

        if (backgroundClearCamera != null)
        {
            backgroundClearCamera.backgroundColor = cam.backgroundColor;
            backgroundClearCamera.rect = new Rect(0f, 0f, 1f, 1f);
        }
    }

    void RestoreCameraViewport()
    {
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam != null && cameraViewportOwned)
        {
            cam.ResetProjectionMatrix();
            cam.rect = new Rect(0f, 0f, 1f, 1f);
        }

        cameraViewportOwned = false;
        if (backgroundClearCamera != null)
        {
            backgroundClearCamera.enabled = false;
        }
    }

    // ----- UI / ログ -----

    // 1. Setup に、Gemini へ渡す事前指示と関数宣言を並べて出す
    void RefreshSetupPanel()
    {
        if (setupHeaderText == null)
        {
            return;
        }

        string instruction = string.IsNullOrEmpty(systemInstruction)
            ? "（空。Setup に載せない）"
            : systemInstruction;

        setupHeaderText.text =
            "systemInstruction:\n"
            + instruction
            + "\n\nfunctionDeclarations:\n"
            + GeminiJson.PrettyPrint("[" + FunctionDeclarationJson + "]");
    }

    void InitPanelTexts()
    {
        RefreshSetupPanel();

        if (outboundLogText != null)
        {
            outboundLogText.text = "（まだ送信していません）";
        }

        if (inboundLogText != null)
        {
            inboundLogText.text = "（まだ toolCall がありません）";
        }

        if (transcriptionText != null)
        {
            transcriptionText.text = "（transcription 待ち）";
        }

        SetStatus("接続前", false);
    }

    void AppendOutboundLog(string line)
    {
        if (outboundLog.Length > 0)
        {
            outboundLog.Append('\n');
        }

        outboundLog.Append(line);
        if (outboundLog.Length > MaxLogChars)
        {
            outboundLog.Remove(0, outboundLog.Length - MaxLogChars);
        }

        if (outboundLogText != null)
        {
            outboundLogText.text = outboundLog.ToString();
        }
    }

    void AppendTranscription(string line)
    {
        if (transcriptionText == null)
        {
            return;
        }

        string current = transcriptionText.text;
        if (current == "（transcription 待ち）")
        {
            current = string.Empty;
        }

        if (current.Length > 0)
        {
            current += "\n";
        }

        current += line;
        if (current.Length > MaxLogChars)
        {
            current = current.Substring(current.Length - MaxLogChars);
        }

        transcriptionText.text = current;
    }

    // 送信中クリップの大きさを横棒の長さにする（計算は MicLevel）
    void UpdateLevelMeter()
    {
        if (levelMeterFill == null)
        {
            return;
        }

        int position = microphoneDevice != null ? Microphone.GetPosition(microphoneDevice) : -1;
        float target = MicLevel.ReadBar(micClip, position, meterSamples, true, displayedLevel);
        displayedLevel = MicLevel.Smooth(displayedLevel, target, Time.deltaTime);
        levelMeterFill.fillAmount = displayedLevel;
    }

    void SetStatus(string statusJapanese, bool blink)
    {
        statusBlink = blink;
        if (statusText == null)
        {
            return;
        }

        statusText.text = statusJapanese;
        Color color = statusText.color;
        color.a = 1f;
        statusText.color = color;
    }

    void UpdateStatusBlink()
    {
        if (!statusBlink || statusText == null)
        {
            return;
        }

        float wave = (Mathf.Sin(Time.unscaledTime * StatusBlinkSpeed) + 1f) * 0.5f;
        Color color = statusText.color;
        color.a = Mathf.Lerp(0.25f, 1f, wave);
        statusText.color = color;
    }

    void ShowError(string message)
    {
        Debug.LogError("[SpeechToMotion] " + message);
        SetStatus("エラー", false);
        AppendOutboundLog("[Error] " + message);
    }

    // ----- マイク / PCM -----

    void SetupMicrophone()
    {
        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            microphoneDevice = null;
            Debug.LogWarning("[SpeechToMotion] マイクがありません。");
            return;
        }

        microphoneDevice = Microphone.devices[0];
        Debug.Log("[SpeechToMotion] マイク: " + microphoneDevice);
    }

    void EnsurePlaybackAudioSource()
    {
        if (playbackAudioSource != null)
        {
            return;
        }

        playbackAudioSource = GetComponent<AudioSource>();
        if (playbackAudioSource == null)
        {
            playbackAudioSource = gameObject.AddComponent<AudioSource>();
        }

        playbackAudioSource.playOnAwake = false;
    }

    // ----- 設定ファイル -----

    void LoadApiKey()
    {
        string error;
        if (!GeminiKey.TryRead(apiKeyRelativePath, out apiKey, out error))
        {
            Debug.LogError("[SpeechToMotion] " + error);
            return;
        }
    }

    void LoadSystemInstruction()
    {
        string path = Path.Combine(Application.dataPath, systemInstructionRelativePath);
        if (File.Exists(path))
        {
            systemInstruction = File.ReadAllText(path).Trim();
        }

        if (string.IsNullOrEmpty(systemInstruction))
        {
            systemInstruction = DefaultSystemInstruction;
        }
    }

    // ----- メインスレッド / 切断 -----

    void EnqueueMain(Action action)
    {
        if (action != null)
        {
            mainThreadActions.Enqueue(action);
        }
    }

    void DrainMainThreadActions()
    {
        Action action;
        while (mainThreadActions.TryDequeue(out action))
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Debug.LogError("[SpeechToMotion] メインスレッド処理例外: " + e.Message);
            }
        }
    }

    void CloseSocket(bool stopPlaybackPump)
    {
        if (stopPlaybackPump)
        {
            playbackCoroutineRunning = false;
        }

        try
        {
            if (receiveCts != null)
            {
                receiveCts.Cancel();
                receiveCts.Dispose();
            }
        }
        catch
        {
            // ignore
        }

        receiveCts = null;

        try
        {
            if (socket != null)
            {
                if (socket.State == WebSocketState.Open)
                {
                    socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None)
                        .Wait(500);
                }

                socket.Dispose();
            }
        }
        catch
        {
            // ignore
        }

        socket = null;
        isConnected = false;
        setupComplete = false;
    }

    // ----- JSON ヘルパー（教材用の最小パーサ） -----

    static int IndexOfJsonKey(string json, string key, int start)
    {
        return json.IndexOf("\"" + key + "\"", start, StringComparison.Ordinal);
    }

    static bool TryExtractJsonNumber(string json, string key, out float value)
    {
        value = 0f;
        if (string.IsNullOrEmpty(json))
        {
            return false;
        }

        int keyIndex = IndexOfJsonKey(json, key, 0);
        if (keyIndex < 0)
        {
            return false;
        }

        int colon = json.IndexOf(':', keyIndex);
        if (colon < 0)
        {
            return false;
        }

        int i = colon + 1;
        while (i < json.Length && char.IsWhiteSpace(json[i]))
        {
            i++;
        }

        int start = i;
        if (i < json.Length && (json[i] == '-' || json[i] == '+'))
        {
            i++;
        }

        bool sawDigit = false;
        while (i < json.Length && (char.IsDigit(json[i]) || json[i] == '.'))
        {
            if (char.IsDigit(json[i]))
            {
                sawDigit = true;
            }

            i++;
        }

        if (!sawDigit)
        {
            return false;
        }

        return float.TryParse(
            json.Substring(start, i - start),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);
    }

    static string ExtractBalancedObject(string json, int openBrace)
    {
        if (openBrace < 0 || openBrace >= json.Length || json[openBrace] != '{')
        {
            return null;
        }

        int depth = 0;
        bool inString = false;
        for (int i = openBrace; i < json.Length; i++)
        {
            char c = json[i];
            if (c == '"' && (i == 0 || json[i - 1] != '\\'))
            {
                inString = !inString;
            }

            if (inString)
            {
                continue;
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return json.Substring(openBrace, i - openBrace + 1);
                }
            }
        }

        return null;
    }
}
