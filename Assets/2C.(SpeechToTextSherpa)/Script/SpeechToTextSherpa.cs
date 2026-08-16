// SpeechToTextSherpa.cs
// 2A.SpeechToText の派生デモ。STT だけ sherpa-onnx（ローカル）に差し替える。
// Chat は 2A と同じ Gemini generateContent。
//
// 上からの流れ:
//   Start → APIキー読込・systemInstruction 読込・マイク確認・sherpa 初期化
//   Space 押下 → Microphone.Start（録音中）
//   Space 解放 → Microphone.End → float サンプル
//     → sherpa offline 認識（端末）
//     → 認識テキストを user として Chat（2A と同型）へ
//
// 会話コンテキストは常に送る。
// systemInstruction は Chat リクエストにだけ載せる。空ならキーごと省略。

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// マイク録音とローカル STT / Gemini チャットで、声入力からテキスト返答までを可視化する。
/// </summary>
public class SpeechToTextSherpa : MonoBehaviour
{
    // ===== インスペクタ: 設定 =====

    public string modelName = "gemini-3.1-flash-lite"; // Chat に使う Gemini モデル名
    public string apiKeyRelativePath = "Common/APIKey.txt"; // Assets/ からの相対パス
    public string systemInstructionRelativePath = "Common/SystemInstruction.txt";
    public int sampleRate = 16000; // マイク録音のサンプルレート（Hz）。sherpa 側も 16kHz
    public int maxRecordingSeconds = 30;
    public float minRecordingSeconds = 0.3f;
    public int sherpaNumThreads = 2; // ローカル STT の CPU スレッド数

    // ===== インスペクタ: 左ペイン（チャット UI） =====

    public TMP_InputField systemInstructionField;
    public TMP_Text recordHintText;
    public Transform messageContent;
    public ChatBubble messageBubblePrefab;
    public ScrollRect chatScrollRect;

    // ===== インスペクタ: 通信可視化（1/2 はローカル STT、3/4 は Gemini Chat） =====

    public TMP_Text statusText;
    public TMP_Text sttRequestText; // 1. Local STT（sherpa-onnx）
    public TMP_Text llmRequestText; // 3. Request - GenerateContent（Text）
    public TMP_Text sttResponseText; // 2. Local STT 結果
    public TMP_Text llmResponseText; // 4. Response - GenerateContent（Text）

    // ===== 内部状態 =====

    string apiKey;
    readonly List<ChatTurn> turns = new List<ChatTurn>();
    bool isSending;
    bool isRecording;
    string microphoneDevice;
    AudioClip recordingClip;
    float recordingStartedTime;
    DateTime systemInstructionFileWriteTimeUtc;
    bool statusBlink;
    const float StatusBlinkSpeed = 6f;

    readonly SherpaOfflineAsr sherpa = new SherpaOfflineAsr(); // ローカル STT。Play 中は使い回す

    class ChatTurn
    {
        public string role;
        public string text;
    }

    class HttpResult
    {
        public long statusCode;
        public string body;
        public bool ok;
        public string error;
    }

    // ----- エントリポイント -----

    void Start()
    {
        LoadApiKey();
        LoadSystemInstructionFromFile();
        if (systemInstructionField != null)
        {
            systemInstructionField.onEndEdit.AddListener(OnSystemInstructionEndEdit);
        }

        SetupMicrophone();
        if (recordHintText != null)
        {
            recordHintText.text = "Space を押しているあいだ録音します（離すとローカルで文字起こし → 返信）";
        }

        sherpa.numThreads = sherpaNumThreads;
        SetStatus("モデル読み込み中", true);
        bool sherpaReady = sherpa.TryInitialize();

        if (!sherpaReady)
        {
            SetStatus("sherpa 未配置", false);
            SetPanelPlaceholder(sttRequestText, sherpa.LastError);
            SetPanelPlaceholder(sttResponseText, "Docs/sherpa-onnx-setup.md を見てモデルとネイティブ lib を配置してください。");
        }
        else
        {
            SetStatus(microphoneDevice != null ? "待機中（Space で録音）" : "マイクなし", false);
            SetPanelPlaceholder(sttRequestText, "（まだ認識していません）");
            SetPanelPlaceholder(sttResponseText, "（まだ認識していません）");
        }

        SetPanelPlaceholder(llmRequestText, "（まだ送っていません）");
        SetPanelPlaceholder(llmResponseText, "（まだ応答がありません）");
        SetSending(false);
    }

