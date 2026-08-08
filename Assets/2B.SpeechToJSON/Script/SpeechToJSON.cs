// SpeechToJSON.cs
// 2A（Space 押し話し → Audio generateContent）と 1B（スキーマ付き JSON → 色反映）を組み合わせた派生デモ。
//
// 発生順:
//   1. Request  - GenerateContent（Audio） … 音声 inlineData
//   2. Response - GenerateContent（Audio） … 文字起こし
//   3. Request  - GenerateContent（Text）  … responseSchema 付き
//   4. Response - GenerateContent（Text）  … 構造化 JSON → キューブ／背景色

using System;
using System.Collections;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// マイク発話を文字起こしし、構造化 JSON でキューブ色と背景色を更新する。
/// </summary>
public class SpeechToJSON : MonoBehaviour
{
    // ===== インスペクタ: 設定 =====

    public string modelName = "gemini-3.6-flash"; // 使う Gemini モデル名
    public string apiKeyRelativePath = "Common/APIKey.txt"; // Assets/ からの相対パス
    public int sampleRate = 16000; // マイク録音のサンプルレート（Hz）
    public int maxRecordingSeconds = 30; // Space 押し話しの上限秒数
    public float minRecordingSeconds = 0.3f; // これより短い録音は送らない

    // ===== インスペクタ: 3D 反映先（1B と同型） =====

    public Renderer cubeRenderer; // 色を変えるキューブの Renderer
    public Camera targetCamera; // 背景色を変えるカメラ（未設定なら Camera.main）
    public RectTransform previewArea; // キューブ描画エリア（パース中心をここに合わせる）
    public float cubeRotationSpeedDegrees = 18f; // キューブの Y 回転速度（度/秒）

    // ===== インスペクタ: 左ペイン =====

    public TMP_Text recordHintText; // Space 押し話しの案内
    public TMP_Text transcriptText; // 直近の認識テキスト
    public TMP_Text statusText; // 待機中 / 録音中 / 送信中 など

    // ===== インスペクタ: 発生順 1〜4 の可視化 =====

    public TMP_Text audioRequestText; // 1. Request - GenerateContent（Audio）
    public TMP_Text textRequestText; // 3. Request - GenerateContent（Text）
    public TMP_Text audioResponseText; // 2. Response - GenerateContent（Audio）
    public TMP_Text textResponseText; // 4. Response - GenerateContent（Text）

    // ===== 内部状態 =====

    string apiKey;
    bool isSending;
    bool isRecording;
    string microphoneDevice;
    AudioClip recordingClip;
    float recordingStartedTime;
    bool statusBlink;
    const float StatusBlinkSpeed = 6f;
    const int DisplayBase64MaxChars = 96;
    Camera backgroundClearCamera;
    bool cameraViewportOwned;

    // STT 用の固定指示（Audio 段。返答の色 JSON は次の Text 段で取る）
    const string SttPromptText =
        "この音声を日本語で文字起こししてください。前置きや説明は付けず、発話の本文だけを返してください。";

    // 1B と同型。r/g/b は NUMBER（0〜1）
    const string ResponseSchemaJson =
        "{"
        + "\"type\":\"OBJECT\","
        + "\"properties\":{"
        + "\"cubeColor\":{"
        + "\"type\":\"OBJECT\","
        + "\"description\":\"Color of the 3D cube. Each of r,g,b is 0 to 1.\","
        + "\"properties\":{"
        + "\"r\":{\"type\":\"NUMBER\"},"
        + "\"g\":{\"type\":\"NUMBER\"},"
        + "\"b\":{\"type\":\"NUMBER\"}"
        + "},"
        + "\"required\":[\"r\",\"g\",\"b\"]"
        + "},"
        + "\"backgroundColor\":{"
        + "\"type\":\"OBJECT\","
        + "\"description\":\"Camera background color. Each of r,g,b is 0 to 1.\","
        + "\"properties\":{"
        + "\"r\":{\"type\":\"NUMBER\"},"
        + "\"g\":{\"type\":\"NUMBER\"},"
        + "\"b\":{\"type\":\"NUMBER\"}"
        + "},"
        + "\"required\":[\"r\",\"g\",\"b\"]"
        + "}"
        + "},"
        + "\"required\":[\"cubeColor\",\"backgroundColor\"]"
        + "}";

    [Serializable]
    class ColorRgb
    {
        public float r;
        public float g;
        public float b;
    }

    [Serializable]
    class StructuredColors
    {
        public ColorRgb cubeColor;
        public ColorRgb backgroundColor;
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
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        SetupMicrophone();
        if (recordHintText != null)
        {
            recordHintText.text = "Space を押しているあいだ録音します（離すと色が変わります）";
        }

