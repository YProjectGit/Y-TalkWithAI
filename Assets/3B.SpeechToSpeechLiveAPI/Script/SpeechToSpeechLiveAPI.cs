// SpeechToSpeechLiveAPI.cs
// 3A.SpeechToSpeech の姉妹デモ。REST の STT→LLM→TTS ではなく、Live API（WebSocket）1セッションで音声↔音声を行う。
// 左ペインに会話（transcription）、中央に送信（Outbound）、右に受信（Inbound）を出し、ソケットの向きを追えるようにする。
//
// 上からの流れ:
//   Start → APIキー読込・systemInstruction・マイク・AudioSource・Live 接続（Setup）
//   【手動モード（初期）】
//     Space 押下 → activityStart → Microphone から PCM チャンクを realtimeInput で送り続ける
//     Space 解放 → 送信停止 → activityEnd
//   【VAD 自動モード】（ボタンで切替。切替時は Setup を載せ直すため再接続）
//     マイクを常時送信。サーバの automaticActivityDetection が無音でターンを区切る
//     このあいだ Space は無効
//   受信 → serverContent の音声を再生キューへ / transcription を吹き出しと右欄へ

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
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Gemini Live API（WebSocket）で、声入力から声の返答までを1セッションで可視化する。
/// </summary>
public class SpeechToSpeechLiveAPI : MonoBehaviour
{
    // ===== インスペクタ: 設定 =====

    public string modelName = "gemini-3.1-flash-live-preview"; // Live モデル名（Setup の models/ 以下）
    public string apiKeyRelativePath = "Common/APIKey.txt"; // Assets/ からの相対パス
    public string systemInstructionRelativePath = "Common/SystemInstruction.txt"; // 事前指示ファイル
    public int sampleRate = 16000; // 送信 PCM のサンプルレート（Hz）。Live 入力の想定に合わせる
    public int maxRecordingSeconds = 30; // Space 押し話しの上限秒数
    public float minRecordingSeconds = 0.3f; // これより短い発話は送らない
    public string voiceName = "Kore"; // Setup の prebuilt 声色名
    public int playbackSampleRate = 24000; // 受信 PCM の想定サンプルレート（Hz）

    // ===== インスペクタ: 左ペイン（チャット UI） =====

    public TMP_InputField systemInstructionField; // systemInstruction。空なら Setup に載せない
    public TMP_Text recordHintText; // Space / VAD モードの案内
    public Button vadModeButton; // VAD 自動モードのトグル（Message 行のボタン）
    public Transform messageContent; // バブルを並べる Content
    public ChatBubble messageBubblePrefab; // 1A と同型の吹き出し Prefab
    public ScrollRect chatScrollRect; // 新着時に下端へ

    // ===== インスペクタ: 可視化（段階バー / 送信 / 受信） =====

    public TMP_Text statusText; // 左下 Status（接続・エラーなど）
    public TMP_Text stageBarText; // Connect → Send PCM → Receive PCM → Play
    public TMP_Text setupHeaderText; // 送信ペイン上部: Setup 設定エッセンス
    public TMP_Text outboundLogText; // 送信チャンクログ（追記）
    public TMP_Text outboundStatusText; // 送信中（1行）
    public TMP_Text inboundHeaderText; // 受信ペイン上部: 受信前提
    public TMP_Text inboundLogText; // 受信チャンクログ（追記）
    public TMP_Text transcriptionText; // input/output 文字起こしログ
    public TMP_Text inboundStatusText; // 受信中 / 再生中（1行）
    public AudioSource playbackAudioSource; // 受信音声の再生先

    // ===== 内部状態 =====

    string apiKey; // APIキー（画面・ログに全文は出さない）
    ClientWebSocket socket; // Live セッション用 WebSocket
    CancellationTokenSource receiveCts; // 受信ループ停止用
    readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>(); // 受信スレッド→メインスレッド
    readonly ConcurrentQueue<byte[]> playbackPcmQueue = new ConcurrentQueue<byte[]>(); // 再生待ち PCM
    readonly StringBuilder outboundLog = new StringBuilder(); // 送信ログ本文
    readonly StringBuilder inboundLog = new StringBuilder(); // 受信ログ本文
    readonly StringBuilder transcriptionLog = new StringBuilder(); // 文字起こしログ本文