    void OnDestroy()
    {
        sherpa.Dispose();
    }

    void Update()
    {
        UpdateStatusBlink();
        UpdatePushToTalk();
    }

    // 旧 Input Manager で Space の押し始め／離しを見る
    void UpdatePushToTalk()
    {
        if (isSending)
        {
            return;
        }

        if (IsTypingInSystemInstruction())
        {
            return;
        }

        if (!isRecording && Input.GetKeyDown(KeyCode.Space))
        {
            BeginRecording();
            return;
        }

        if (!isRecording)
        {
            return;
        }

        if (Time.time - recordingStartedTime >= maxRecordingSeconds)
        {
            EndRecordingAndSend();
            return;
        }

        if (Input.GetKeyUp(KeyCode.Space))
        {
            EndRecordingAndSend();
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

    // ----- マイク録音（押し話し） -----

    void SetupMicrophone()
    {
        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            microphoneDevice = null;
            Debug.LogWarning("[SpeechToTextSherpa] マイクデバイスが見つかりません。");
            return;
        }

        microphoneDevice = Microphone.devices[0];
        Debug.Log("[SpeechToTextSherpa] マイクを使用します: " + microphoneDevice);
    }

    void BeginRecording()
    {
        if (microphoneDevice == null)
        {
            ShowError("マイクがありません。PC の入力デバイスと権限を確認してください。");
            return;
        }

        if (!sherpa.IsReady)
        {
            ShowError(
                sherpa.LastError != null
                    ? sherpa.LastError
                    : "sherpa が初期化されていません。Docs/sherpa-onnx-setup.md を見てください。",
                sttResponseText);
            return;
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            ShowError("APIキーがありません。Assets/Common/APIKey.txt を確認してください（Chat 用）。");
            return;
        }

        recordingClip = Microphone.Start(microphoneDevice, false, maxRecordingSeconds, sampleRate);
        if (recordingClip == null)
        {
            ShowError("Microphone.Start に失敗しました。");
            return;
        }

        isRecording = true;
        recordingStartedTime = Time.time;
        SetStatus("録音中", true);
    }

    // Space 解放: 録音を止め、float サンプルをローカル STT → Chat へ渡す
    void EndRecordingAndSend()
    {
        if (!isRecording)
        {
            return;
        }

        isRecording = false;
        float elapsed = Time.time - recordingStartedTime;

        int positionSamples = Microphone.GetPosition(microphoneDevice);
        Microphone.End(microphoneDevice);

        if (elapsed < minRecordingSeconds || positionSamples <= 0)
        {
            SetStatus("短すぎます（もう一度 Space）", false);
            recordingClip = null;
            return;
        }

        SyncSystemInstructionBeforeSend();

        float[] samples = AudioCodec.CopyClipSamples(recordingClip, positionSamples);
        recordingClip = null;
        if (samples == null || samples.Length == 0)
        {
            ShowError("録音データの切り出しに失敗しました。");
            return;
        }

        StartCoroutine(RecognizeThenChatCoroutine(samples, elapsed));
    }

    // ----- 通信本体（ローカル STT → Chat） -----

    IEnumerator RecognizeThenChatCoroutine(float[] samples, float audioSeconds)
    {
        SetSending(true);
        SetPanelPlaceholder(llmRequestText, "（STT 完了後に表示）");
        SetPanelPlaceholder(llmResponseText, "（STT 完了後に表示）");

        if (sttRequestText != null)
        {
            sttRequestText.text = BuildLocalSttRequestText(samples.Length, audioSeconds);
        }

        SetStatus("ローカル STT 中", true);
        SherpaAsrResult asr = null;
        yield return StartCoroutine(RecognizeBackgroundCoroutine(samples, value => { asr = value; }));

        if (sttResponseText != null && asr != null)
        {
            sttResponseText.text = BuildLocalSttResponseText(asr);
        }

        if (asr == null || !string.IsNullOrEmpty(asr.error))
        {
            string message = asr != null ? asr.error : "認識結果を受け取れませんでした。";
            ShowError(message, sttResponseText);
            SetSending(false);
            yield break;
        }

        string transcript = asr.text != null ? asr.text.Trim() : string.Empty;
        if (string.IsNullOrEmpty(transcript))
        {
            ShowError("文字起こし結果が空でした。日本語でもう一度話してみてください。", sttResponseText);
            SetSending(false);
            yield break;
        }

        turns.Add(new ChatTurn { role = "user", text = transcript });
        AddBubble("You", transcript, true);

        string url = GeminiKey.BuildGenerateContentUrl(modelName);
        string chatRequestJson = BuildChatRequestJson();
        if (llmRequestText != null)
        {
            llmRequestText.text = HttpDisplay.FormatRequest(url, chatRequestJson, apiKey, 0);
        }

        SetStatus("3. Request 送信中", false);
        HttpResult chatResult = new HttpResult();
        yield return StartCoroutine(PostJsonCoroutine(url, chatRequestJson, chatResult));

        if (llmResponseText != null)
        {
            llmResponseText.text = HttpDisplay.FormatResponse(chatResult.statusCode, chatResult.body);
        }

        SetStatus("応答解析中", false);
        if (!chatResult.ok)
        {
            ShowError("LLM HTTP エラー: " + chatResult.statusCode + " / " + chatResult.error, llmResponseText);
            RemoveLastTurnIfUser();
            SetSending(false);
            yield break;
        }

        string assistantText;
        if (!GeminiTextResponse.TryExtractText(chatResult.body, "[SpeechToTextSherpa]", out assistantText))
        {
            ShowError("LLM 応答 JSON からテキストを取り出せませんでした。4. Response を確認してください。", llmResponseText);
            RemoveLastTurnIfUser();
            SetSending(false);
            yield break;
        }

        turns.Add(new ChatTurn { role = "model", text = assistantText });
        AddBubble("Gemini", assistantText, false);

        SetStatus("完了（Space で録音）", false);
        SetSending(false);
    }

    // 認識をスレッドプールで回し、終わるまで待つ（メインスレッドを止めない）
    IEnumerator RecognizeBackgroundCoroutine(float[] samples, Action<SherpaAsrResult> onDone)
    {
        SherpaAsrResult result = null;
        bool done = false;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            result = sherpa.Recognize(samples, sampleRate);
            done = true;
        });