        if (transcriptText != null)
        {
            transcriptText.text = "（まだ認識していません）";
        }

        // 認識欄として使う InputField があれば編集不可にする
        TMP_InputField input = transcriptText != null
            ? transcriptText.GetComponentInParent<TMP_InputField>()
            : null;
        if (input != null)
        {
            input.interactable = false;
            input.readOnly = true;
        }

        SetStatus(microphoneDevice != null ? "待機中（Space で録音）" : "マイクなし", false);
        SetPanel(audioRequestText, "（まだ送っていません）");
        SetPanel(textRequestText, "（まだ送っていません）");
        SetPanel(audioResponseText, "（まだ応答がありません）");
        SetPanel(textResponseText, "（まだ応答がありません）");

        EnsureBackgroundClearCamera();
        Canvas.ForceUpdateCanvases();
        UpdateCameraViewportToPreview();
    }

    void OnDisable()
    {
        RestoreCameraViewport();
    }

    void Update()
    {
        UpdateStatusBlink();
        RotateCube();
        UpdatePushToTalk();
    }

    void LateUpdate()
    {
        UpdateCameraViewportToPreview();
    }

    // ----- Space 押し話し（旧 Input Manager） -----

    void UpdatePushToTalk()
    {
        if (isSending)
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

    void SetupMicrophone()
    {
        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            microphoneDevice = null;
            Debug.LogWarning("[SpeechToJSON] マイクデバイスが見つかりません。");
            return;
        }

        microphoneDevice = Microphone.devices[0];
        Debug.Log("[SpeechToJSON] マイクを使用します: " + microphoneDevice);
    }

    void BeginRecording()
    {
        if (microphoneDevice == null)
        {
            ShowError("マイクがありません。PC の入力デバイスと権限を確認してください。", audioResponseText);
            return;
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            ShowError("APIキーがありません。Assets/Common/APIKey.txt を確認してください。", audioResponseText);
            return;
        }

        recordingClip = Microphone.Start(microphoneDevice, false, maxRecordingSeconds, sampleRate);
        if (recordingClip == null)
        {
            ShowError("Microphone.Start に失敗しました。", audioResponseText);
            return;
        }

        isRecording = true;
        recordingStartedTime = Time.time;
        SetStatus("録音中", true);
    }

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

        AudioClip trimmedClip = TrimClip(recordingClip, positionSamples);
        recordingClip = null;
        if (trimmedClip == null)
        {
            ShowError("録音データの切り出しに失敗しました。", audioResponseText);
            return;
        }

        SetStatus("音声データ変換中", false);
        byte[] wavBytes = ConvertAudioClipToWav(trimmedClip);
        Destroy(trimmedClip);
        if (wavBytes == null || wavBytes.Length == 0)
        {
            ShowError("WAV への変換に失敗しました。", audioResponseText);
            return;
        }