    bool setupComplete; // setupComplete 受信済みか
    bool isMicStreaming; // マイクから PCM を送り続けているか
    bool vadAutoMode; // true=サーバ VAD 自動 / false=Space 手動
    bool isReconnecting; // VAD 切替のための再接続中か
    bool isConnected; // ソケットが Open か
    string microphoneDevice; // マイク名
    AudioClip micClip; // ループ録音用クリップ
    int lastMicSamplePos; // 前回送ったサンプル位置
    float recordingStartedTime; // 手動モードの録音開始時刻
    long outboundTotalBytes; // 送信累計バイト
    long inboundTotalBytes; // 受信累計バイト
    int outboundChunkCount; // 送信チャンク数
    int inboundChunkCount; // 受信チャンク数
    string inputTranscriptBuffer; // 入力 transcription の蓄積（吹き出し用）
    string outputTranscriptBuffer; // 出力 transcription の蓄積
    bool playbackCoroutineRunning; // 再生コルーチン稼働中か
    DateTime systemInstructionFileWriteTimeUtc; // SystemInstruction.txt 同期用
    Stage currentStage = Stage.Connect; // 段階バー用
    TMP_Text vadModeButtonLabel; // ボタン上のラベル（Start で取得）
    Image vadModeButtonImage; // ボタン色（ON/OFF 表示用）
    string lastSetupJsonForDisplay; // Setup ヘッダに添える直近の Setup JSON

    const int MicClipSeconds = 2; // マイクのリングバッファ長さ（秒）
    const int MaxLogChars = 8000; // ログ欄の上限（古い先頭を捨てる）
    static readonly Color VadButtonOffColor = new Color(0.25f, 0.55f, 0.9f, 1f); // 手動モード時のボタン色
    static readonly Color VadButtonOnColor = new Color(0.2f, 0.65f, 0.35f, 1f); // VAD 自動 ON 時
    const string WsEndpoint =
        "wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent";

    enum Stage
    {
        Connect,
        SendPcm,
        ReceivePcm,
        Play
    }

    // ----- エントリポイント -----

    // 起動時: キー・UI・マイク・再生・Live 接続を順に用意する
    void Start()
    {
        LoadApiKey();
        LoadSystemInstructionFromFile();
        if (systemInstructionField != null)
        {
            systemInstructionField.onEndEdit.AddListener(OnSystemInstructionEndEdit);
        }

        SetupMicrophone();
        EnsurePlaybackAudioSource();
        SetupVadModeButton();
        InitPanelTexts();
        RefreshModeUi();

        if (string.IsNullOrEmpty(apiKey) || microphoneDevice == null)
        {
            SetStatus("準備エラー（キーまたはマイク）", false);
            SetVadButtonInteractable(false);
            return;
        }

        StartCoroutine(ConnectLiveSessionCoroutine());
        StartCoroutine(PlaybackPumpCoroutine());
    }

    // Space 押し話しと、受信スレッドからの UI 更新をメインスレッドで処理する
    void Update()
    {
        DrainMainThreadActions();
        UpdatePushToTalk();
        PumpMicrophoneChunksIfStreaming();
    }

    // 終了時にソケットとマイクを解放する
    void OnDestroy()
    {
        StopMicStreamingInternal();
        CloseSocket(true);
    }

    // VAD トグルボタンのラベル／クリックを配線する
    void SetupVadModeButton()
    {
        if (vadModeButton == null)
        {
            return;
        }

        vadModeButtonLabel = vadModeButton.GetComponentInChildren<TMP_Text>(true);
        vadModeButtonImage = vadModeButton.GetComponent<Image>();
        vadModeButton.onClick.RemoveListener(OnVadModeButtonClicked);
        vadModeButton.onClick.AddListener(OnVadModeButtonClicked);
    }

    // ----- 接続（Setup） -----

    // WebSocket を開き、最初の Setup JSON を送って setupComplete を待つ
    IEnumerator ConnectLiveSessionCoroutine()
    {
        SetStage(Stage.Connect);
        SetStatus("接続中…", true);
        SetOutboundStatus("—");
        SetInboundStatus("—");
        SetVadButtonInteractable(false);

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
                : "WebSocket を Open にできませんでした";
            ShowError("Live 接続失敗: " + err);
            SetVadButtonInteractable(true);
            yield break;
        }

        string setupJson = BuildSetupJson();
        RefreshSetupHeader(setupJson);
        SetInboundHeaderStatic();

