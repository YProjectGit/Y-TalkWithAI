// TextChat.cs
// Gemini とテキストチャットするデモの本体。
// 左ペインに会話、右ペインに HTTP/JSON の生データを出し、通信の流れを追えるようにする。
//
// 上からの流れ:
//   Start → APIキー読込・systemInstruction 読込・UI初期化
//   送信ボタン → 履歴に user 追加 → リクエストJSON組み立て（指示があれば systemInstruction 付き）→ POST
//   応答受信 → 生JSON表示 → テキスト抽出 → 履歴に model 追加
//
// systemInstruction は UI と Assets/Common/SystemInstruction.txt を同期する。

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// Gemini generateContent で複数ターンのテキストチャットを行い、送受信の生データを可視化する。
/// </summary>
public class TextChat : MonoBehaviour
{
    // ===== インスペクタ: 設定 =====

    public string modelName = "gemini-3.6-flash"; // 使う Gemini モデル名（URL の一部になる）
    public string apiKeyRelativePath = "Common/APIKey.txt"; // Assets/ からの相対パス
    public string systemInstructionRelativePath = "Common/SystemInstruction.txt"; // Assets/ からの相対パス（事前指示）

    // ===== インスペクタ: 左ペイン（チャット UI） =====

    public TMP_InputField systemInstructionField; // systemInstruction（事前指示）。空ならリクエストに載せない
    public TMP_InputField inputField; // ユーザー入力欄
    public Button sendButton; // 送信ボタン
    public Transform messageContent; // バブルを並べる ScrollView の Content
    public ChatBubble messageBubblePrefab; // 1メッセージ分の Prefab
    public ScrollRect chatScrollRect; // 新着時に下端へスクロールするため

    // ===== インスペクタ: 右ペイン（通信可視化） =====

    public TMP_Text statusText; // Idle / Building / Sending などの状態
    public TMP_Text requestText; // URL・ヘッダ（キーはマスク）・リクエスト JSON
    public TMP_Text responseText; // HTTP ステータス + 生レスポンス JSON

    // ===== 内部状態 =====

    string apiKey; // Assets/Common/APIKey.txt から読んだキー（画面には出さない）
    readonly List<ChatTurn> turns = new List<ChatTurn>(); // 複数ターン用の会話履歴
    bool isSending; // 二重送信防止
    DateTime systemInstructionFileWriteTimeUtc; // 最後に同期した SystemInstruction.txt の更新時刻
    float systemInstructionPollTimer; // ファイル変更ポーリング用（秒）
    const float SystemInstructionPollInterval = 0.5f; // テキスト編集の反映を見る間隔
    bool statusBlink; // 応答待ちのとき Status を点滅させる
    const float StatusBlinkSpeed = 6f; // 点滅の速さ（大きいほど速い）

    // Gemini contents の1要素（role + text）
    class ChatTurn
    {
        public string role; // "user" または "model"
        public string text; // そのターンの本文
    }

    // ----- エントリポイント -----

    // 起動時: キーと事前指示を読み、ボタンと初期表示を用意する
    void Start()
    {
        LoadApiKey();
        LoadSystemInstructionFromFile();
        if (systemInstructionField != null)
        {
            // UI で編集が終わったらファイルへ書き戻す（その場入力 ↔ txt の同期）
            systemInstructionField.onEndEdit.AddListener(OnSystemInstructionEndEdit);
        }

        if (sendButton != null)
        {
            sendButton.onClick.AddListener(OnSendClicked);
        }

        SetStatus("待機中", false);
        if (requestText != null)
        {
            requestText.text = "（まだ送信していません）";
        }

        if (responseText != null)
        {
            responseText.text = "（まだ応答がありません）";
        }

        SetSending(false);
    }

    // 応答待ちの点滅と、SystemInstruction.txt の外部編集取り込み
    void Update()
    {
        UpdateStatusBlink();

        systemInstructionPollTimer += Time.unscaledDeltaTime;
        if (systemInstructionPollTimer < SystemInstructionPollInterval)
        {
            return;
        }

        systemInstructionPollTimer = 0f;
        if (systemInstructionField != null && systemInstructionField.isFocused)
        {
            return;
        }

        ReloadSystemInstructionFromFileIfChanged();
    }

