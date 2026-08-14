// SpeechToTextLocal.cs
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
public class SpeechToTextLocal : MonoBehaviour
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
            Debug.LogWarning("[SpeechToTextLocal] マイクデバイスが見つかりません。");
            return;
        }

        microphoneDevice = Microphone.devices[0];
        Debug.Log("[SpeechToTextLocal] マイクを使用します: " + microphoneDevice);
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

        float[] samples = CopyClipSamples(recordingClip, positionSamples);
        recordingClip = null;
        if (samples == null || samples.Length == 0)
        {
            ShowError("録音データの切り出しに失敗しました。");
            return;
        }

        StartCoroutine(RecognizeThenChatCoroutine(samples, elapsed));
    }

    // Microphone が書き込んだ先頭 positionSamples を float[] にする（WAV 化はしない）
    float[] CopyClipSamples(AudioClip source, int positionSamples)
    {
        if (source == null || positionSamples <= 0)
        {
            return null;
        }

        int channels = source.channels;
        int copySamples = Mathf.Min(positionSamples, source.samples);
        float[] data = new float[copySamples * channels];
        if (!source.GetData(data, 0))
        {
            return null;
        }

        if (channels <= 1)
        {
            return data;
        }

        // sherpa はモノラル想定。複数チャネルなら平均して 1ch にする
        float[] mono = new float[copySamples];
        for (int i = 0; i < copySamples; i++)
        {
            float sum = 0f;
            for (int c = 0; c < channels; c++)
            {
                sum += data[i * channels + c];
            }

            mono[i] = sum / channels;
        }

        return mono;
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

        string url = BuildGenerateContentUrl();
        string chatRequestJson = BuildChatRequestJson();
        if (llmRequestText != null)
        {
            llmRequestText.text = FormatHttpRequestForDisplay(url, chatRequestJson);
        }

        SetStatus("3. Request 送信中", false);
        HttpResult chatResult = new HttpResult();
        yield return StartCoroutine(PostJsonCoroutine(url, chatRequestJson, chatResult));

        if (llmResponseText != null)
        {
            llmResponseText.text = FormatHttpResponseForDisplay(chatResult.statusCode, chatResult.body);
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
        if (!TryExtractAssistantText(chatResult.body, out assistantText))
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

    string BuildGenerateContentUrl()
    {
        return "https://generativelanguage.googleapis.com/v1beta/models/"
               + modelName
               + ":generateContent";
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
            sb.Append(EscapeJson(instruction));
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
            sb.Append(EscapeJson(turn.role));
            sb.Append("\",\"parts\":[{\"text\":\"");
            sb.Append(EscapeJson(turn.text));
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

    bool TryExtractAssistantText(string responseBody, out string assistantText)
    {
        assistantText = null;
        if (string.IsNullOrEmpty(responseBody))
        {
            return false;
        }

        GeminiResponse parsed = null;
        try
        {
            parsed = JsonUtility.FromJson<GeminiResponse>(responseBody);
        }
        catch (Exception e)
        {
            Debug.LogError("[SpeechToTextLocal] JSON 解析失敗: " + e.Message);
            return false;
        }

        if (parsed == null || parsed.candidates == null || parsed.candidates.Length == 0)
        {
            return false;
        }

        GeminiCandidate first = parsed.candidates[0];
        if (first == null || first.content == null || first.content.parts == null || first.content.parts.Length == 0)
        {
            return false;
        }

        assistantText = first.content.parts[0].text;
        return !string.IsNullOrEmpty(assistantText);
    }

    [Serializable]
    class GeminiResponse
    {
        public GeminiCandidate[] candidates;
    }

    [Serializable]
    class GeminiCandidate
    {
        public GeminiContent content;
    }

    [Serializable]
    class GeminiContent
    {
        public GeminiPart[] parts;
    }

    [Serializable]
    class GeminiPart
    {
        public string text;
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
            Debug.LogWarning("[SpeechToTextLocal] SystemInstruction.txt がありません: " + GetSystemInstructionFilePath());
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
        string path = Path.Combine(Application.dataPath, apiKeyRelativePath);
        if (!File.Exists(path))
        {
            Debug.LogError("[SpeechToTextLocal] APIキーファイルがありません: " + path);
            apiKey = null;
            return;
        }

        apiKey = File.ReadAllText(path).Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("[SpeechToTextLocal] APIキーが空です: " + path);
            apiKey = null;
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

    string FormatHttpRequestForDisplay(string url, string requestJson)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("POST " + url);
        sb.AppendLine("Content-Type: application/json; charset=utf-8");
        sb.AppendLine("x-goog-api-key: " + MaskApiKey(apiKey));
        sb.AppendLine();
        sb.Append(PrettyPrintJson(requestJson));
        return sb.ToString();
    }

    string FormatHttpResponseForDisplay(long statusCode, string responseBody)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("HTTP " + statusCode);
        sb.AppendLine();
        sb.Append(string.IsNullOrEmpty(responseBody) ? "(empty body)" : PrettyPrintJson(responseBody));
        return sb.ToString();
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
        Debug.LogError("[SpeechToTextLocal] " + message);
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
            Debug.LogWarning("[SpeechToTextLocal] messageBubblePrefab または messageContent が未設定です。");
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
