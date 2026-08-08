// SpeechToText.cs
// 1A.TextToText の派生デモ。入力だけ「Space 押し話し → マイク録音 → WAV → STT → LLM」に拡張する。
// 左ペインに会話、中央に Request、右に Response の生データを出し、通信の流れを追えるようにする。
//
// 上からの流れ:
//   Start → APIキー読込・systemInstruction 読込・マイク確認・UI初期化
//   Space 押下 → Microphone.Start（録音中）
//   Space 解放 → Microphone.End → AudioClip 切り出し → WAV バイト列 → Base64
//     → STT（generateContent + inlineData）で文字起こし
//     → 認識テキストを user として Chat（1A と同型）へ
//
// 会話コンテキストは常に送る（Option Toggle は置かない）。
//
// systemInstruction（事前指示）:
//   Chat リクエスト（2. LLM Request）にだけ載せる（STT には載せない）。空ならキーごと省略。

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// マイク録音と Gemini STT / チャットで、声入力からテキスト返答までを可視化する。
/// </summary>
public class SpeechToText : MonoBehaviour
{
    // ===== インスペクタ: 設定 =====

    public string modelName = "gemini-3.6-flash"; // 使う Gemini モデル名（URL の一部になる）
    public string apiKeyRelativePath = "Common/APIKey.txt"; // Assets/ からの相対パス
    public string systemInstructionRelativePath = "Common/SystemInstruction.txt"; // Assets/ からの相対パス（事前指示）
    public int sampleRate = 16000; // マイク録音のサンプルレート（Hz）
    public int maxRecordingSeconds = 30; // Space 押し話しの上限秒数
    public float minRecordingSeconds = 0.3f; // これより短い録音は送らない

    // ===== インスペクタ: 左ペイン（チャット UI） =====

    public TMP_InputField systemInstructionField; // systemInstruction（事前指示）。空なら Chat に載せない
    public TMP_Text recordHintText; // 「Space を押しているあいだ録音」などの案内
    public Transform messageContent; // バブルを並べる ScrollView の Content
    public ChatBubble messageBubblePrefab; // 1メッセージ分の Prefab（見た目は 1A と同型）
    public ScrollRect chatScrollRect; // 新着時に下端へスクロールするため

    // ===== インスペクタ: 通信可視化（中央 Request / 右 Response は上下2段、Status は左下） =====

    public TMP_Text statusText; // 待機中 / 録音中 / STT / 応答待ち などの状態
    public TMP_Text sttRequestText; // 1. STT Request（音声 inlineData のリクエスト）
    public TMP_Text llmRequestText; // 2. LLM Request（文字起こし後のチャットリクエスト）
    public TMP_Text sttResponseText; // 1. STT Response（文字起こしの応答）
    public TMP_Text llmResponseText; // 2. LLM Response（チャットの応答）

    // ===== 内部状態 =====

    string apiKey; // Assets/Common/APIKey.txt から読んだキー（画面には出さない）
    readonly List<ChatTurn> turns = new List<ChatTurn>(); // API に送る会話履歴（吹き出し表示とは別）
    bool isSending; // STT〜Chat の処理中（二重送信・二重録音防止）
    bool isRecording; // Space 押し話しで録音中か
    string microphoneDevice; // 使うマイク名（null なら利用不可）
    AudioClip recordingClip; // Microphone.Start が書き込むクリップ
    float recordingStartedTime; // 録音開始時刻（短すぎ防止・上限判定用）
    DateTime systemInstructionFileWriteTimeUtc; // 最後に同期した SystemInstruction.txt の更新時刻
    bool statusBlink; // 応答待ちのとき Status を点滅させる
    const float StatusBlinkSpeed = 6f; // 点滅の速さ（大きいほど速い）
    const int DisplayBase64MaxChars = 96; // Request ペインで Base64 を省略表示する長さ

    // STT 用の固定指示（音声 → テキストだけ。会話の返答は次の Chat で行う）
    const string SttPromptText =
        "この音声を日本語で文字起こししてください。前置きや説明は付けず、発話の本文だけを返してください。";

    // Gemini contents の1要素（role + text）。履歴に載せる user は必ずテキスト（音声は載せない）
    class ChatTurn
    {
        public string role; // "user" または "model"
        public string text; // そのターンの本文
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

    // 起動時: キー・事前指示・マイクを用意し、録音案内を出す
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
            recordHintText.text = "Space を押しているあいだ録音します（離すと文字起こし → 返信）";
        }

