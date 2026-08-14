// TextToText.cs
// Gemini とテキストチャットするデモの本体。
// 左ペインに会話、中央に Request、右に Response の生データを出し、通信の流れを追えるようにする。
//
// 上からの流れ:
//   Start → APIキー読込・systemInstruction 読込・UI初期化
//   送信ボタン / Enter → StartCoroutine(SendChatCoroutine)
//     → 履歴に user 追加 → リクエストJSON組み立て → UnityWebRequest で POST
//     → yield で応答待ち（このあいだ Status 点滅）→ 生JSON表示 → テキスト抽出 → 履歴に model 追加
//
// 「コンテキストを送る」OFF のとき:
//   contents には今回の user だけ載せる。成功後は turns をクリア（左の吹き出しは残る）。
//
// systemInstruction（事前指示）:
//   UI 欄が空ならリクエストに載せない。値があれば contents とは別枠で送る。
//   UI と Assets/Common/SystemInstruction.txt を、起動時読込・編集確定・送信直前で同期する。

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
public class TextToText : MonoBehaviour
{
    // ===== インスペクタ: 設定 =====

    public string modelName = "gemini-3.1-flash-lite"; // 使う Gemini モデル名（URL の一部になる）
    public string apiKeyRelativePath = "Common/APIKey.txt"; // Assets/ からの相対パス
    public string systemInstructionRelativePath = "Common/SystemInstruction.txt"; // Assets/ からの相対パス（事前指示）

    // ===== インスペクタ: 左ペイン（チャット UI） =====

    public TMP_InputField systemInstructionField; // systemInstruction（事前指示）。空ならリクエストに載せない
    public TMP_InputField inputField; // ユーザー入力欄
    public Button sendButton; // 送信ボタン
    public Toggle sendContextToggle; // ON=会話履歴を contents に載せる / OFF=今回の発言だけ（学習用）
    public Transform messageContent; // バブルを並べる ScrollView の Content
    public ChatBubble messageBubblePrefab; // 1メッセージ分の Prefab
    public ScrollRect chatScrollRect; // 新着時に下端へスクロールするため

    // ===== インスペクタ: 通信可視化（中央 Request / 右 Response、Status は左下） =====

    public TMP_Text statusText; // 待機中 / 送信中 / 応答待ち などの状態
    public TMP_Text requestText; // URL・ヘッダ（キーはマスク）・リクエスト JSON（中央ペイン）
    public TMP_Text responseText; // HTTP ステータス + 生レスポンス JSON（右ペイン）

    // ===== 内部状態 =====

