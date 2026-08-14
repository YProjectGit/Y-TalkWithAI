// TextToImage.cs
// Gemini にテキスト（プロンプト）を送り、返ってきた画像を左に出すデモの本体。
// 中央に Request、右に Response の生データを出し、通信の流れを追えるようにする。
//
// 上からの流れ:
//   Start → APIキー読込・UI 初期化
//   送信ボタン / Enter → StartCoroutine(SendImageCoroutine)
//     → リクエストJSON組み立て（responseModalities に IMAGE）→ UnityWebRequest で POST
//     → yield で応答待ち（このあいだ Status 点滅）→ 生JSON表示
//     → inlineData の Base64 を Texture2D にして左に表示
//
// 会話履歴は送らない（毎回、いまのプロンプトだけ）。続きの編集は 7.ImageToImage。

using System;
using System.Collections;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Gemini generateContent でテキストから画像を1枚生成し、送受信の生データを可視化する。
/// </summary>
public class TextToImage : MonoBehaviour
{
    // ===== インスペクタ: 設定 =====

    public string modelName = "gemini-3.1-flash-image"; // 使う画像生成モデル名（URL の一部になる）
    public string apiKeyRelativePath = "Common/APIKey.txt"; // Assets/ からの相対パス
    public string aspectRatio = "1:1"; // generationConfig.imageConfig.aspectRatio
    public string imageSize = "1K"; // generationConfig.imageConfig.imageSize（512 / 1K / 2K / 4K）
    public TMP_FontAsset uiFont; // 日本語 UI 用フォント（未設定だと欠ける）

    // ===== インスペクタ: 左ペイン（結果＋入力） =====

    public RawImage resultImage; // 生成画像の表示先
    public TMP_Text emptyHintText; // まだ画像が無いときの案内
    public TMP_Text captionText; // 応答のテキスト part（あれば）
    public TMP_InputField inputField; // プロンプト入力
    public Button sendButton; // 送信ボタン
    public TMP_Text statusText; // 待機中 / 送信中 / 応答待ち などの状態

    // ===== インスペクタ: 可視化（中央 Request / 右 Response） =====

    public TMP_Text requestText; // URL・ヘッダ（キーはマスク）・リクエスト JSON
    public TMP_Text responseText; // HTTP ステータス + 要約 + 短縮した生 JSON

    // ===== 内部状態 =====

    string apiKey; // Assets/Common/APIKey.txt から読んだキー（画面には出さない）
    bool isSending; // 二重送信防止
    bool statusBlink; // 応答待ちのとき Status を点滅させる
    Texture2D generatedTexture; // いま左に出している生成画像（再送信で差し替え）
    Sprite uiSprite; // 実行時に作る白いスプライト

    const float StatusBlinkSpeed = 6f; // 点滅の速さ（大きいほど速い）
    const int DisplayBase64MaxChars = 96; // 画面では Base64 をこの長さまで

    static readonly Color BackgroundColor = new Color(0.91f, 0.90f, 0.88f, 1f);
    static readonly Color PaneColor = new Color(0.97f, 0.96f, 0.94f, 1f);
    static readonly Color TitleColor = new Color(0.18f, 0.18f, 0.20f, 1f);
    static readonly Color BodyTextColor = new Color(0.16f, 0.16f, 0.18f, 1f);
    static readonly Color MutedTextColor = new Color(0.42f, 0.41f, 0.39f, 1f);
    static readonly Color ButtonColor = new Color(0.22f, 0.28f, 0.38f, 1f);
    static readonly Color ImageWellColor = new Color(0.86f, 0.85f, 0.82f, 1f);
    static readonly Color InputColor = new Color(1f, 1f, 1f, 1f);

    // ----- エントリポイント -----

