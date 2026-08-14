// SpeechToJSON.cs
// 2A の音声入力と 1B の構造化出力を組み合わせた派生デモ。
// Space 押し話し → マイク録音 → WAV → STT → スキーマ付き JSON → キューブ／背景色。
//
// 上からの流れ:
//   Start → APIキー読込・マイク確認・3D プレビュー・UI初期化
//   Space 押下 → Microphone.Start（録音中）
//   Space 解放 → Microphone.End → AudioClip 切り出し → WAV バイト列 → Base64
//     → STT（generateContent + inlineData）で文字起こし
//     → 認識テキストをスキーマ付き generateContent へ
//     → 構造化 JSON をパースして色を適用
//
// 発生順（画面の 1〜4）:
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

    public string modelName = "gemini-3.1-flash-lite"; // 使う Gemini モデル名（URL の一部になる）
    public string apiKeyRelativePath = "Common/APIKey.txt"; // Assets/ からの相対パス
    public int sampleRate = 16000; // マイク録音のサンプルレート（Hz）
    public int maxRecordingSeconds = 30; // Space 押し話しの上限秒数
    public float minRecordingSeconds = 0.3f; // これより短い録音は送らない

    // ===== インスペクタ: 3D 反映先（1B と同型） =====

    public Renderer cubeRenderer; // 色を変えるキューブの Renderer
    public Camera targetCamera; // 背景色を変えるカメラ（未設定なら Camera.main）
    public RectTransform previewArea; // キューブ描画エリア（パース中心をここに合わせる）
    public float cubeRotationSpeedDegrees = 18f; // キューブの Y 回転速度（度/秒）。ゆっくり回す用

    // ===== インスペクタ: 左ペイン =====

    public TMP_Text recordHintText; // 「Space を押しているあいだ録音」などの案内
    public TMP_Text transcriptText; // 直近の認識テキスト（STT 結果）
    public TMP_Text statusText; // 待機中 / 録音中 / STT / 応答待ち などの状態

    // ===== インスペクタ: 発生順 1〜4 の可視化 =====

    public TMP_Text audioRequestText; // 1. Request - GenerateContent（Audio）
    public TMP_Text textRequestText; // 3. Request - GenerateContent（Text）
    public TMP_Text audioResponseText; // 2. Response - GenerateContent（Audio）
    public TMP_Text textResponseText; // 4. Response - GenerateContent（Text）

    // ===== 内部状態 =====

    string apiKey; // Assets/Common/APIKey.txt から読んだキー（画面には出さない）
    bool isSending; // STT〜構造化出力の処理中（二重送信・二重録音防止）
    bool isRecording; // Space 押し話しで録音中か
    string microphoneDevice; // 使うマイク名（null なら利用不可）
    AudioClip recordingClip; // Microphone.Start が書き込むクリップ
    float recordingStartedTime; // 録音開始時刻（短すぎ防止・上限判定用）
    bool statusBlink; // 応答待ちのとき Status を点滅させる
    const float StatusBlinkSpeed = 6f; // 点滅の速さ（大きいほど速い）
    const int DisplayBase64MaxChars = 96; // Request ペインで Base64 を省略表示する長さ
    Camera backgroundClearCamera; // Preview 以外を背景色で塗る（cullingMask なし）
    bool cameraViewportOwned; // このスクリプトが Camera.rect を上書き中か

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

    // JsonUtility 用: 構造化レスポンス本文
    [Serializable]
    class ColorRgb
    {
        public float r; // 赤 0〜1
        public float g; // 緑 0〜1
        public float b; // 青 0〜1
    }

    [Serializable]
    class StructuredColors
    {
        public ColorRgb cubeColor; // キューブ色
        public ColorRgb backgroundColor; // カメラ背景色
    }

    // JsonUtility 用: generateContent の外枠（テキスト抽出に必要な分だけ）
    [Serializable]
    class GeminiResponse
    {
        public GeminiCandidate[] candidates;
    }

    [Serializable]
    class GeminiCandidate
    {
        public GeminiContent content; // 最初の候補の本文
    }

    [Serializable]
    class GeminiContent
    {
        public GeminiPart[] parts; // text が入る parts
    }

    [Serializable]
    class GeminiPart
    {
        public string text; // 構造化出力時は JSON 文字列が入る
    }

    // UnityWebRequest の結果をコルーチンから受け取る入れ物
    class HttpResult
    {
        public long statusCode; // HTTP ステータスコード
        public string body; // レスポンス本文
        public bool ok; // UnityWebRequest が Success か
        public string error; // 失敗時の error 文字列
    }

    // ----- エントリポイント -----

    // 起動時: キー・マイク・3D プレビューを用意し、録音案内を出す
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
        SetPanelPlaceholder(audioRequestText, "（まだ送っていません）");
        SetPanelPlaceholder(textRequestText, "（まだ送っていません）");
        SetPanelPlaceholder(audioResponseText, "（まだ応答がありません）");
        SetPanelPlaceholder(textResponseText, "（まだ応答がありません）");

        EnsureBackgroundClearCamera();
        Canvas.ForceUpdateCanvases();
        UpdateCameraViewportToPreview();
    }

    // 無効化時: カメラ rect / 背景クリア用カメラを元に戻す
    void OnDisable()
    {
        RestoreCameraViewport();
    }

    // 応答待ちの点滅、キューブ回転、Space 押し話し
    void Update()
    {
        UpdateStatusBlink();
        RotateCube();
        UpdatePushToTalk();
    }

    // UI レイアウト後に、描画矩形を Preview へ追従させる
    void LateUpdate()
    {
        UpdateCameraViewportToPreview();
    }

    // ----- Space 押し話し（旧 Input Manager） -----

    // 旧 Input Manager で Space の押し始め／離しを見る（新 Input System API は使わない）
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

    // 利用可能なマイクを1つ選ぶ。無ければ以降の録音はエラー表示だけする
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

    // Space 押し始め: Microphone.Start で AudioClip への書き込みを開始する
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

    // Space 解放（または上限）: 録音を止め、WAV 化して STT→JSON コルーチンへ渡す
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

    // Microphone が書き込んだ先頭 positionSamples だけを新しい AudioClip にコピーする
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

    // AudioClip → WAV（ヘッダ + 16-bit PCM）。Gemini inlineData 用のバイト列を作る
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
            // RIFF / WAVE ヘッダ（PCM）
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataSize);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((ushort)1); // PCM
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

    // 音声 Base64 を STT し、認識テキストで構造化 JSON を取る一連の流れ
    IEnumerator SendSpeechPipelineCoroutine(string audioBase64, int wavByteLength, float audioSeconds)
    {
        isSending = true;
        SetPanelPlaceholder(textRequestText, "（Audio 完了後に表示）");
        SetPanelPlaceholder(textResponseText, "（Audio 完了後に表示）");

        string url = BuildGenerateContentUrl();

        // 1→2 Audio（文字起こし）
        string sttRequestJson = BuildSttRequestJson(audioBase64);
        SetPanelPlaceholder(
            audioRequestText,
            "audio/wav bytes=" + wavByteLength
            + " / ~" + audioSeconds.ToString("0.0") + "s\n\n"
            + FormatHttpRequestForDisplay(url, sttRequestJson));

        SetStatus("1. Request 送信中", false);
        HttpResult sttResult = new HttpResult();
        yield return StartCoroutine(PostJsonCoroutine(url, sttRequestJson, sttResult));
        SetPanelPlaceholder(audioResponseText, FormatHttpResponseForDisplay(sttResult.statusCode, sttResult.body));

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
        SetPanelPlaceholder(textRequestText, FormatHttpRequestForDisplay(url, structuredRequestJson));

        SetStatus("3. Request 送信中", false);
        HttpResult jsonResult = new HttpResult();
        yield return StartCoroutine(PostJsonCoroutine(url, structuredRequestJson, jsonResult));

        string structuredJson;
        if (!TryExtractText(jsonResult.body, out structuredJson))
        {
            SetPanelPlaceholder(textResponseText, FormatHttpResponseForDisplay(jsonResult.statusCode, jsonResult.body));
            ShowError(
                jsonResult.ok
                    ? "構造化 JSON を取り出せませんでした。4. Response を確認してください。"
                    : "Text HTTP エラー: " + jsonResult.statusCode + " / " + jsonResult.error,
                textResponseText);
            isSending = false;
            yield break;
        }

        SetPanelPlaceholder(
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

    // generateContent の URL を組み立てる
    string BuildGenerateContentUrl()
    {
        return "https://generativelanguage.googleapis.com/v1beta/models/"
               + modelName
               + ":generateContent";
    }

    // JSON を POST し、結果を result に書き込む
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

    // STT 用: 指示テキスト + 音声 inlineData（Base64）。履歴は載せない
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

    // candidates[0].content.parts[0].text から本文を取り出す
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

    // 構造化 JSON を読み、キューブ色とカメラ背景色へ書く
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

    // 0〜1 の r/g/b を Color にする（範囲外はクランプ）
    static Color ToColor(ColorRgb rgb)
    {
        return new Color(Mathf.Clamp01(rgb.r), Mathf.Clamp01(rgb.g), Mathf.Clamp01(rgb.b), 1f);
    }

    // ----- 3D / カメラ（1B と同型） -----

    // キューブを一定スピードで Y 軸まわりにゆっくり回す
    void RotateCube()
    {
        if (cubeRenderer == null || cubeRotationSpeedDegrees == 0f)
        {
            return;
        }

        cubeRenderer.transform.Rotate(0f, cubeRotationSpeedDegrees * Time.deltaTime, 0f, Space.World);
    }

    // Preview 以外を同じ背景色で塗るカメラを用意する（キューブ用カメラは Preview だけ描く）
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

    // previewArea の画面上の矩形に Camera.rect を合わせ、パースの中心を描画エリア中央にする
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

    // Camera.rect を全画面に戻し、背景クリア用カメラを止める
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

    // Assets/Common/APIKey.txt を1行読む（リポジトリにはコミットしない）
    void LoadApiKey()
    {
        string path = Path.Combine(Application.dataPath, apiKeyRelativePath);
        if (!File.Exists(path))
        {
            Debug.LogError("[SpeechToJSON] APIキーファイルがありません: " + path);
            apiKey = null;
            SetStatus("エラー", false);
            SetPanelPlaceholder(audioResponseText, "APIキーファイルが見つかりません:\n" + path);
            return;
        }

        apiKey = File.ReadAllText(path).Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("[SpeechToJSON] APIキーが空です: " + path);
            apiKey = null;
            SetStatus("エラー", false);
            SetPanelPlaceholder(audioResponseText, "APIキーが空です。Docs/gemini-ai-studio-setup.md を参照してください。");
        }
        else
        {
            Debug.Log("[SpeechToJSON] APIキーを読み込みました（長さ " + apiKey.Length + "）。キー自体はログに出しません。");
        }
    }

    // Status 欄の Value を日本語で更新する（タイトルはシーン側の固定文言）
    // blink=true のとき点滅（応答待ち用）
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

    // 応答待ち中だけアルファを上下させて点滅させる
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

    // プレースホルダ文言を1ペインに出す
    static void SetPanelPlaceholder(TMP_Text target, string message)
    {
        if (target != null)
        {
            target.text = message;
        }
    }

    // エラーを Status と該当レスポンス欄に出す
    void ShowError(string message, TMP_Text responsePanel)
    {
        Debug.LogError("[SpeechToJSON] " + message);
        SetStatus("エラー", false);
        if (responsePanel != null && !responsePanel.text.Contains(message))
        {
            responsePanel.text = responsePanel.text + "\n\n[Error]\n" + message;
        }
    }

    // 中央ペイン用: URL / マスク済みヘッダ / JSON（長い Base64 は表示だけ短縮）
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

    // レスポンス欄用: HTTP ステータス + 生 JSON
    string FormatHttpResponseForDisplay(long statusCode, string responseBody)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("HTTP " + statusCode);
        sb.AppendLine();
        sb.Append(string.IsNullOrEmpty(responseBody) ? "(empty body)" : PrettyPrintJson(responseBody));
        return sb.ToString();
    }

    // inlineData.data の長い Base64 を、画面では先頭だけにして読みやすくする（送信自体は全文）
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

    // 画面表示用にキーを伏せる（先頭数文字だけ残す）
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

    // JSON 文字列用の最低限のエスケープ
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

    // インデントを軽く付けて読みやすくする（厳密なパーサではない）
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