        while (!done)
        {
            yield return null;
        }

        onDone(result);
    }

    string BuildLocalSttRequestText(int sampleCount, float audioSeconds)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("engine: sherpa-onnx OfflineRecognizer");
        sb.AppendLine("model: ReazonSpeech Zipformer int8（日本語）");
        sb.AppendLine("provider: cpu");
        sb.AppendLine("numThreads: " + sherpa.numThreads);
        sb.AppendLine("sampleRate: " + sampleRate);
        sb.AppendLine("samples: " + sampleCount + " / ~" + audioSeconds.ToString("0.0") + "s");
        sb.AppendLine();
        sb.AppendLine("encoder: " + sherpa.encoderFileName);
        sb.AppendLine("decoder: " + sherpa.decoderFileName);
        sb.AppendLine("joiner: " + sherpa.joinerFileName);
        sb.AppendLine("tokens: " + sherpa.tokensFileName);
        sb.AppendLine("dir: " + sherpa.ModelDirectory);
        return sb.ToString();
    }

    static string BuildLocalSttResponseText(SherpaAsrResult asr)
    {
        StringBuilder sb = new StringBuilder();
        if (!string.IsNullOrEmpty(asr.error))
        {
            sb.AppendLine("[Error]");
            sb.Append(asr.error);
            return sb.ToString();
        }

        sb.AppendLine("elapsedMs: " + asr.elapsedMilliseconds);
        sb.AppendLine("audioSeconds: " + asr.audioSeconds.ToString("0.00"));
        sb.AppendLine("RTF: " + asr.realtimeFactor.ToString("0.000"));
        sb.AppendLine();
        sb.Append(asr.text);
        return sb.ToString();
    }

    IEnumerator PostJsonCoroutine(string url, string requestJson, HttpResult result)
    {
        byte[] bodyRaw = Encoding.UTF8.GetBytes(requestJson);
        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            request.SetRequestHeader("x-goog-api-key", apiKey);

            SetStatus("応答待ち", true);
            yield return request.SendWebRequest();

            result.statusCode = request.responseCode;
            result.body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            result.ok = request.result == UnityWebRequest.Result.Success;
            result.error = request.error;
        }
    }

    string BuildChatRequestJson()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append('{');

        string instruction = GetSystemInstructionText();
        if (!string.IsNullOrEmpty(instruction))
        {
            sb.Append("\"systemInstruction\":{\"parts\":[{\"text\":\"");
            sb.Append(GeminiJson.Escape(instruction));
            sb.Append("\"}]},");
        }

        sb.Append("\"contents\":[");
        for (int i = 0; i < turns.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            ChatTurn turn = turns[i];
            sb.Append("{\"role\":\"");
            sb.Append(GeminiJson.Escape(turn.role));
            sb.Append("\",\"parts\":[{\"text\":\"");
            sb.Append(GeminiJson.Escape(turn.text));
            sb.Append("\"}]}");
        }

        sb.Append("]}");
        return sb.ToString();
    }

    string GetSystemInstructionText()
    {
        if (systemInstructionField == null)
        {
            return string.Empty;
        }

        return systemInstructionField.text != null
            ? systemInstructionField.text.Trim()
            : string.Empty;
    }

    // ----- systemInstruction ファイル同期（2A と同型） -----

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

        string text;
        DateTime writeTimeUtc;
        if (!TryReadSystemInstructionFile(out text, out writeTimeUtc))
        {
            Debug.LogWarning("[SpeechToTextSherpa] SystemInstruction.txt がありません: " + GetSystemInstructionFilePath());
            systemInstructionField.text = string.Empty;
            systemInstructionFileWriteTimeUtc = DateTime.MinValue;
            return;
        }

        systemInstructionField.text = text;
        systemInstructionFileWriteTimeUtc = writeTimeUtc;
    }

    void ReloadSystemInstructionFromFileIfChanged()
    {
        if (systemInstructionField == null)
        {
            return;
        }

        string text;
        DateTime writeTimeUtc;
        if (!TryReadSystemInstructionFile(out text, out writeTimeUtc))
        {
            return;
        }

        if (writeTimeUtc <= systemInstructionFileWriteTimeUtc)
        {
            return;
        }

        systemInstructionField.text = text;
        systemInstructionFileWriteTimeUtc = writeTimeUtc;
    }

    bool TryReadSystemInstructionFile(out string text, out DateTime writeTimeUtc)
    {
        text = string.Empty;
        writeTimeUtc = DateTime.MinValue;
        string path = GetSystemInstructionFilePath();
        if (!File.Exists(path))
        {
            return false;
        }

        string raw = File.ReadAllText(path);
        text = raw != null ? raw : string.Empty;
        writeTimeUtc = File.GetLastWriteTimeUtc(path);
        return true;
    }

    void SaveSystemInstructionFromField()
    {
        if (systemInstructionField == null)
        {
            return;
        }

        string path = GetSystemInstructionFilePath();
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string text = systemInstructionField.text != null ? systemInstructionField.text : string.Empty;
        File.WriteAllText(path, text, new UTF8Encoding(false));
        systemInstructionFileWriteTimeUtc = File.GetLastWriteTimeUtc(path);
    }

    void OnSystemInstructionEndEdit(string _)
    {
        SaveSystemInstructionFromField();
    }

    void SyncSystemInstructionBeforeSend()
    {
        if (systemInstructionField == null)
        {
            return;
        }

        if (!systemInstructionField.isFocused)
        {
            ReloadSystemInstructionFromFileIfChanged();
        }

        SaveSystemInstructionFromField();
    }

    void LoadApiKey()
    {
        string error;
        if (!GeminiKey.TryRead(apiKeyRelativePath, out apiKey, out error))
        {
            Debug.LogError("[SpeechToTextSherpa] " + error);
            return;
        }
    }

    // ----- UI 更新 -----

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

    static void SetPanelPlaceholder(TMP_Text target, string message)
    {
        if (target != null)
        {
            target.text = message;
        }
    }

    void ShowError(string message, TMP_Text responsePanel = null)
    {
        Debug.LogError("[SpeechToTextSherpa] " + message);
        SetStatus("エラー", false);
        AddBubble("Error", message, false);
        if (responsePanel != null && !responsePanel.text.Contains(message))
        {
            responsePanel.text = responsePanel.text + "\n\n[Error]\n" + message;
        }
    }

    void RemoveLastTurnIfUser()
    {
        if (turns.Count > 0 && turns[turns.Count - 1].role == "user")
        {
            turns.RemoveAt(turns.Count - 1);
        }
    }

    void AddBubble(string speaker, string body, bool isUser)
    {
        if (messageBubblePrefab == null || messageContent == null)
        {
            Debug.LogWarning("[SpeechToTextSherpa] messageBubblePrefab または messageContent が未設定です。");
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

    void SetSending(bool sending)
    {
        isSending = sending;
        if (systemInstructionField != null)
        {
            systemInstructionField.interactable = !sending;
        }
    }
}