        Task sendSetup = SendTextAsync(setupJson);
        while (!sendSetup.IsCompleted)
        {
            yield return null;
        }

        if (sendSetup.IsFaulted)
        {
            ShowError("Setup 送信失敗: " + sendSetup.Exception.GetBaseException().Message);
            SetVadButtonInteractable(true);
            yield break;
        }

        AppendOutboundLog("Setup 送信完了（設定は上部ヘッダ）");
        // 受信ループをバックグラウンドで回す（結果は mainThreadActions 経由）
        _ = Task.Run(() => ReceiveLoopAsync(receiveCts.Token));

        float wait = 0f;
        while (!setupComplete && wait < 15f)
        {
            wait += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!setupComplete)
        {
            ShowError("setupComplete がタイムアウトしました。モデル名とキーを確認してください。");
            SetVadButtonInteractable(true);
            yield break;
        }

        isConnected = true;
        SetStage(Stage.Connect);
        SetVadButtonInteractable(true);
        RefreshModeUi();

        // VAD 自動なら接続直後からマイク常時送信を始める（activityStart は送らない）
        if (vadAutoMode)
        {
            BeginContinuousListening();
        }
        else
        {
            SetStatus(GetReadyStatusText(), false);
        }

        Debug.Log(
            "[SpeechToSpeechLiveAPI] Live セッション準備完了。VAD="
            + (vadAutoMode ? "auto" : "manual"));
    }

    // Setup メッセージ（最初に1回だけ送るセッション設定）。VAD モードで automaticActivityDetection を切り替える
    string BuildSetupJson()
    {
        StringBuilder sb = new StringBuilder(512);
        sb.Append("{\"setup\":{");
        sb.Append("\"model\":\"models/");
        sb.Append(EscapeJson(modelName));
        sb.Append("\",");

        // 手動: disabled=true（Space で activityStart/End）
        // 自動: disabled=false（サーバが無音判定。クライアントは PCM を流し続けるだけ）
        if (vadAutoMode)
        {
            sb.Append("\"realtimeInputConfig\":{\"automaticActivityDetection\":{\"disabled\":false}},");
        }
        else
        {
            sb.Append("\"realtimeInputConfig\":{\"automaticActivityDetection\":{\"disabled\":true}},");
        }

        sb.Append("\"inputAudioTranscription\":{},");
        sb.Append("\"outputAudioTranscription\":{},");

        string instruction = GetSystemInstructionText();
        if (!string.IsNullOrEmpty(instruction))
        {
            sb.Append("\"systemInstruction\":{\"parts\":[{\"text\":\"");
            sb.Append(EscapeJson(instruction));
            sb.Append("\"}]},");
        }

        sb.Append("\"generationConfig\":{");
        sb.Append("\"responseModalities\":[\"AUDIO\"],");
        sb.Append("\"speechConfig\":{\"voiceConfig\":{\"prebuiltVoiceConfig\":{\"voiceName\":\"");
        sb.Append(EscapeJson(voiceName));
        sb.Append("\"}}}");
        sb.Append('}'); // generationConfig
        sb.Append("}}"); // setup + root
        return sb.ToString();
    }

    // ----- VAD モード切替（ボタン） -----

    // ボタンを押すたびに手動 ↔ サーバ VAD を入れ替える。Setup が接続時設定のため再接続する
    void OnVadModeButtonClicked()
    {
        if (isReconnecting)
        {
            return;
        }

        if (string.IsNullOrEmpty(apiKey) || microphoneDevice == null)
        {
            ShowError("VAD 切替できません（キーまたはマイクなし）。");
            return;
        }

        // 再接続するので activityEnd は送らず、マイクだけ止める
        StopMicStreamingInternal();
        vadAutoMode = !vadAutoMode;
        RefreshModeUi();
        StartCoroutine(ReconnectForVadModeCoroutine());
    }

    // 現在の vadAutoMode でソケットを張り直す
    IEnumerator ReconnectForVadModeCoroutine()
    {
        isReconnecting = true;
        SetVadButtonInteractable(false);
        SetStatus(vadAutoMode ? "VAD 自動へ切替中…" : "Space 手動へ切替中…", true);
        AppendOutboundLog(
            "mode switch → " + (vadAutoMode ? "VAD auto (reconnect)" : "manual Space (reconnect)"));

        CloseSocket(false);
        setupComplete = false;
        isConnected = false;
        ClearPlaybackQueue();

        yield return StartCoroutine(ConnectLiveSessionCoroutine());

        isReconnecting = false;
        if (!isConnected)
        {
            SetVadButtonInteractable(true);
        }
    }

    // 案内文・ボタン表示・Setup 要約の VAD 行をモードに合わせる
    void RefreshModeUi()
    {
        if (recordHintText != null)
        {
            recordHintText.text = vadAutoMode
                ? "VAD 自動モード中（Space 無効・サーバが無音でターンを区切る）"
                : "Space を押しているあいだ送信します（離すと返答待ち）";
        }

        if (vadModeButtonLabel != null)
        {
            vadModeButtonLabel.text = vadAutoMode ? "VAD ON" : "VAD 自動";
        }

        if (vadModeButtonImage != null)
        {
            vadModeButtonImage.color = vadAutoMode ? VadButtonOnColor : VadButtonOffColor;
        }

        if (setupHeaderText == null)
        {
            return;
        }

        // 設定行の VAD 表記を更新。接続後は直近の Setup JSON も添える
        if (!string.IsNullOrEmpty(lastSetupJsonForDisplay) && isConnected)
        {
            setupHeaderText.text =
                BuildSetupSettingsSummary() + "\n\n"
                + PrettyPrintJson(TruncateForDisplay(lastSetupJsonForDisplay, 400));
        }
        else
        {
            setupHeaderText.text =
                BuildSetupSettingsSummary() + "\n\n（接続後に Setup JSON を表示）";
        }
    }

    void SetVadButtonInteractable(bool interactable)
    {
        if (vadModeButton != null)
        {
            vadModeButton.interactable = interactable;
        }
    }

    string GetReadyStatusText()
    {
        return vadAutoMode
            ? "VAD 自動（無音で区切って返答）"
            : "接続済み（Space で送信）";
    }

    // ----- Space 押し話し（手動モードのみ） -----

    // 旧 Input Manager で Space の押し始め／離しを見る。VAD 自動中は無効
    void UpdatePushToTalk()
    {
        if (vadAutoMode || isReconnecting || !isConnected || !setupComplete)
        {
            return;
        }

        if (IsTypingInSystemInstruction())
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

    bool IsTypingInSystemInstruction()
    {
        if (systemInstructionField != null && systemInstructionField.isFocused)
        {
            return true;
        }

        if (EventSystem.current == null || EventSystem.current.currentSelectedGameObject == null)
        {
            return false;
        }

        return systemInstructionField != null
               && EventSystem.current.currentSelectedGameObject == systemInstructionField.gameObject;
    }

    // Space 押し始め: マイク開始 + activityStart（手動モード専用）
    void BeginRecording()
    {
        if (!TryStartMicrophoneStreaming())
        {
            return;
        }

        recordingStartedTime = Time.time;
        SetStage(Stage.SendPcm);
        SetStatus("録音送信中", true);
        SetOutboundStatus("送信中");
        SetInboundStatus("—");
        StartCoroutine(SendJsonCoroutine("{\"realtimeInput\":{\"activityStart\":{}}}"));
        AppendOutboundLog("activityStart");
    }

    // Space 解放: 残りサンプル送信 + activityEnd（手動モード専用）
    void EndRecording()
    {
        if (!isMicStreaming || vadAutoMode)
        {
            return;
        }

        float elapsed = Time.time - recordingStartedTime;
        // 最後の差分を送ってから止める
        PumpMicrophoneChunksIfStreaming();
        StopMicStreamingInternal();

        if (elapsed < minRecordingSeconds)
        {
            SetStatus("短すぎます（もう一度 Space）", false);
            SetOutboundStatus("—");
            SetStage(Stage.Connect);
            return;
        }

        StartCoroutine(SendJsonCoroutine("{\"realtimeInput\":{\"activityEnd\":{}}}"));
        AppendOutboundLog("activityEnd");
        SetOutboundStatus("—");
        SetInboundStatus("受信待ち");
        SetStatus("返答待ち", true);
        SetStage(Stage.ReceivePcm);
    }

    // VAD 自動: マイク常時送信を始める（activityStart は送らない。サーバが発話／無音を判定する）
    void BeginContinuousListening()
    {
        if (!TryStartMicrophoneStreaming())
        {
            return;
        }

        SetStage(Stage.SendPcm);
        SetStatus("VAD 自動（マイク常時送信）", true);
        SetOutboundStatus("常時送信");
        SetInboundStatus("—");
        AppendOutboundLog("VAD auto: continuous mic (no activityStart/End)");
    }

    // マイクリングバッファを開き、PCM 送信ループの対象にする
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

        SyncSystemInstructionBeforeSend();
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

    // マイク送信だけ止める（activityEnd は送らない。切替・終了用）
    void StopMicStreamingInternal()
    {
        isMicStreaming = false;
        if (microphoneDevice != null && Microphone.IsRecording(microphoneDevice))
        {
            Microphone.End(microphoneDevice);
        }

        micClip = null;
    }

    // マイク送信中だけ、リングバッファの新規サンプルを PCM にして送る
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
            // リングバッファが先頭に戻った
            samplesToSend = micClip.samples - lastMicSamplePos + pos;
        }

        if (samplesToSend <= 0)
        {
            return;
        }

        float[] floats = new float[samplesToSend * micClip.channels];
        // GetData は開始オフセットからの連続読み。ラップ時は2回に分ける
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
        byte[] pcm = FloatsToPcm16(floats);
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

        outboundChunkCount++;
        outboundTotalBytes += pcm.Length;
        AppendOutboundLog(
            "+chunk " + pcm.Length + "B / total " + outboundTotalBytes + "B  " + sampleRate + "Hz");
    }

    // ----- 送受信 -----

    // JSON テキストを WebSocket で送る（完了まで待つコルーチン）
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

    // 受信ループ（バックグラウンド）。UI / 再生への反映はメインスレッドキューへ
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

                    string json = Encoding.UTF8.GetString(ms.ToArray());
                    HandleServerMessage(json);
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

    // サーバ JSON を種別ごとに振り分ける（教材用の最小パーサ）
    void HandleServerMessage(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        if (json.IndexOf("\"setupComplete\"", StringComparison.Ordinal) >= 0)
        {
            EnqueueMain(() =>
            {
                setupComplete = true;
                AppendOutboundLog("setupComplete 受信");
            });
            return;
        }

        // 音声 inlineData（modelTurn.parts）
        TryExtractAndEnqueueAudio(json);

        // transcription（input / output）。オブジェクト内の "text" を取る
        if (json.IndexOf("\"inputTranscription\"", StringComparison.Ordinal) >= 0)
        {
            string t = ExtractNestedTextAfterKey(json, "inputTranscription");
            if (!string.IsNullOrEmpty(t))
            {
                EnqueueMain(() => OnInputTranscription(t));
            }
        }

        if (json.IndexOf("\"outputTranscription\"", StringComparison.Ordinal) >= 0)
        {
            string t = ExtractNestedTextAfterKey(json, "outputTranscription");
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
            EnqueueMain(() =>
            {
                ClearPlaybackQueue();
                SetInboundStatus("割り込み");
            });
        }
    }

    // candidates ではなく Live の modelTurn.parts[].inlineData.data を拾う
    void TryExtractAndEnqueueAudio(string json)
    {
        const string marker = "\"data\":\"";
        // inlineData ブロック内だけを対象にしたいが、教材用に mime が audio のときだけ採用
        if (json.IndexOf("\"inlineData\"", StringComparison.Ordinal) < 0
            && json.IndexOf("\"inline_data\"", StringComparison.Ordinal) < 0)
        {
            return;
        }

        if (json.IndexOf("audio", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return;
        }

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

            // Setup など短いフィールドを誤爆しにくいよう、ある程度長い Base64 だけ音声とみなす
            if (b64.Length < 64)
            {
                continue;
            }

            try
            {
                byte[] pcm = Convert.FromBase64String(b64);
                if (pcm.Length < 2)
                {
                    continue;
                }

                playbackPcmQueue.Enqueue(pcm);
                int len = pcm.Length;
                EnqueueMain(() =>
                {
                    inboundChunkCount++;
                    inboundTotalBytes += len;
                    AppendInboundLog(
                        "+audio " + len + "B / total " + inboundTotalBytes + "B  " + playbackSampleRate + "Hz");
                    SetStage(Stage.ReceivePcm);
                    SetInboundStatus("受信中");
                });
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
        AppendTranscriptionLog("in: " + fragment);
    }

    void OnOutputTranscription(string fragment)
    {
        outputTranscriptBuffer = (outputTranscriptBuffer ?? string.Empty) + fragment;
        AppendTranscriptionLog("out: " + fragment);
        SetStage(Stage.ReceivePcm);
    }

    // ターン完了: 吹き出しを確定し、再生段階へ
    void OnTurnComplete()
    {
        string userText = (inputTranscriptBuffer ?? string.Empty).Trim();
        string modelText = (outputTranscriptBuffer ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(userText))
        {
            AddBubble("You", userText, true);
        }

        if (!string.IsNullOrEmpty(modelText))
        {
            AddBubble("Gemini", modelText, false);
        }

        inputTranscriptBuffer = string.Empty;
        outputTranscriptBuffer = string.Empty;
        SetInboundStatus(playbackPcmQueue.IsEmpty ? "—" : "再生中");
        if (!playbackPcmQueue.IsEmpty)
        {
            SetStage(Stage.Play);
        }

        // VAD 自動ではマイク常時送信を続けたまま待機表示にする
        if (vadAutoMode && isMicStreaming)
        {
            SetStatus("VAD 自動（マイク常時送信）", true);
            SetOutboundStatus("常時送信");
            SetStage(Stage.SendPcm);
        }
        else
        {
            SetStatus(GetReadyStatusText(), false);
        }
    }

    // ----- 再生 -----

    // キューに溜まった PCM を順に AudioClip 化して再生する
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
            AudioClip clip = Pcm16ToClip(pcm, playbackSampleRate);
            if (clip == null)
            {
                continue;
            }

            SetStage(Stage.Play);
            SetInboundStatus("再生中");
            playbackAudioSource.clip = clip;
            playbackAudioSource.Play();
            while (playbackAudioSource != null && playbackAudioSource.isPlaying)
            {
                yield return null;
            }

            Destroy(clip);
            if (playbackPcmQueue.IsEmpty)
            {
                SetInboundStatus("—");
                if (!isMicStreaming)
                {
                    SetStage(Stage.Connect);
                }
                else if (vadAutoMode)
                {
                    SetStage(Stage.SendPcm);
                }
            }
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

    // ----- UI / ログ -----

    void InitPanelTexts()
    {
        SetStage(Stage.Connect);
        if (outboundLogText != null)
        {
            outboundLogText.text = "（まだ送信していません）";
        }

        if (inboundLogText != null)
        {
            inboundLogText.text = "（まだ受信していません）";
        }

        if (transcriptionText != null)
        {
            transcriptionText.text = "（transcription 待ち）";
        }

        SetOutboundStatus("—");
        SetInboundStatus("—");
        SetInboundHeaderStatic();
        // 接続前から設定（model / voice など）を見せ、JSON は接続後に足す
        if (setupHeaderText != null)
        {
            setupHeaderText.text =
                BuildSetupSettingsSummary() + "\n\n（接続後に Setup JSON を表示）";
        }
    }

    // Setup ヘッダ先頭の設定行（Inspector の modelName / voiceName など）
    string BuildSetupSettingsSummary()
    {
        string vadLine = vadAutoMode
            ? "VAD: auto (server silence detection)\n"
            : "VAD: manual (Space → activityStart/End)\n";

        return "model: " + modelName + "\n"
               + "responseModalities: AUDIO\n"
               + "voice: " + voiceName + "\n"
               + "transcription: input/output ON\n"
               + "send: PCM " + sampleRate + "Hz\n"
               + vadLine
               + "key: " + MaskApiKey(apiKey);
    }

    void RefreshSetupHeader(string setupJson)
    {
        lastSetupJsonForDisplay = setupJson;
        if (setupHeaderText == null)
        {
            return;
        }

        setupHeaderText.text =
            BuildSetupSettingsSummary() + "\n\n"
            + PrettyPrintJson(TruncateForDisplay(setupJson, 400));
    }

    void SetInboundHeaderStatic()
    {
        if (inboundHeaderText == null)
        {
            return;
        }

        inboundHeaderText.text =
            "output: PCM " + playbackSampleRate + "Hz\n"
            + "mime: audio/pcm (L16 LE)\n"
            + "channels: 1\n"
            + "transcription: serverContent 経由";
    }

    void SetStage(Stage stage)
    {
        currentStage = stage;
        if (stageBarText == null)
        {
            return;
        }

        string c = stage == Stage.Connect ? "[Connect]" : "Connect";
        string s = stage == Stage.SendPcm ? "[Send PCM]" : "Send PCM";
        string r = stage == Stage.ReceivePcm ? "[Receive PCM]" : "Receive PCM";
        string p = stage == Stage.Play ? "[Play]" : "Play";
        stageBarText.text = c + " → " + s + " → " + r + " → " + p;
    }

    void AppendOutboundLog(string line)
    {
        AppendLog(outboundLog, outboundLogText, line);
    }

    void AppendInboundLog(string line)
    {
        AppendLog(inboundLog, inboundLogText, line);
    }

    void AppendTranscriptionLog(string line)
    {
        AppendLog(transcriptionLog, transcriptionText, line);
    }

    static void AppendLog(StringBuilder sb, TMP_Text view, string line)
    {
        if (sb.Length > 0)
        {
            sb.Append('\n');
        }

        sb.Append(line);
        if (sb.Length > MaxLogChars)
        {
            sb.Remove(0, sb.Length - MaxLogChars);
        }

        if (view != null)
        {
            view.text = sb.ToString();
        }
    }

    void SetOutboundStatus(string text)
    {
        if (outboundStatusText != null)
        {
            outboundStatusText.text = text;
        }
    }

    void SetInboundStatus(string text)
    {
        if (inboundStatusText != null)
        {
            inboundStatusText.text = text;
        }
    }

    void SetStatus(string text, bool blinkUnused)
    {
        if (statusText != null)
        {
            statusText.text = text;
        }
    }

    void ShowError(string message)
    {
        Debug.LogError("[SpeechToSpeechLiveAPI] " + message);
        SetStatus("エラー", false);
        AddBubble("Error", message, false);
        AppendInboundLog("[Error] " + message);
    }

    void AddBubble(string speaker, string body, bool isUser)
    {
        if (messageBubblePrefab == null || messageContent == null)
        {
            return;
        }

        ChatBubble bubble = Instantiate(messageBubblePrefab, messageContent);
        bubble.SetMessage(speaker, body, isUser);
        Canvas.ForceUpdateCanvases();
        if (chatScrollRect != null)
        {
            chatScrollRect.verticalNormalizedPosition = 0f;
        }
    }

    // ----- マイク / PCM 変換 -----

    void SetupMicrophone()
    {
        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            microphoneDevice = null;
            Debug.LogWarning("[SpeechToSpeechLiveAPI] マイクがありません。");
            return;
        }

        microphoneDevice = Microphone.devices[0];
        Debug.Log("[SpeechToSpeechLiveAPI] マイク: " + microphoneDevice);
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

    static byte[] FloatsToPcm16(float[] samples)
    {
        byte[] pcm = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            float clamped = Mathf.Clamp(samples[i], -1f, 1f);
            short s = (short)Mathf.RoundToInt(clamped * short.MaxValue);
            pcm[i * 2] = (byte)(s & 0xff);
            pcm[i * 2 + 1] = (byte)((s >> 8) & 0xff);
        }

        return pcm;
    }

    static AudioClip Pcm16ToClip(byte[] pcm, int rate)
    {
        if (pcm == null || pcm.Length < 2 || rate <= 0)
        {
            return null;
        }

        int sampleCount = pcm.Length / 2;
        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            short s = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
            samples[i] = s / 32768f;
        }

        AudioClip clip = AudioClip.Create("LivePcm", sampleCount, 1, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    // ----- systemInstruction / APIキー -----

    void LoadApiKey()
    {
        string path = Path.Combine(Application.dataPath, apiKeyRelativePath);
        if (!File.Exists(path))
        {
            apiKey = null;
            Debug.LogError("[SpeechToSpeechLiveAPI] APIキーがありません: " + path);
            return;
        }

        apiKey = File.ReadAllText(path).Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            apiKey = null;
            Debug.LogError("[SpeechToSpeechLiveAPI] APIキーが空です。");
        }
    }

    string GetSystemInstructionFilePath()
    {
        return Path.Combine(Application.dataPath, systemInstructionRelativePath);
    }

    void LoadSystemInstructionFromFile()
    {
        if (systemInstructionField == null)
        {
            return;
        }

        string path = GetSystemInstructionFilePath();
        if (!File.Exists(path))
        {
            systemInstructionField.text = string.Empty;
            return;
        }

        systemInstructionField.text = File.ReadAllText(path);
        systemInstructionFileWriteTimeUtc = File.GetLastWriteTimeUtc(path);
    }

    // InputField の編集確定時
    void OnSystemInstructionEndEdit(string _)
    {
        SaveSystemInstructionFromField();
    }

    void SaveSystemInstructionFromField()
    {
        if (systemInstructionField == null)
        {
            return;
        }

        string path = GetSystemInstructionFilePath();
        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, systemInstructionField.text ?? string.Empty, new UTF8Encoding(false));
        systemInstructionFileWriteTimeUtc = File.GetLastWriteTimeUtc(path);
    }

    void SyncSystemInstructionBeforeSend()
    {
        if (systemInstructionField == null)
        {
            return;
        }

        string path = GetSystemInstructionFilePath();
        if (File.Exists(path))
        {
            DateTime write = File.GetLastWriteTimeUtc(path);
            if (write > systemInstructionFileWriteTimeUtc && !systemInstructionField.isFocused)
            {
                systemInstructionField.text = File.ReadAllText(path);
                systemInstructionFileWriteTimeUtc = write;
            }
        }

        SaveSystemInstructionFromField();
    }

    string GetSystemInstructionText()
    {
        if (systemInstructionField == null || systemInstructionField.text == null)
        {
            return string.Empty;
        }

        return systemInstructionField.text.Trim();
    }

    // ----- メインスレッド橋渡し / 切断 -----

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
                Debug.LogError("[SpeechToSpeechLiveAPI] メインスレッド処理例外: " + e.Message);
            }
        }
    }

    // WebSocket を閉じる。VAD 切替の再接続時は再生ポンプを止めない
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

    // ----- JSON / 表示ヘルパー -----

    // key のオブジェクト直後にある "text":"..." を取り出す（簡易）
    static string ExtractNestedTextAfterKey(string json, string objectKey)
    {
        int keyIndex = json.IndexOf("\"" + objectKey + "\"", StringComparison.Ordinal);
        if (keyIndex < 0)
        {
            return null;
        }

        int textKey = json.IndexOf("\"text\"", keyIndex, StringComparison.Ordinal);
        if (textKey < 0)
        {
            return null;
        }

        return ExtractJsonStringFieldFrom(json, textKey);
    }

    static string ExtractJsonStringFieldFrom(string json, int keyIndex)
    {
        int colon = json.IndexOf(':', keyIndex);
        if (colon < 0)
        {
            return null;
        }

        int firstQuote = json.IndexOf('"', colon + 1);
        if (firstQuote < 0)
        {
            return null;
        }

        int i = firstQuote + 1;
        StringBuilder sb = new StringBuilder();
        while (i < json.Length)
        {
            char c = json[i];
            if (c == '\\' && i + 1 < json.Length)
            {
                char n = json[i + 1];
                if (n == 'n')
                {
                    sb.Append('\n');
                }
                else if (n == '"')
                {
                    sb.Append('"');
                }
                else if (n == '\\')
                {
                    sb.Append('\\');
                }
                else
                {
                    sb.Append(n);
                }

                i += 2;
                continue;
            }

            if (c == '"')
            {
                break;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    static string MaskApiKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return "(none)";
        }

        if (key.Length <= 6)
        {
            return "******";
        }

        return key.Substring(0, 4) + "…" + new string('*', 8);
    }

    static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        StringBuilder sb = new StringBuilder(value.Length + 8);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            switch (c)
            {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    static string TruncateForDisplay(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
        {
            return value;
        }

        return value.Substring(0, maxChars) + "…(" + value.Length + " chars)";
    }

    static string PrettyPrintJson(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return string.Empty;
        }

        StringBuilder sb = new StringBuilder(json.Length + 32);
        int indent = 0;
        bool inString = false;
        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];
            if (c == '"' && (i == 0 || json[i - 1] != '\\'))
            {
                inString = !inString;
                sb.Append(c);
                continue;
            }

            if (inString)
            {
                sb.Append(c);
                continue;
            }

            switch (c)
            {
                case '{':
                case '[':
                    sb.Append(c);
                    sb.Append('\n');
                    indent++;
                    sb.Append(new string(' ', indent * 2));
                    break;
                case '}':
                case ']':
                    sb.Append('\n');
                    indent = Mathf.Max(0, indent - 1);
                    sb.Append(new string(' ', indent * 2));
                    sb.Append(c);
                    break;
                case ',':
                    sb.Append(c);
                    sb.Append('\n');
                    sb.Append(new string(' ', indent * 2));
                    break;
                case ':':
                    sb.Append(": ");
                    break;
                default:
                    if (!char.IsWhiteSpace(c))
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.ToString();
    }
}