    // 送信ボタン押下 → コルーチンで API 呼び出し
    void OnSendClicked()
    {
        if (isSending)
        {
            return;
        }

        // 送信直前: ファイルの新しい編集を取り込み、UI の内容をファイルへも残す
        SyncSystemInstructionBeforeSend();

        string userText = inputField != null ? inputField.text.Trim() : string.Empty;
        if (string.IsNullOrEmpty(userText))
        {
            return;
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            ShowError("APIキーがありません。Assets/Common/APIKey.txt を確認してください。");
            return;
        }

        if (inputField != null)
        {
            inputField.text = string.Empty;
        }

        StartCoroutine(SendChatCoroutine(userText));
    }

    // ----- 通信本体 -----

    // user 文言を送り、Gemini の返答を受け取って両ペインを更新する
    IEnumerator SendChatCoroutine(string userText)
    {
        SetSending(true);

        // 1) 会話 UI と履歴にユーザー発言を追加
        turns.Add(new ChatTurn { role = "user", text = userText });
        AddBubble("You", userText, true);

        // 2) リクエスト組み立て（右ペインに生データを出す）
        SetStatus("リクエスト作成中", false);
        string url = "https://generativelanguage.googleapis.com/v1beta/models/"
                     + modelName
                     + ":generateContent";
        string requestJson = BuildRequestJson();
        ShowRequest(url, requestJson);

        // 3) POST 送信（ヘッダに APIキー、ボディに JSON）
        SetStatus("送信中", false);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(requestJson);
        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            // Docs と同じ認証ヘッダ。キー自体は右ペインではマスク表示する
            request.SetRequestHeader("x-goog-api-key", apiKey);

            // サーバ応答待ち。このあいだ Status を点滅させる
            SetStatus("応答待ち", true);
            yield return request.SendWebRequest();

            long statusCode = request.responseCode;
            string responseBody = request.downloadHandler != null
                ? request.downloadHandler.text
                : string.Empty;

            // 4) 生レスポンスを右ペインへ
            SetStatus("応答解析中", false);
            ShowResponse(statusCode, responseBody);

            if (request.result != UnityWebRequest.Result.Success)
            {
                string err = "HTTP エラー: " + statusCode + " / " + request.error;
                ShowError(err);
                // 失敗した user ターンは履歴から外し、次の送信で矛盾しないようにする
                RemoveLastTurnIfUser();
                SetSending(false);
                yield break;
            }

            // 5) candidates[0].content.parts[0].text を取り出してチャットに追加
            string assistantText;
            if (!TryExtractAssistantText(responseBody, out assistantText))
            {
                ShowError("応答 JSON からテキストを取り出せませんでした。右ペインの Response を確認してください。");
                RemoveLastTurnIfUser();
                SetSending(false);
                yield break;
            }

            turns.Add(new ChatTurn { role = "model", text = assistantText });
            AddBubble("Gemini", assistantText, false);
            SetStatus("完了", false);
        }