        SetStatus(microphoneDevice != null ? "待機中（Space で録音）" : "マイクなし", false);
        SetPanelPlaceholder(sttRequestText, "（まだ送っていません）");
        SetPanelPlaceholder(llmRequestText, "（まだ送っていません）");
        SetPanelPlaceholder(sttResponseText, "（まだ応答がありません）");
        SetPanelPlaceholder(llmResponseText, "（まだ応答がありません）");

        SetSending(false);
    }

    // Space 押し話しの検知と Status 点滅
    void Update()
    {
        UpdateStatusBlink();
        UpdatePushToTalk();
    }

    // 旧 Input Manager で Space の押し始め／離しを見る（新 Input System API は使わない）
    void UpdatePushToTalk()
    {
        if (isSending)
        {
            return;
        }

        // System Instruction 編集中の Space は文字入力として扱い、録音しない
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

        // 上限秒に達したら自動で止めて送信する
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

    // System Instruction 欄にフォーカスがあるか（EventSystem の選択も見る）
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

    // 利用可能なマイクを1つ選ぶ。無ければ以降の録音はエラー表示だけする
    void SetupMicrophone()
    {
        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            microphoneDevice = null;
            Debug.LogWarning("[SpeechToText] マイクデバイスが見つかりません。");
            return;
        }

        microphoneDevice = Microphone.devices[0];
        Debug.Log("[SpeechToText] マイクを使用します: " + microphoneDevice);
    }

    // Space 押し始め: Microphone.Start で AudioClip への書き込みを開始する
    void BeginRecording()
    {
        if (microphoneDevice == null)
        {
            ShowError("マイクがありません。PC の入力デバイスと権限を確認してください。");
            return;
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            ShowError("APIキーがありません。Assets/Common/APIKey.txt を確認してください。");
            return;
        }

        // loop=false: 最大秒数ぶんのクリップを用意し、そこにマイクがサンプルを書き込む
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

    // Space 解放（または上限）: 録音を止め、WAV 化して STT→Chat コルーチンへ渡す
    void EndRecordingAndSend()
    {
        if (!isRecording)
        {
            return;
        }

        isRecording = false;
        float elapsed = Time.time - recordingStartedTime;

        // マイクへの書き込みを止める。この時点の書き込み位置が「実際に録れた長さ」
        int positionSamples = Microphone.GetPosition(microphoneDevice);
        Microphone.End(microphoneDevice);

        if (elapsed < minRecordingSeconds || positionSamples <= 0)
        {
            SetStatus("短すぎます（もう一度 Space）", false);
            recordingClip = null;
            return;
        }

        // 送信直前に事前指示をファイルと同期（Chat で使う）
        SyncSystemInstructionBeforeSend();

        AudioClip trimmedClip = TrimClip(recordingClip, positionSamples);
        recordingClip = null;
        if (trimmedClip == null)
        {
            ShowError("録音データの切り出しに失敗しました。");
            return;
        }

        // float サンプル → 16-bit PCM WAV バイト列（ここが「音声データ」になる）
        SetStatus("音声データ変換中", false);
        byte[] wavBytes = ConvertAudioClipToWav(trimmedClip);
        Destroy(trimmedClip);

        if (wavBytes == null || wavBytes.Length == 0)
        {
            ShowError("WAV への変換に失敗しました。");
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

    // ----- 通信本体（STT → Chat） -----

    // 音声 Base64 を STT し、認識テキストで Chat する一連の流れ
    IEnumerator SendSpeechPipelineCoroutine(string audioBase64, int wavByteLength, float audioSeconds)
    {
        SetSending(true);

        // 新しい発話のたびに LLM 側はクリアし、STT から順に埋めていく
        SetPanelPlaceholder(llmRequestText, "（STT 完了後に表示）");
        SetPanelPlaceholder(llmResponseText, "（STT 完了後に表示）");

        string url = BuildGenerateContentUrl();

        // --- 1) STT: 音声 inlineData を送り、文字起こしテキストだけ受け取る ---
        string sttRequestJson = BuildSttRequestJson(audioBase64);
        if (sttRequestText != null)
        {
            sttRequestText.text =
                "audio/wav bytes=" + wavByteLength
                + " / ~" + audioSeconds.ToString("0.0") + "s\n\n"
                + FormatHttpRequestForDisplay(url, sttRequestJson);
        }

        SetStatus("1. STT 送信中", false);
        HttpResult sttResult = new HttpResult();
        yield return StartCoroutine(PostJsonCoroutine(url, sttRequestJson, sttResult));

        if (sttResponseText != null)
        {
            sttResponseText.text = FormatHttpResponseForDisplay(sttResult.statusCode, sttResult.body);
        }

        if (!sttResult.ok)
        {
            ShowError("STT HTTP エラー: " + sttResult.statusCode + " / " + sttResult.error, sttResponseText);
            SetSending(false);
            yield break;
        }

        string transcript;
        if (!TryExtractAssistantText(sttResult.body, out transcript))
        {
            ShowError("STT 応答から文字起こしを取り出せませんでした。1. STT Response を確認してください。", sttResponseText);
            SetSending(false);
            yield break;
        }

        transcript = transcript.Trim();
        if (string.IsNullOrEmpty(transcript))
        {
            ShowError("文字起こし結果が空でした。もう一度話してみてください。", sttResponseText);
            SetSending(false);
            yield break;
        }

        // --- 2) LLM: 認識テキストを会話履歴付きで送り、返答を得る ---
        turns.Add(new ChatTurn { role = "user", text = transcript });
        AddBubble("You", transcript, true);

        string chatRequestJson = BuildChatRequestJson();
        if (llmRequestText != null)
        {
            llmRequestText.text = FormatHttpRequestForDisplay(url, chatRequestJson);
        }

        SetStatus("2. LLM 送信中", false);
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
            ShowError("LLM 応答 JSON からテキストを取り出せませんでした。2. LLM Response を確認してください。", llmResponseText);
            RemoveLastTurnIfUser();
            SetSending(false);
            yield break;
        }

        turns.Add(new ChatTurn { role = "model", text = assistantText });
        AddBubble("Gemini", assistantText, false);

        SetStatus("完了（Space で録音）", false);
        SetSending(false);
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

    // ----- リクエスト JSON -----

    // STT 用: 指示テキスト + 音声 inlineData（Base64）。systemInstruction / 履歴は載せない
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

    // Chat 用: 会話履歴をすべて contents にまとめ、事前指示があれば付ける（コンテキストは常にON）
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

    // UI 欄の現在テキスト（トリム済み）。空ならリクエストに載せない
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

    // ----- レスポンス解析 -----

    // JsonUtility で入れ子 DTO に載せ、最初の候補テキストを返す
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
            Debug.LogError("[SpeechToText] JSON 解析失敗: " + e.Message);
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

    // ----- systemInstruction ファイル同期（1A と同型） -----

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
            Debug.LogWarning("[SpeechToText] SystemInstruction.txt がありません: " + GetSystemInstructionFilePath());
            systemInstructionField.text = string.Empty;
            systemInstructionFileWriteTimeUtc = DateTime.MinValue;
            return;
        }

        systemInstructionField.text = text;
        systemInstructionFileWriteTimeUtc = writeTimeUtc;
        Debug.Log("[SpeechToText] SystemInstruction.txt を読み込みました（長さ " + text.Length + "）。");
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
        Debug.Log("[SpeechToText] SystemInstruction.txt の変更を UI に反映しました。");
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

    // ----- APIキー -----

    void LoadApiKey()
    {
        string path = Path.Combine(Application.dataPath, apiKeyRelativePath);
        if (!File.Exists(path))
        {
            Debug.LogError("[SpeechToText] APIキーファイルがありません: " + path);
            apiKey = null;
            SetStatus("エラー", false);
            SetPanelPlaceholder(sttResponseText, "APIキーファイルが見つかりません:\n" + path);
            return;
        }

        apiKey = File.ReadAllText(path).Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("[SpeechToText] APIキーが空です: " + path);
            apiKey = null;
            SetStatus("エラー", false);
            SetPanelPlaceholder(
                sttResponseText,
                "APIキーが空です。Docs/gemini-ai-studio-setup.md を参照してください。");
        }
        else
        {
            Debug.Log("[SpeechToText] APIキーを読み込みました（長さ " + apiKey.Length + "）。キー自体はログに出しません。");
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
        string replacement = head + "…(" + length + " chars total)";
        return requestJson.Substring(0, valueStart) + replacement + requestJson.Substring(valueEnd);
    }

    // プレースホルダ文言を1ペインに出す
    static void SetPanelPlaceholder(TMP_Text target, string message)
    {
        if (target != null)
        {
            target.text = message;
        }
    }

    // チャットにエラー吹き出しを出し、該当レスポンス欄にも追記する
    void ShowError(string message, TMP_Text responsePanel = null)
    {
        Debug.LogError("[SpeechToText] " + message);
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
            Debug.LogWarning("[SpeechToText] messageBubblePrefab または messageContent が未設定です。");
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

    // 送信中は録音以外の操作を止める
    void SetSending(bool sending)
    {
        isSending = sending;
        if (systemInstructionField != null)
        {
            systemInstructionField.interactable = !sending;
        }
    }

    // ----- JSON / 表示ヘルパー -----

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