    // 起動時: キーを読み、UI と初期表示を用意する
    void Start()
    {
        LoadApiKey();
        EnsureUi();
        WireInput();
        ShowEmptyPreview();

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

    // 実行時スプライトと生成テクスチャを解放する
    void OnDestroy()
    {
        ReleaseGeneratedTexture();
        if (uiSprite != null)
        {
            Destroy(uiSprite);
            uiSprite = null;
        }
    }

    // 送信ボタン / Enter を購読する
    void WireInput()
    {
        if (sendButton != null)
        {
            sendButton.onClick.AddListener(OnSendClicked);
        }

        if (inputField != null)
        {
            inputField.onSubmit.AddListener(OnPromptSubmit);
        }
    }

    // Message 欄で Enter（送信）されたとき
    void OnPromptSubmit(string _)
    {
        OnSendClicked();
    }

    // 送信の入口。空のプロンプトと二重送信は無視する
    void OnSendClicked()
    {
        if (isSending)
        {
            return;
        }

        string prompt = GetPromptText();
        if (string.IsNullOrEmpty(prompt))
        {
            SetStatus("プロンプトを入力してください", false);
            return;
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            ShowError("APIキーが空です。Docs/gemini-ai-studio-setup.md を参照してください。");
            return;
        }

        StartCoroutine(SendImageCoroutine(prompt));
    }

    // ----- 通信本体（コルーチン） -----

    // プロンプトを送り、Gemini の画像を受け取って各ペインを更新する
    // UnityWebRequest.SendWebRequest の完了まで yield するため、待ちのあいだ Status を点滅できる
    IEnumerator SendImageCoroutine(string prompt)
    {
        SetSending(true);

        SetStatus("リクエスト作成中", false);
        string url = "https://generativelanguage.googleapis.com/v1beta/models/"
                     + modelName
                     + ":generateContent";
        string requestJson = BuildRequestJson(prompt);
        ShowRequest(url, requestJson);

        SetStatus("送信中", false);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(requestJson);
        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            // Docs と同じ認証ヘッダ。キー自体は Request ペインではマスク表示する
            request.SetRequestHeader("x-goog-api-key", apiKey);

            SetStatus("応答待ち", true);
            yield return request.SendWebRequest();

            long statusCode = request.responseCode;
            string responseBody = request.downloadHandler != null
                ? request.downloadHandler.text
                : string.Empty;

            SetStatus("応答解析中", false);
            ShowResponse(statusCode, responseBody);

            if (request.result != UnityWebRequest.Result.Success)
            {
                ShowError("HTTP エラー: " + statusCode + " / " + request.error);
                SetSending(false);
                yield break;
            }

            string mimeType;
            string imageBase64;
            string caption;
            if (!TryExtractInlineImage(responseBody, out mimeType, out imageBase64, out caption))
            {
                ShowError("応答 JSON から画像を取り出せませんでした。Response ペインを確認してください。");
                SetSending(false);
                yield break;
            }

            if (!TryShowGeneratedImage(imageBase64, mimeType))
            {
                SetSending(false);
                yield break;
            }

            SetCaption(caption);
            SetStatus("完了", false);
        }

        SetSending(false);
    }

    // ----- リクエスト JSON -----

    // いまのプロンプト1件と、画像を返す設定を載せる
    // 形: {"contents":[...],"generationConfig":{"responseModalities":["TEXT","IMAGE"],"imageConfig":{...}}}
    string BuildRequestJson(string prompt)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("{\"contents\":[{\"role\":\"user\",\"parts\":[{\"text\":\"");
        sb.Append(EscapeJson(prompt));
        sb.Append("\"}]}],\"generationConfig\":{");
        sb.Append("\"responseModalities\":[\"TEXT\",\"IMAGE\"]");

        // 画面には出さないが、Request ペインで縦横比と解像度が追えるように載せる
        if (!string.IsNullOrEmpty(aspectRatio) || !string.IsNullOrEmpty(imageSize))
        {
            sb.Append(",\"imageConfig\":{");
            bool wrote = false;
            if (!string.IsNullOrEmpty(aspectRatio))
            {
                sb.Append("\"aspectRatio\":\"");
                sb.Append(EscapeJson(aspectRatio));
                sb.Append('"');
                wrote = true;
            }

            if (!string.IsNullOrEmpty(imageSize))
            {
                if (wrote)
                {
                    sb.Append(',');
                }

                sb.Append("\"imageSize\":\"");
                sb.Append(EscapeJson(imageSize));
                sb.Append('"');
            }

            sb.Append('}');
        }

