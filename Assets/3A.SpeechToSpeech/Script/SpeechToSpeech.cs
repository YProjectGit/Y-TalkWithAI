// SpeechToSpeech.cs
// 2A.SpeechToText の派生デモ。STT → LLM のあとに TTS（声の出口）と再生を足す。
// 左ペインに会話、中央に Request、右に Response の生データを出し、通信の流れを追えるようにする。
//
// 上からの流れ:
//   Start → APIキー読込・systemInstruction 読込・マイク確認・AudioSource・UI初期化
//   Space 押下 → Microphone.Start（録音中）
//   Space 解放 → Microphone.End → AudioClip 切り出し → WAV バイト列 → Base64
//     → 1→2 STT（generateContent + inlineData）で文字起こし
//     → 3→4 Chat（1A と同型）で返答テキスト
//     → 5→6 TTS（generateContent + responseModalities AUDIO）で音声 → AudioSource 再生
//
// 会話コンテキストは常に送る（Option Toggle は置かない）。
//
// systemInstruction（事前指示）:
//   Chat リクエスト（3. Request - GenerateContent（Text））にだけ載せる。空ならキーごと省略。
// TTS は別モデル（ttsModelName）へ、返答テキストだけを渡す。

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
/// マイク録音と Gemini STT / チャット / TTS で、声入力から声の返答までを可視化する。
/// </summary>
public class SpeechToSpeech : MonoBehaviour
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
    public TMP_Text sttRequestText; // 1. Request - GenerateContent（Audio）
    public TMP_Text llmRequestText; // 3. Request - GenerateContent（Text）
    public TMP_Text ttsRequestText; // 5. Request - GenerateContent（TTS）
    public TMP_Text sttResponseText; // 2. Response - GenerateContent（Audio）
    public TMP_Text llmResponseText; // 4. Response - GenerateContent（Text）
    public TMP_Text ttsResponseText; // 6. Response - GenerateContent（TTS）

    // ===== インスペクタ: TTS / 再生 =====

    public AudioSource playbackAudioSource; // TTS 音声の再生先（未設定なら起動時に同じ GameObject へ追加）
    public string ttsModelName = "gemini-3.1-flash-tts-preview"; // TTS 専用モデル名（URL の一部）
    public string ttsVoiceName = "Kore"; // speechConfig の prebuilt 声色名
    public int ttsSampleRate = 24000; // TTS が返す PCM の想定サンプルレート（Hz）

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
    AudioClip lastTtsClip; // 直前に再生した TTS クリップ（差し替え時に破棄する）
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
        EnsurePlaybackAudioSource();
        if (recordHintText != null)
        {
            recordHintText.text = "Space を押しているあいだ録音します（離すと文字起こし → 返信 → 音声再生）";
        }

        SetStatus(microphoneDevice != null ? "待機中（Space で録音）" : "マイクなし", false);
        SetPanelPlaceholder(sttRequestText, "（まだ送っていません）");
        SetPanelPlaceholder(llmRequestText, "（まだ送っていません）");
        SetPanelPlaceholder(ttsRequestText, "（まだ送っていません）");
        SetPanelPlaceholder(sttResponseText, "（まだ応答がありません）");
        SetPanelPlaceholder(llmResponseText, "（まだ応答がありません）");
        SetPanelPlaceholder(ttsResponseText, "（まだ応答がありません）");

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
            Debug.LogWarning("[SpeechToSpeech] マイクデバイスが見つかりません。");
            return;
        }

        microphoneDevice = Microphone.devices[0];
        Debug.Log("[SpeechToSpeech] マイクを使用します: " + microphoneDevice);
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

        // 新しい発話のたびに LLM / TTS 側はクリアし、STT から順に埋めていく
        SetPanelPlaceholder(llmRequestText, "（STT 完了後に表示）");
        SetPanelPlaceholder(llmResponseText, "（STT 完了後に表示）");
        SetPanelPlaceholder(ttsRequestText, "（Chat 完了後に表示）");
        SetPanelPlaceholder(ttsResponseText, "（Chat 完了後に表示）");

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

        SetStatus("1. Request 送信中", false);
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
            ShowError("STT 応答から文字起こしを取り出せませんでした。2. Response を確認してください。", sttResponseText);
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

        // --- 3) TTS: 返答テキストを TTS モデルへ送り、AUDIO を受け取って再生する ---
        SetPanelPlaceholder(ttsRequestText, "（Chat 完了後に表示）");
        SetPanelPlaceholder(ttsResponseText, "（Chat 完了後に表示）");

        string ttsUrl = BuildTtsGenerateContentUrl();
        string ttsRequestJson = BuildTtsRequestJson(assistantText);
        if (ttsRequestText != null)
        {
            ttsRequestText.text = FormatHttpRequestForDisplay(ttsUrl, ttsRequestJson);
        }

        SetStatus("5. Request 送信中", false);
        HttpResult ttsResult = new HttpResult();
        yield return StartCoroutine(PostJsonCoroutine(ttsUrl, ttsRequestJson, ttsResult));

        if (!ttsResult.ok)
        {
            if (ttsResponseText != null)
            {
                ttsResponseText.text = FormatHttpResponseForDisplay(ttsResult.statusCode, ttsResult.body);
            }

            ShowError("TTS HTTP エラー: " + ttsResult.statusCode + " / " + ttsResult.error, ttsResponseText);
            SetSending(false);
            yield break;
        }

        string mimeType;
        string audioBase64Out;
        if (!TryExtractInlineAudio(ttsResult.body, out mimeType, out audioBase64Out))
        {
            if (ttsResponseText != null)
            {
                ttsResponseText.text = FormatHttpResponseForDisplay(ttsResult.statusCode, ttsResult.body);
            }

            ShowError("TTS 応答から音声データを取り出せませんでした。6. Response を確認してください。", ttsResponseText);
            SetSending(false);
            yield break;
        }

        byte[] pcmOrWavBytes = Convert.FromBase64String(audioBase64Out);
        if (ttsResponseText != null)
        {
            // 音声バイナリは大きいので、MIME / バイト数 / Base64 先頭だけを表示する
            ttsResponseText.text =
                "HTTP " + ttsResult.statusCode + "\n\n"
                + "mimeType: " + mimeType + "\n"
                + "audio bytes: " + pcmOrWavBytes.Length + "\n"
                + "base64 head: " + TruncateForDisplay(audioBase64Out, DisplayBase64MaxChars) + "\n\n"
                + "(本文の audio バイナリは再生に回し、ここには載せていません)";
        }

        AudioClip clip;
        string decodeError;
        if (!TryCreateAudioClipFromTtsBytes(pcmOrWavBytes, mimeType, out clip, out decodeError))
        {
            ShowError("TTS 音声のデコードに失敗: " + decodeError, ttsResponseText);
            SetSending(false);
            yield break;
        }

        PlayTtsClip(clip);
        SetStatus("再生中（完了後 Space で録音）", false);
        SetSending(false);
    }

    // チャット用 generateContent の URL を組み立てる
    string BuildGenerateContentUrl()
    {
        return "https://generativelanguage.googleapis.com/v1beta/models/"
               + modelName
               + ":generateContent";
    }

    // TTS 用 generateContent の URL（ttsModelName を使う）
    string BuildTtsGenerateContentUrl()
    {
        return "https://generativelanguage.googleapis.com/v1beta/models/"
               + ttsModelName
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


    // TTS 用: 返答テキストを読み上げさせる。responseModalities に AUDIO を指定する
    string BuildTtsRequestJson(string speakText)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append('{');
        sb.Append("\"contents\":[{\"role\":\"user\",\"parts\":[{\"text\":\"");
        // 読み上げ指示を短く添え、本文は Chat の返答そのもの
        sb.Append(EscapeJson("次の文を自然な日本語で読み上げてください。\n\n" + speakText));
        sb.Append("\"}]}],");
        sb.Append("\"generationConfig\":{");
        sb.Append("\"responseModalities\":[\"AUDIO\"],");
        sb.Append("\"speechConfig\":{");
        sb.Append("\"voiceConfig\":{");
        sb.Append("\"prebuiltVoiceConfig\":{");
        sb.Append("\"voiceName\":\"");
        sb.Append(EscapeJson(ttsVoiceName));
        sb.Append("\"}");
        sb.Append('}');
        sb.Append('}');
        sb.Append('}');
        sb.Append('}');
        return sb.ToString();
    }

    // 再生用 AudioSource を確保する（未割り当てなら同じ GameObject に付ける）
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

    // TTS で得た AudioClip を再生する（直前クリップは破棄）
    void PlayTtsClip(AudioClip clip)
    {
        EnsurePlaybackAudioSource();
        if (lastTtsClip != null)
        {
            Destroy(lastTtsClip);
            lastTtsClip = null;
        }

        lastTtsClip = clip;
        playbackAudioSource.clip = clip;
        playbackAudioSource.Play();
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
            Debug.LogError("[SpeechToSpeech] JSON 解析失敗: " + e.Message);
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
        public GeminiInlineData inlineData; // TTS 応答の音声（text と排他的なことが多い）
    }

    [Serializable]
    class GeminiInlineData
    {
        public string mimeType; // 例: audio/L16;codec=pcm;rate=24000
        public string data; // Base64 の音声バイト列
    }

    // TTS 応答 JSON から最初の inlineData（音声）を取り出す
    bool TryExtractInlineAudio(string responseBody, out string mimeType, out string audioBase64)
    {
        mimeType = null;
        audioBase64 = null;
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
            Debug.LogError("[SpeechToSpeech] TTS JSON 解析失敗: " + e.Message);
            return false;
        }

        if (parsed == null || parsed.candidates == null || parsed.candidates.Length == 0)
        {
            return false;
        }

        GeminiCandidate first = parsed.candidates[0];
        if (first == null || first.content == null || first.content.parts == null)
        {
            return false;
        }

        for (int i = 0; i < first.content.parts.Length; i++)
        {
            GeminiPart part = first.content.parts[i];
            if (part == null || part.inlineData == null)
            {
                continue;
            }

            if (string.IsNullOrEmpty(part.inlineData.data))
            {
                continue;
            }

            mimeType = part.inlineData.mimeType != null ? part.inlineData.mimeType : string.Empty;
            audioBase64 = part.inlineData.data;
            return true;
        }

        return false;
    }

    // TTS のバイト列を AudioClip にする（L16 PCM または WAV）
    bool TryCreateAudioClipFromTtsBytes(byte[] bytes, string mimeType, out AudioClip clip, out string error)
    {
        clip = null;
        error = null;
        if (bytes == null || bytes.Length < 4)
        {
            error = "バイト列が空です";
            return false;
        }

        string mime = mimeType != null ? mimeType.ToLowerInvariant() : string.Empty;

        // WAV（RIFF）ならヘッダから周波数などを読む
        if (bytes.Length >= 12 && bytes[0] == (byte)'R' && bytes[1] == (byte)'I'
            && bytes[2] == (byte)'F' && bytes[3] == (byte)'F')
        {
            return TryCreateClipFromWav(bytes, out clip, out error);
        }

        // 既定: raw PCM 16-bit little-endian mono（Gemini TTS の典型）
        int rate = ttsSampleRate > 0 ? ttsSampleRate : 24000;
        int rateFromMime = TryParseRateFromMime(mime);
        if (rateFromMime > 0)
        {
            rate = rateFromMime;
        }

        if (bytes.Length % 2 != 0)
        {
            error = "PCM バイト長が奇数です（16-bit 想定）";
            return false;
        }

        int sampleCount = bytes.Length / 2;
        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            short s = (short)(bytes[i * 2] | (bytes[i * 2 + 1] << 8));
            samples[i] = s / 32768f;
        }

        clip = AudioClip.Create("TtsPcm", sampleCount, 1, rate, false);
        clip.SetData(samples, 0);
        return true;
    }

    // MIME 文字列から rate=24000 のような値を拾う
    static int TryParseRateFromMime(string mime)
    {
        if (string.IsNullOrEmpty(mime))
        {
            return 0;
        }

        const string key = "rate=";
        int idx = mime.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0)
        {
            return 0;
        }

        int start = idx + key.Length;
        int end = start;
        while (end < mime.Length && char.IsDigit(mime[end]))
        {
            end++;
        }

        if (end <= start)
        {
            return 0;
        }

        int rate;
        if (!int.TryParse(mime.Substring(start, end - start), out rate))
        {
            return 0;
        }

        return rate;
    }

    // 最小限の WAV パーサ（PCM 16-bit）
    bool TryCreateClipFromWav(byte[] wav, out AudioClip clip, out string error)
    {
        clip = null;
        error = null;
        try
        {
            int channels = BitConverter.ToInt16(wav, 22);
            int rate = BitConverter.ToInt32(wav, 24);
            int bits = BitConverter.ToInt16(wav, 34);
            if (bits != 16)
            {
                error = "未対応の WAV bit depth: " + bits;
                return false;
            }

            // "data" チャンクを探す
            int dataIndex = -1;
            for (int i = 12; i < wav.Length - 8; i++)
            {
                if (wav[i] == (byte)'d' && wav[i + 1] == (byte)'a'
                    && wav[i + 2] == (byte)'t' && wav[i + 3] == (byte)'a')
                {
                    dataIndex = i;
                    break;
                }
            }

            if (dataIndex < 0)
            {
                error = "WAV に data チャンクがありません";
                return false;
            }

            int dataSize = BitConverter.ToInt32(wav, dataIndex + 4);
            int dataStart = dataIndex + 8;
            if (dataStart + dataSize > wav.Length)
            {
                dataSize = wav.Length - dataStart;
            }

            int sampleCount = dataSize / 2;
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                short s = BitConverter.ToInt16(wav, dataStart + i * 2);
                samples[i] = s / 32768f;
            }

            int frames = channels > 0 ? sampleCount / channels : sampleCount;
            clip = AudioClip.Create("TtsWav", frames, channels, rate, false);
            clip.SetData(samples, 0);
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    // 表示用に長い文字列を先頭だけ残す
    static string TruncateForDisplay(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
        {
            return value;
        }

        return value.Substring(0, maxChars) + "…(" + value.Length + " chars total)";
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
            Debug.LogWarning("[SpeechToSpeech] SystemInstruction.txt がありません: " + GetSystemInstructionFilePath());
            systemInstructionField.text = string.Empty;
            systemInstructionFileWriteTimeUtc = DateTime.MinValue;
            return;
        }

        systemInstructionField.text = text;
        systemInstructionFileWriteTimeUtc = writeTimeUtc;
        Debug.Log("[SpeechToSpeech] SystemInstruction.txt を読み込みました（長さ " + text.Length + "）。");
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
        Debug.Log("[SpeechToSpeech] SystemInstruction.txt の変更を UI に反映しました。");
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
            Debug.LogError("[SpeechToSpeech] APIキーファイルがありません: " + path);
            apiKey = null;
            SetStatus("エラー", false);
            SetPanelPlaceholder(sttResponseText, "APIキーファイルが見つかりません:\n" + path);
            return;
        }

        apiKey = File.ReadAllText(path).Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("[SpeechToSpeech] APIキーが空です: " + path);
            apiKey = null;
            SetStatus("エラー", false);
            SetPanelPlaceholder(
                sttResponseText,
                "APIキーが空です。Docs/gemini-ai-studio-setup.md を参照してください。");
        }
        else
        {
            Debug.Log("[SpeechToSpeech] APIキーを読み込みました（長さ " + apiKey.Length + "）。キー自体はログに出しません。");
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
        Debug.LogError("[SpeechToSpeech] " + message);
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
            Debug.LogWarning("[SpeechToSpeech] messageBubblePrefab または messageContent が未設定です。");
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