    string apiKey; // Assets/Common/APIKey.txt から読んだキー（画面には出さない）
    readonly List<ChatTurn> turns = new List<ChatTurn>(); // API に送る会話履歴（吹き出し表示とは別）
    bool isSending; // 二重送信防止
    DateTime systemInstructionFileWriteTimeUtc; // 最後に同期した SystemInstruction.txt の更新時刻
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
            // UI で編集が終わったらファイルへ書き戻す
            systemInstructionField.onEndEdit.AddListener(OnSystemInstructionEndEdit);
        }

        if (sendButton != null)
        {
            sendButton.onClick.AddListener(OnSendClicked);
        }

        if (inputField != null)
        {
            // Enter で送信（Shift+Enter で改行）。LineType はシーン側で MultiLineSubmit
            inputField.onSubmit.AddListener(OnMessageSubmit);
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

    // 応答待ち中だけ Status を点滅させる
    void Update()
    {
        UpdateStatusBlink();
    }

    // Message 欄で Enter（送信）されたとき
    void OnMessageSubmit(string _)
    {
        OnSendClicked();
    }

    // 送信ボタン押下 / Enter → コルーチンで API 呼び出しを始める
    void OnSendClicked()
    {
        if (isSending)
        {
            return;
        }

        // 送信直前: 外部編集の取り込みと、UI 内容のファイルへの保存
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

        // 通信はコルーチン。応答が来るまで yield で待ち、その間も画面は動く
        StartCoroutine(SendChatCoroutine(userText));
    }

    // ----- 通信本体（コルーチン） -----

    // user 文言を送り、Gemini の返答を受け取って各ペインを更新する
    // UnityWebRequest.SendWebRequest の完了まで yield するため、待ちのあいだ Status を点滅できる
    IEnumerator SendChatCoroutine(string userText)
    {
        SetSending(true);

        // 1) 会話 UI と履歴にユーザー発言を追加
        turns.Add(new ChatTurn { role = "user", text = userText });
        AddBubble("You", userText, true);

        // 2) リクエスト組み立て（中央ペインに生データを出す）
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
            // Docs と同じ認証ヘッダ。キー自体は Request ペインではマスク表示する
            request.SetRequestHeader("x-goog-api-key", apiKey);

            // サーバ応答待ち。このあいだ Status を点滅させる
            SetStatus("応答待ち", true);
            yield return request.SendWebRequest();

            long statusCode = request.responseCode;
            string responseBody = request.downloadHandler != null
                ? request.downloadHandler.text
                : string.Empty;

            // 4) 生レスポンスを右ペイン（Response）へ
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
                ShowError("応答 JSON からテキストを取り出せませんでした。Response ペインを確認してください。");
                RemoveLastTurnIfUser();
                SetSending(false);
                yield break;
            }

            turns.Add(new ChatTurn { role = "model", text = assistantText });
            AddBubble("Gemini", assistantText, false);

            // OFF のときは次の送信も単発になるよう履歴を捨てる（左の吹き出しは残す）
            if (!IsSendContextEnabled())
            {
                turns.Clear();
            }

            SetStatus("完了", false);
        }

        SetSending(false);
    }

    // ----- リクエスト JSON -----

    // 会話履歴を contents にまとめ、事前指示があれば systemInstruction を付ける
    // 形: {"systemInstruction":{"parts":[{"text":"..."}]},"contents":[...]}
    // 指示が空なら {"contents":[...]} のみ（systemInstruction キー自体を出さない）
    // 「コンテキストを送る」OFF のときは末尾の user 1件だけ（履歴を重ねない）
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

        // ON: 全ターン / OFF: 今回の user だけ（学習用に差分を見せる）
        int startIndex = 0;
        if (!IsSendContextEnabled() && turns.Count > 0)
        {
            startIndex = turns.Count - 1;
        }

        sb.Append("\"contents\":[");
        for (int i = startIndex; i < turns.Count; i++)
        {
            if (i > startIndex)
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

    // Toggle が無い／未配線なら履歴を送る。ON=コンテキストを送る
    bool IsSendContextEnabled()
    {
        return sendContextToggle == null || sendContextToggle.isOn;
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
            Debug.LogError("[TextToText] JSON 解析失敗: " + e.Message);
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

    // ----- systemInstruction ファイル同期 -----
    // UI 欄 ↔ Assets/Common/SystemInstruction.txt
    // 起動時に読込、編集確定と送信直前に保存。送信直前だけファイル側の更新も取り込む。

    // Assets/.../SystemInstruction.txt の絶対パス
    string GetSystemInstructionFilePath()
    {
        return Path.Combine(Application.dataPath, systemInstructionRelativePath);
    }

    // 起動時: ファイル → UI
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
            Debug.LogWarning("[TextToText] SystemInstruction.txt がありません: " + GetSystemInstructionFilePath());
            systemInstructionField.text = string.Empty;
            systemInstructionFileWriteTimeUtc = DateTime.MinValue;
            return;
        }

        systemInstructionField.text = text;
        systemInstructionFileWriteTimeUtc = writeTimeUtc;
        Debug.Log("[TextToText] SystemInstruction.txt を読み込みました（長さ " + text.Length + "）。");
    }

    // ファイルが更新されていれば UI へ取り込む（送信直前用）
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
        Debug.Log("[TextToText] SystemInstruction.txt の変更を UI に反映しました。");
    }

    // ファイルを読んで text / 更新時刻を返す。無いときは false
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

    // ----- APIキー -----

    // Assets/Common/APIKey.txt を1行読む（リポジトリにはコミットしない）
    void LoadApiKey()
    {
        string path = Path.Combine(Application.dataPath, apiKeyRelativePath);
        if (!File.Exists(path))
        {
            Debug.LogError("[TextToText] APIキーファイルがありません: " + path);
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
            Debug.LogError("[TextToText] APIキーが空です: " + path);
            apiKey = null;
            SetStatus("エラー", false);
            if (responseText != null)
            {
                responseText.text = "APIキーが空です。Docs/gemini-ai-studio-setup.md を参照してください。";
            }
        }
        else
        {
            Debug.Log("[TextToText] APIキーを読み込みました（長さ " + apiKey.Length + "）。キー自体はログに出しません。");
        }
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

    // 中央ペイン Request: URL / マスク済みヘッダ / JSON 本文
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
        Debug.LogError("[TextToText] " + message);
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
            Debug.LogWarning("[TextToText] messageBubblePrefab または messageContent が未設定です。");
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

        if (sendContextToggle != null)
        {
            sendContextToggle.interactable = !sending;
        }
    }

    // ----- JSON / 表示ヘルパー -----

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