        SetSending(false);
    }

    // ----- リクエスト JSON -----

    // 会話履歴を contents にまとめ、事前指示があれば systemInstruction を付ける
    // 形: {"systemInstruction":{"parts":[{"text":"..."}]},"contents":[...]}
    // 指示が空なら従来どおり {"contents":[...]} のみ
    string BuildRequestJson()
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

    // ----- systemInstruction ファイル同期 -----

    // Assets/.../SystemInstruction.txt の絶対パス
    string GetSystemInstructionFilePath()
    {
        return Path.Combine(Application.dataPath, systemInstructionRelativePath);
    }

    // 起動時など: ファイル → UI
    void LoadSystemInstructionFromFile()
    {
        if (systemInstructionField == null)
        {
            return;
        }

        string path = GetSystemInstructionFilePath();
        if (!File.Exists(path))
        {
            Debug.LogWarning("[TextChat] SystemInstruction.txt がありません: " + path);
            systemInstructionField.text = string.Empty;
            systemInstructionFileWriteTimeUtc = DateTime.MinValue;
            return;
        }

        string text = File.ReadAllText(path);
        systemInstructionField.text = text != null ? text : string.Empty;
        systemInstructionFileWriteTimeUtc = File.GetLastWriteTimeUtc(path);
        Debug.Log("[TextChat] SystemInstruction.txt を読み込みました（長さ "
                  + systemInstructionField.text.Length + "）。");
    }

    // ファイルが更新されていれば UI へ取り込む（入力中は呼ばない想定）
    void ReloadSystemInstructionFromFileIfChanged()
    {
        if (systemInstructionField == null)
        {
            return;
        }

        string path = GetSystemInstructionFilePath();
        if (!File.Exists(path))
        {
            return;
        }

        DateTime writeTimeUtc = File.GetLastWriteTimeUtc(path);
        if (writeTimeUtc <= systemInstructionFileWriteTimeUtc)
        {
            return;
        }

        string text = File.ReadAllText(path);
        systemInstructionField.text = text != null ? text : string.Empty;
        systemInstructionFileWriteTimeUtc = writeTimeUtc;
        Debug.Log("[TextChat] SystemInstruction.txt の変更を UI に反映しました。");
    }

    // UI → ファイルへ保存（onEndEdit / 送信直前）
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

    // InputField の編集確定時
    void OnSystemInstructionEndEdit(string _)
    {
        SaveSystemInstructionFromField();
    }

    // 送信直前の同期: 外部編集の取り込み → UI を正としてファイルへ書き戻し
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
            Debug.LogError("[TextChat] JSON 解析失敗: " + e.Message);
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

    // JsonUtility 用のレスポンス型（必要なフィールドだけ）
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

    // ----- APIキー -----

    // Assets/Common/APIKey.txt を1行読む（リポジトリにはコミットしない）
    void LoadApiKey()
    {
        string path = Path.Combine(Application.dataPath, apiKeyRelativePath);
        if (!File.Exists(path))
        {
            Debug.LogError("[TextChat] APIキーファイルがありません: " + path);
            apiKey = null;
            SetStatus("エラー", false);
            if (responseText != null)
            {
                responseText.text = "APIキーファイルが見つかりません:\n" + path;
            }

            return;
        }

        apiKey = File.ReadAllText(path).Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("[TextChat] APIキーが空です: " + path);
            apiKey = null;
            SetStatus("エラー", false);
            if (responseText != null)
            {
                responseText.text = "APIキーが空です。Docs/gemini-ai-studio-setup.md を参照してください。";
            }
        }
        else
        {
            Debug.Log("[TextChat] APIキーを読み込みました（長さ " + apiKey.Length + "）。キー自体はログに出しません。");
        }
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

    // ----- UI 更新 -----

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

    // 右ペイン Request: URL / マスク済みヘッダ / JSON 本文
    void ShowRequest(string url, string requestJson)
    {
        if (requestText == null)
        {
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("POST " + url);
        sb.AppendLine("Content-Type: application/json; charset=utf-8");
        sb.AppendLine("x-goog-api-key: " + MaskApiKey(apiKey));
        sb.AppendLine();
        sb.Append(PrettyPrintJson(requestJson));
        requestText.text = sb.ToString();
    }

    // 右ペイン Response: ステータスコード + 生 JSON
    void ShowResponse(long statusCode, string responseBody)
    {
        if (responseText == null)
        {
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("HTTP " + statusCode);
        sb.AppendLine();
        sb.Append(string.IsNullOrEmpty(responseBody) ? "(empty body)" : PrettyPrintJson(responseBody));
        responseText.text = sb.ToString();
    }

    // チャットにエラー吹き出し、ステータスを Error に
    void ShowError(string message)
    {
        Debug.LogError("[TextChat] " + message);
        SetStatus("エラー", false);
        AddBubble("Error", message, false);
        if (responseText != null && !responseText.text.Contains(message))
        {
            responseText.text = responseText.text + "\n\n[Error]\n" + message;
        }
    }

    // 履歴の末尾が今回の user なら取り除く（失敗時の巻き戻し）
    void RemoveLastTurnIfUser()
    {
        if (turns.Count > 0 && turns[turns.Count - 1].role == "user")
        {
            turns.RemoveAt(turns.Count - 1);
        }
    }

    // 左ペインにバブルを1つ追加して下端へスクロール
    void AddBubble(string speaker, string body, bool isUser)
    {
        if (messageBubblePrefab == null || messageContent == null)
        {
            Debug.LogWarning("[TextChat] messageBubblePrefab または messageContent が未設定です。");
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

    // 送信中は入力を止め、二重送信を防ぐ
    void SetSending(bool sending)
    {
        isSending = sending;
        if (sendButton != null)
        {
            sendButton.interactable = !sending;
        }

        if (inputField != null)
        {
            inputField.interactable = !sending;
        }

        if (systemInstructionField != null)
        {
            systemInstructionField.interactable = !sending;
        }
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