        sb.Append("}}");
        return sb.ToString();
    }

    // ----- レスポンス解析 -----

    // JsonUtility で入れ子 DTO に載せ、最初の画像 part とテキスト part を返す
    bool TryExtractInlineImage(
        string responseBody,
        out string mimeType,
        out string imageBase64,
        out string caption)
    {
        mimeType = null;
        imageBase64 = null;
        caption = null;
        if (string.IsNullOrEmpty(responseBody))
        {
            return false;
        }

        // REST は camelCase。公式例の snake_case が来ても読めるようにキーだけ揃える
        string normalized = NormalizeInlineDataKeys(responseBody);
        GeminiResponse parsed = null;
        try
        {
            parsed = JsonUtility.FromJson<GeminiResponse>(normalized);
        }
        catch (Exception e)
        {
            Debug.LogError("[TextToImage] JSON 解析失敗: " + e.Message);
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
            if (part == null)
            {
                continue;
            }

            if (caption == null && !string.IsNullOrEmpty(part.text))
            {
                caption = part.text.Trim();
            }

            if (imageBase64 != null || part.inlineData == null || string.IsNullOrEmpty(part.inlineData.data))
            {
                continue;
            }

            mimeType = part.inlineData.mimeType != null ? part.inlineData.mimeType : string.Empty;
            imageBase64 = part.inlineData.data;
        }

        return !string.IsNullOrEmpty(imageBase64);
    }

    // inline_data / mime_type を JsonUtility が読める名前に揃える
    static string NormalizeInlineDataKeys(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return json;
        }

        return json.Replace("\"inline_data\"", "\"inlineData\"").Replace("\"mime_type\"", "\"mimeType\"");
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
        public GeminiInlineData inlineData; // 画像（text と並ぶことがある）
    }

    [Serializable]
    class GeminiInlineData
    {
        public string mimeType; // 例: image/png
        public string data; // Base64 の画像バイト列
    }

    // Base64 を Texture2D にして左の RawImage に載せる
    bool TryShowGeneratedImage(string imageBase64, string mimeType)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(imageBase64);
        }
        catch (Exception e)
        {
            ShowError("画像の Base64 をデコードできませんでした: " + e.Message);
            return false;
        }

        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(bytes))
        {
            Destroy(texture);
            ShowError("画像バイトを Texture2D にできませんでした（mime=" + mimeType + "）。");
            return false;
        }

        ReleaseGeneratedTexture();
        generatedTexture = texture;
        if (resultImage != null)
        {
            resultImage.texture = generatedTexture;
            resultImage.color = Color.white;
        }

        if (emptyHintText != null)
        {
            emptyHintText.gameObject.SetActive(false);
        }

        return true;
    }

    void ReleaseGeneratedTexture()
    {
        if (generatedTexture == null)
        {
            return;
        }

        if (resultImage != null && resultImage.texture == generatedTexture)
        {
            resultImage.texture = null;
        }

        Destroy(generatedTexture);
        generatedTexture = null;
    }

    // ----- APIキー -----

    // Assets/Common/APIKey.txt を1行読む（リポジトリにはコミットしない）
    void LoadApiKey()
    {
        string path = Path.Combine(Application.dataPath, apiKeyRelativePath);
        if (!File.Exists(path))
        {
            Debug.LogError("[TextToImage] APIキーファイルがありません: " + path);
            apiKey = null;
            return;
        }

        apiKey = File.ReadAllText(path).Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("[TextToImage] APIキーが空です: " + path);
            apiKey = null;
        }
        else
        {
            Debug.Log("[TextToImage] APIキーを読み込みました（長さ " + apiKey.Length + "）。キー自体はログに出しません。");
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

    // 右ペイン: HTTP 番号、mime / バイト数、短縮した JSON
    void ShowResponse(long statusCode, string responseBody)
    {
        if (responseText == null)
        {
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("HTTP " + statusCode);

        string mimeType;
        string imageBase64;
        string caption;
        bool hasImage = TryExtractInlineImage(responseBody, out mimeType, out imageBase64, out caption);
        if (hasImage)
        {
            int byteLength = 0;
            try
            {
                byteLength = Convert.FromBase64String(imageBase64).Length;
            }
            catch (Exception)
            {
                byteLength = 0;
            }

            sb.AppendLine("mimeType: " + mimeType);
            sb.AppendLine("image bytes: " + byteLength);
            sb.AppendLine("text part: " + (!string.IsNullOrEmpty(caption) ? "yes" : "no"));
        }

        sb.AppendLine();
        sb.Append(string.IsNullOrEmpty(responseBody)
            ? "(empty body)"
            : PrettyPrintJson(TruncateAllBase64ForDisplay(responseBody)));
        responseText.text = sb.ToString();
    }

    void ShowError(string message)
    {
        Debug.LogError("[TextToImage] " + message);
        SetStatus("エラー", false);
        if (responseText != null && !responseText.text.Contains(message))
        {
            responseText.text = responseText.text + "\n\n[Error]\n" + message;
        }
    }

    void SetCaption(string caption)
    {
        if (captionText == null)
        {
            return;
        }

        captionText.text = caption != null ? caption : string.Empty;
    }

    void ShowEmptyPreview()
    {
        if (resultImage != null)
        {
            resultImage.texture = null;
            resultImage.color = ImageWellColor;
        }

        if (emptyHintText != null)
        {
            emptyHintText.gameObject.SetActive(true);
            emptyHintText.text = "まだ画像がありません";
        }

        SetCaption(string.Empty);
    }

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
    }

    string GetPromptText()
    {
        if (inputField == null || inputField.text == null)
        {
            return string.Empty;
        }

        return inputField.text.Trim();
    }

    // ----- 体験 UI（未配線なら Play 時に組む） -----

    // 3分割（結果 / Request / Response）が無ければ、その場で作る
    void EnsureUi()
    {
        EnsureEventSystem();

        if (Camera.main != null)
        {
            Camera.main.backgroundColor = BackgroundColor;
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
        }

        if (resultImage != null && inputField != null && sendButton != null
            && statusText != null && requestText != null && responseText != null)
        {
            return;
        }

        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasGo = new GameObject(
                "Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            RectTransform canvasRt = canvasGo.GetComponent<RectTransform>();
            StretchFull(canvasRt);
            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        RectTransform root = canvas.transform as RectTransform;
        Image bg = canvas.GetComponent<Image>();
        if (bg == null)
        {
            bg = canvas.gameObject.AddComponent<Image>();
        }

        bg.sprite = GetUiSprite();
        bg.color = BackgroundColor;
        bg.raycastTarget = false;

        CreateTmp(root, "Title", "6.TextToImage", 26, TitleColor, TextAlignmentOptions.MidlineLeft,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -16f), new Vector2(-24f, 48f), new Vector2(0f, 1f));

        RectTransform left = CreatePane(root, "LeftPane", new Vector2(0f, 0f), new Vector2(0.34f, 1f),
            new Vector2(16f, 16f), new Vector2(-8f, -72f));
        RectTransform center = CreatePane(root, "CenterPane", new Vector2(0.34f, 0f), new Vector2(0.67f, 1f),
            new Vector2(8f, 16f), new Vector2(-8f, -72f));
        RectTransform right = CreatePane(root, "RightPane", new Vector2(0.67f, 0f), new Vector2(1f, 1f),
            new Vector2(8f, 16f), new Vector2(-16f, -72f));

        BuildLeftPane(left);
        requestText = BuildLogPane(center, "Request");
        responseText = BuildLogPane(right, "Response");
    }

    void BuildLeftPane(RectTransform pane)
    {
        CreateTmp(pane, "LeftTitle", "結果", 20, TitleColor, TextAlignmentOptions.MidlineLeft,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -10f), new Vector2(-16f, 32f), new Vector2(0f, 1f));

        GameObject wellGo = new GameObject("ImageWell", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        wellGo.transform.SetParent(pane, false);
        RectTransform wellRt = wellGo.GetComponent<RectTransform>();
        wellRt.anchorMin = new Vector2(0f, 0f);
        wellRt.anchorMax = new Vector2(1f, 1f);
        wellRt.offsetMin = new Vector2(16f, 168f);
        wellRt.offsetMax = new Vector2(-16f, -48f);
        Image well = wellGo.GetComponent<Image>();
        well.sprite = GetUiSprite();
        well.color = ImageWellColor;
        well.raycastTarget = false;

        GameObject imageGo = new GameObject("ResultImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        imageGo.transform.SetParent(wellRt, false);
        RectTransform imageRt = imageGo.GetComponent<RectTransform>();
        StretchFull(imageRt);
        imageRt.offsetMin = new Vector2(8f, 8f);
        imageRt.offsetMax = new Vector2(-8f, -8f);
        resultImage = imageGo.GetComponent<RawImage>();
        resultImage.texture = null;
        resultImage.color = ImageWellColor;

        emptyHintText = CreateTmp(wellRt, "EmptyHint", "まだ画像がありません", 20, MutedTextColor,
            TextAlignmentOptions.Center, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        emptyHintText.enableWordWrapping = true;

        captionText = CreateTmp(pane, "Caption", string.Empty, 16, MutedTextColor, TextAlignmentOptions.TopLeft,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(16f, 124f), new Vector2(-16f, 36f), new Vector2(0f, 0f));
        captionText.enableWordWrapping = true;
        captionText.overflowMode = TextOverflowModes.Ellipsis;

        inputField = CreatePromptInput(pane);
        sendButton = CreateSendButton(pane);
        statusText = CreateTmp(pane, "Status", "待機中", 16, MutedTextColor, TextAlignmentOptions.MidlineLeft,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(132f, 16f), new Vector2(-16f, 36f), new Vector2(0f, 0f));
    }

    TMP_Text BuildLogPane(RectTransform pane, string title)
    {
        CreateTmp(pane, title + "Title", title, 20, TitleColor, TextAlignmentOptions.MidlineLeft,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -10f), new Vector2(-16f, 32f), new Vector2(0f, 1f));

        GameObject scrollGo = new GameObject(
            title + "Scroll", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
        scrollGo.transform.SetParent(pane, false);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchorMin = Vector2.zero;
        scrollRt.anchorMax = Vector2.one;
        scrollRt.offsetMin = new Vector2(12f, 12f);
        scrollRt.offsetMax = new Vector2(-12f, -44f);
        Image scrollBg = scrollGo.GetComponent<Image>();
        scrollBg.sprite = GetUiSprite();
        scrollBg.color = InputColor;
        scrollBg.raycastTarget = true;

        GameObject contentGo = new GameObject(title + "Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(ContentSizeFitter));
        contentGo.transform.SetParent(scrollRt, false);
        RectTransform contentRt = contentGo.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0f, 0f);
        contentRt.offsetMin = new Vector2(10f, contentRt.offsetMin.y);
        contentRt.offsetMax = new Vector2(-10f, contentRt.offsetMax.y);

        TextMeshProUGUI tmp = contentGo.GetComponent<TextMeshProUGUI>();
        ApplyFont(tmp);
        tmp.fontSize = 15;
        tmp.color = BodyTextColor;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;

        ContentSizeFitter fitter = contentGo.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.content = contentRt;
        scroll.viewport = scrollRt;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        return tmp;
    }

    TMP_InputField CreatePromptInput(RectTransform parent)
    {
        GameObject go = new GameObject("PromptInput", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 60f);
        rt.sizeDelta = new Vector2(-32f, 56f);

        Image image = go.GetComponent<Image>();
        image.sprite = GetUiSprite();
        image.color = InputColor;

        GameObject viewportGo = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        viewportGo.transform.SetParent(go.transform, false);
        RectTransform viewportRt = viewportGo.GetComponent<RectTransform>();
        StretchFull(viewportRt);
        viewportRt.offsetMin = new Vector2(10f, 6f);
        viewportRt.offsetMax = new Vector2(-10f, -6f);

        TMP_Text placeholder = CreateTmp(viewportRt, "Placeholder", "赤い自転車が公園に停まっている、昼の写真", 16,
            new Color(0.55f, 0.54f, 0.52f, 1f), TextAlignmentOptions.MidlineLeft,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        placeholder.fontStyle = FontStyles.Italic;
        placeholder.raycastTarget = false;

        TMP_Text text = CreateTmp(viewportRt, "Text", string.Empty, 16, BodyTextColor, TextAlignmentOptions.MidlineLeft,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = true;

        TMP_InputField field = go.GetComponent<TMP_InputField>();
        field.textViewport = viewportRt;
        field.textComponent = text;
        field.placeholder = placeholder;
        field.fontAsset = uiFont;
        field.lineType = TMP_InputField.LineType.MultiLineSubmit;
        field.lineLimit = 3;
        field.pointSize = 16;
        return field;
    }

    Button CreateSendButton(RectTransform parent)
    {
        GameObject go = new GameObject("SendButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(16f, 16f);
        rt.sizeDelta = new Vector2(108f, 36f);

        Image image = go.GetComponent<Image>();
        image.sprite = GetUiSprite();
        image.color = ButtonColor;

        Button button = go.GetComponent<Button>();
        CreateTmp(rt, "Label", "送信", 18, Color.white, TextAlignmentOptions.Center,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        return button;
    }

    RectTransform CreatePane(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        Image image = go.GetComponent<Image>();
        image.sprite = GetUiSprite();
        image.color = PaneColor;
        image.raycastTarget = false;
        return rt;
    }

    void EnsureEventSystem()
    {
        if (EventSystem.current != null)
        {
            return;
        }

        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<InputSystemUIInputModule>();
    }

    TMP_Text CreateTmp(
        RectTransform parent,
        string name,
        string text,
        float fontSize,
        Color color,
        TextAlignmentOptions align,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPos,
        Vector2 sizeDelta,
        Vector2 pivot)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;

        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        ApplyFont(tmp);
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = align;
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        return tmp;
    }

    void ApplyFont(TMP_Text tmp)
    {
        if (uiFont != null)
        {
            tmp.font = uiFont;
        }
    }

    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    Sprite GetUiSprite()
    {
        if (uiSprite != null)
        {
            return uiSprite;
        }

        Texture2D tex = Texture2D.whiteTexture;
        uiSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 4f);
        return uiSprite;
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

    // 応答 JSON 内の長い Base64 を、画面では先頭だけにする（送信自体は全文）
    static string TruncateAllBase64ForDisplay(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return json;
        }

        const string marker = "\"data\":\"";
        StringBuilder sb = new StringBuilder(json.Length);
        int searchFrom = 0;
        while (searchFrom < json.Length)
        {
            int dataIndex = json.IndexOf(marker, searchFrom, StringComparison.Ordinal);
            if (dataIndex < 0)
            {
                sb.Append(json.Substring(searchFrom));
                break;
            }

            int valueStart = dataIndex + marker.Length;
            int valueEnd = json.IndexOf('"', valueStart);
            if (valueEnd < 0)
            {
                sb.Append(json.Substring(searchFrom));
                break;
            }

            sb.Append(json.Substring(searchFrom, valueStart - searchFrom));
            int length = valueEnd - valueStart;
            if (length <= DisplayBase64MaxChars)
            {
                sb.Append(json.Substring(valueStart, length));
            }
            else
            {
                sb.Append(json.Substring(valueStart, DisplayBase64MaxChars));
                sb.Append("…(");
                sb.Append(length);
                sb.Append(" chars total)");
            }

            searchFrom = valueEnd;
        }

        return sb.ToString();
    }
}