        string audioBase64 = Convert.ToBase64String(wavBytes);
        StartCoroutine(SendSpeechPipelineCoroutine(audioBase64, wavBytes.Length, elapsed));
    }

    AudioClip TrimClip(AudioClip source, int positionSamples)
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

        AudioClip trimmed = AudioClip.Create(
            "RecordingTrimmed",
            copySamples,
            channels,
            source.frequency,
            false);
        trimmed.SetData(data, 0);
        return trimmed;
    }

    byte[] ConvertAudioClipToWav(AudioClip clip)
    {
        if (clip == null)
        {
            return null;
        }

        int sampleCount = clip.samples * clip.channels;
        float[] samples = new float[sampleCount];
        clip.GetData(samples, 0);

        short[] pcm = new short[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float clamped = Mathf.Clamp(samples[i], -1f, 1f);
            pcm[i] = (short)Mathf.RoundToInt(clamped * short.MaxValue);
        }

        int byteRate = clip.frequency * clip.channels * 2;
        int dataSize = pcm.Length * 2;
        using (MemoryStream stream = new MemoryStream(44 + dataSize))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataSize);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((ushort)1);
            writer.Write((ushort)clip.channels);
            writer.Write(clip.frequency);
            writer.Write(byteRate);
            writer.Write((ushort)(clip.channels * 2));
            writer.Write((ushort)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataSize);
            for (int i = 0; i < pcm.Length; i++)
            {
                writer.Write(pcm[i]);
            }

            return stream.ToArray();
        }
    }

    // ----- 通信: 1→2 Audio / 3→4 Text(JSON) -----

    IEnumerator SendSpeechPipelineCoroutine(string audioBase64, int wavByteLength, float audioSeconds)
    {
        isSending = true;
        SetPanel(textRequestText, "（Audio 完了後に表示）");
        SetPanel(textResponseText, "（Audio 完了後に表示）");

        string url = BuildGenerateContentUrl();

        // 1→2 Audio（文字起こし）
        string sttRequestJson = BuildSttRequestJson(audioBase64);
        SetPanel(
            audioRequestText,
            "audio/wav bytes=" + wavByteLength
            + " / ~" + audioSeconds.ToString("0.0") + "s\n\n"
            + FormatHttpRequestForDisplay(url, sttRequestJson));

        SetStatus("1. Request 送信中", false);
        HttpResult sttResult = new HttpResult();
        yield return StartCoroutine(PostJsonCoroutine(url, sttRequestJson, sttResult));
        SetPanel(audioResponseText, FormatHttpResponseForDisplay(sttResult.statusCode, sttResult.body));

        if (!sttResult.ok)
        {
            ShowError("Audio HTTP エラー: " + sttResult.statusCode + " / " + sttResult.error, audioResponseText);
            isSending = false;
            yield break;
        }

        string transcript;
        if (!TryExtractText(sttResult.body, out transcript))
        {
            ShowError("文字起こしを取り出せませんでした。2. Response を確認してください。", audioResponseText);
            isSending = false;
            yield break;
        }

        transcript = transcript.Trim();
        if (string.IsNullOrEmpty(transcript))
        {
            ShowError("文字起こし結果が空でした。もう一度話してみてください。", audioResponseText);
            isSending = false;
            yield break;
        }

        if (transcriptText != null)
        {
            transcriptText.text = transcript;
        }

        // 3→4 Text（スキーマ付き構造化出力）
        string structuredRequestJson = BuildStructuredRequestJson(transcript);
        SetPanel(textRequestText, FormatHttpRequestForDisplay(url, structuredRequestJson));

        SetStatus("3. Request 送信中", false);
        HttpResult jsonResult = new HttpResult();
        yield return StartCoroutine(PostJsonCoroutine(url, structuredRequestJson, jsonResult));

        string structuredJson;
        if (!TryExtractText(jsonResult.body, out structuredJson))
        {
            SetPanel(textResponseText, FormatHttpResponseForDisplay(jsonResult.statusCode, jsonResult.body));
            ShowError(
                jsonResult.ok
                    ? "構造化 JSON を取り出せませんでした。4. Response を確認してください。"
                    : "Text HTTP エラー: " + jsonResult.statusCode + " / " + jsonResult.error,
                textResponseText);
            isSending = false;
            yield break;
        }

        SetPanel(
            textResponseText,
            "HTTP " + jsonResult.statusCode + "\n\n" + PrettyPrintJson(structuredJson));

        if (!jsonResult.ok)
        {
            ShowError("Text HTTP エラー: " + jsonResult.statusCode + " / " + jsonResult.error, textResponseText);
            isSending = false;
            yield break;
        }

        if (!TryParseAndApply(structuredJson))
        {
            ShowError("構造化 JSON のパース、または色の反映に失敗しました。", textResponseText);
            isSending = false;
            yield break;
        }

        SetStatus("完了（Space で録音）", false);
        isSending = false;
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

    string BuildSttRequestJson(string audioBase64)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("{\"contents\":[{\"role\":\"user\",\"parts\":[");
        sb.Append("{\"text\":\"");
        sb.Append(EscapeJson(SttPromptText));
        sb.Append("\"},");
        sb.Append("{\"inlineData\":{\"mimeType\":\"audio/wav\",\"data\":\"");
        sb.Append(audioBase64);
        sb.Append("\"}}");
        sb.Append("]}]}");
        return sb.ToString();
    }

    // 1B と同型: contents + generationConfig.responseSchema（rgb は NUMBER）
    string BuildStructuredRequestJson(string userText)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append('{');
        sb.Append("\"contents\":[{\"role\":\"user\",\"parts\":[{\"text\":\"");
        sb.Append(EscapeJson(userText));
        sb.Append("\"}]}],");
        sb.Append("\"generationConfig\":{");
        sb.Append("\"responseMimeType\":\"application/json\",");
        sb.Append("\"responseSchema\":");
        sb.Append(ResponseSchemaJson);
        sb.Append('}');
        sb.Append('}');
        return sb.ToString();
    }

    bool TryExtractText(string responseBody, out string text)
    {
        text = null;
        if (string.IsNullOrEmpty(responseBody))
        {
            return false;
        }

        GeminiResponse parsed;
        try
        {
            parsed = JsonUtility.FromJson<GeminiResponse>(responseBody);
        }
        catch (Exception e)
        {
            Debug.LogError("[SpeechToJSON] JSON 解析失敗: " + e.Message);
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

        text = first.content.parts[0].text;
        return !string.IsNullOrEmpty(text);
    }

    bool TryParseAndApply(string structuredJson)
    {
        StructuredColors data;
        try
        {
            data = JsonUtility.FromJson<StructuredColors>(structuredJson);
        }
        catch (Exception e)
        {
            Debug.LogError("[SpeechToJSON] 構造化 JSON のパース失敗: " + e.Message);
            return false;
        }

        if (data == null || data.cubeColor == null || data.backgroundColor == null)
        {
            return false;
        }

        Color cubeColor = ToColor(data.cubeColor);
        Color backgroundColor = ToColor(data.backgroundColor);

        if (cubeRenderer != null)
        {
            cubeRenderer.material.color = cubeColor;
        }
        else
        {
            Debug.LogWarning("[SpeechToJSON] cubeRenderer が未設定です。");
        }

        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = backgroundColor;
            if (backgroundClearCamera != null)
            {
                backgroundClearCamera.backgroundColor = backgroundColor;
            }
        }
        else
        {
            Debug.LogWarning("[SpeechToJSON] カメラが見つかりません。");
        }

        return true;
    }

    static Color ToColor(ColorRgb rgb)
    {
        return new Color(Mathf.Clamp01(rgb.r), Mathf.Clamp01(rgb.g), Mathf.Clamp01(rgb.b), 1f);
    }

    // ----- 3D / カメラ（1B と同型） -----

    void RotateCube()
    {
        if (cubeRenderer == null || cubeRotationSpeedDegrees == 0f)
        {
            return;
        }

        cubeRenderer.transform.Rotate(0f, cubeRotationSpeedDegrees * Time.deltaTime, 0f, Space.World);
    }

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

    // ----- APIキー / UI -----

    void LoadApiKey()
    {
        string path = Path.Combine(Application.dataPath, apiKeyRelativePath);
        if (!File.Exists(path))
        {
            Debug.LogError("[SpeechToJSON] APIキーファイルがありません: " + path);
            apiKey = null;
            SetStatus("エラー", false);
            SetPanel(audioResponseText, "APIキーファイルが見つかりません:\n" + path);
            return;
        }

        apiKey = File.ReadAllText(path).Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("[SpeechToJSON] APIキーが空です: " + path);
            apiKey = null;
            SetStatus("エラー", false);
            SetPanel(audioResponseText, "APIキーが空です。Docs/gemini-ai-studio-setup.md を参照してください。");
        }
        else
        {
            Debug.Log("[SpeechToJSON] APIキーを読み込みました（長さ " + apiKey.Length + "）。");
        }
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

    static void SetPanel(TMP_Text target, string message)
    {
        if (target != null)
        {
            target.text = message;
        }
    }

    void ShowError(string message, TMP_Text responsePanel)
    {
        Debug.LogError("[SpeechToJSON] " + message);
        SetStatus("エラー", false);
        if (responsePanel != null && !responsePanel.text.Contains(message))
        {
            responsePanel.text = responsePanel.text + "\n\n[Error]\n" + message;
        }
    }

    string FormatHttpRequestForDisplay(string url, string requestJson)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("POST " + url);
        sb.AppendLine("Content-Type: application/json; charset=utf-8");
        sb.AppendLine("x-goog-api-key: " + MaskApiKey(apiKey));
        sb.AppendLine();
        sb.Append(PrettyPrintJson(TruncateBase64ForDisplay(requestJson)));
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

    static string TruncateBase64ForDisplay(string requestJson)
    {
        if (string.IsNullOrEmpty(requestJson))
        {
            return requestJson;
        }

        const string marker = "\"data\":\"";
        int dataIndex = requestJson.IndexOf(marker, StringComparison.Ordinal);
        if (dataIndex < 0)
        {
            return requestJson;
        }

        int valueStart = dataIndex + marker.Length;
        int valueEnd = requestJson.IndexOf('"', valueStart);
        if (valueEnd < 0)
        {
            return requestJson;
        }

        int length = valueEnd - valueStart;
        if (length <= DisplayBase64MaxChars)
        {
            return requestJson;
        }

        string head = requestJson.Substring(valueStart, DisplayBase64MaxChars);
        return requestJson.Substring(0, valueStart)
               + head + "…(" + length + " chars total)"
               + requestJson.Substring(valueEnd);
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
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
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
