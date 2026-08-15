// TextToJSON.cs
// Gemini の構造化出力（JSON）で、3D キューブの色とカメラ背景色を変えるデモの本体。
// 中央に Schema（形の約束）、右に構造化された Response を出し、パース結果を左の 3D に反映する。
//
// 上からの流れ:
//   Start → APIキー読込・UI初期化・Schema 表示
//   送信ボタン / Enter → StartCoroutine(SendStructuredCoroutine)
//     → リクエストJSON組み立て（generationConfig に schema）→ UnityWebRequest で POST
//     → yield で応答待ち（このあいだ Status 点滅）→ 構造化 JSON 表示 → パース → 色を適用
//
// 初期フィールドは cubeColor / backgroundColor のみ。

using System;
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// Gemini generateContent の構造化出力で色を受け取り、キューブとカメラ背景へ反映する。
/// </summary>
public class TextToJSON : MonoBehaviour
{
    // ===== インスペクタ: 設定 =====

    public string modelName = "gemini-3.1-flash-lite"; // 使う Gemini モデル名（URL の一部になる）
    public string apiKeyRelativePath = "Common/APIKey.txt"; // Assets/ からの相対パス

    // ===== インスペクタ: 3D 反映先 =====

    public Renderer cubeRenderer; // 色を変えるキューブの Renderer
    public Camera targetCamera; // 背景色を変えるカメラ（未設定なら Camera.main）
    public RectTransform previewArea; // キューブ描画エリア（パース中心をここに合わせる）
    public float cubeRotationSpeedDegrees = 18f; // キューブの Y 回転速度（度/秒）。ゆっくり回す用

    // ===== インスペクタ: 左ペイン（入力 UI） =====

    public TMP_InputField inputField; // ユーザー入力欄
    public Button sendButton; // 送信ボタン

    // ===== インスペクタ: 可視化（中央 Schema / 右 Response、Status は左下） =====

    public TMP_Text statusText; // 待機中 / 送信中 / 応答待ち などの状態
    public TMP_Text schemaText; // 返してほしい形（responseSchema）の表示（中央ペイン）
    public TMP_Text responseText; // HTTP ステータス + 構造化された JSON（右ペイン）

    // ===== 内部状態 =====

    string apiKey; // Assets/Common/APIKey.txt から読んだキー（画面には出さない）
    bool isSending; // 二重送信防止
    bool statusBlink; // 応答待ちのとき Status を点滅させる
    const float StatusBlinkSpeed = 6f; // 点滅の速さ（大きいほど速い）
    Camera backgroundClearCamera; // Preview 以外を背景色で塗る（cullingMask なし）
    bool cameraViewportOwned; // このスクリプトが Camera.rect を上書き中か

    // Gemini の構造化出力用スキーマ（中央ペインにも同じ文字列を出す）
    // type は REST でよく使う大文字表記。色は Unity Color に合わせ 0〜1 の r/g/b
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
        public string text; // 構造化出力時は JSON 文字列が入る
    }

    // ----- エントリポイント -----

    // 起動時: キー読込、ボタン購読、Schema 初期表示
    void Start()
    {
        LoadApiKey();

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
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
        ShowSchema();
        if (responseText != null)
        {
            responseText.text = "（まだ応答がありません）";
        }

        SetSending(false);

        EnsureBackgroundClearCamera();
        // レイアウト確定後に Preview 領域へカメラ描画を合わせる
        Canvas.ForceUpdateCanvases();
        UpdateCameraViewportToPreview();
    }

    // 無効化時: カメラ rect / 背景クリア用カメラを元に戻す
    void OnDisable()
    {
        RestoreCameraViewport();
    }

    // 応答待ちの点滅と、キューブの一定回転
    void Update()
    {
        UpdateStatusBlink();
        RotateCube();
    }

    // キューブを一定スピードで Y 軸まわりにゆっくり回す
    void RotateCube()
    {
        if (cubeRenderer == null || cubeRotationSpeedDegrees == 0f)
        {
            return;
        }

        cubeRenderer.transform.Rotate(0f, cubeRotationSpeedDegrees * Time.deltaTime, 0f, Space.World);
    }

    // UI レイアウト後に、描画矩形を Preview へ追従させる（パース中心＝エリア中央）
    void LateUpdate()
    {
        UpdateCameraViewportToPreview();
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
        backgroundClearCamera.cullingMask = 0; // 何も描かずクリアだけ
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

        // 一時的に全画面 rect にして pixelRect を Game View 全体として使う
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
        StartCoroutine(SendStructuredCoroutine(userText));
    }

    // ----- 通信本体（コルーチン） -----

    // ユーザー指示を送り、構造化 JSON を受け取って色へ反映する
    IEnumerator SendStructuredCoroutine(string userText)
    {
        SetSending(true);

        // 1) リクエスト組み立て（Schema ペインは常に同じ約束を再表示）
        SetStatus("リクエスト作成中", false);
        string url = "https://generativelanguage.googleapis.com/v1beta/models/"
                     + modelName
                     + ":generateContent";
        string requestJson = BuildRequestJson(userText);
        ShowSchema();

        // 2) POST 送信（ヘッダに APIキー、ボディに JSON）
        SetStatus("送信中", false);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(requestJson);
        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            // Docs と同じ認証ヘッダ。キー自体は画面に出さない
            request.SetRequestHeader("x-goog-api-key", apiKey);

            // サーバ応答待ち。このあいだ Status を点滅させる
            SetStatus("応答待ち", true);
            yield return request.SendWebRequest();

            long statusCode = request.responseCode;
            string responseBody = request.downloadHandler != null
                ? request.downloadHandler.text
                : string.Empty;

            // 3) 構造化 JSON を取り出して右ペインへ
            SetStatus("応答解析中", false);
            string structuredJson;
            if (!TryExtractStructuredJson(responseBody, out structuredJson))
            {
                ShowResponse(statusCode, responseBody, null);
                string err = request.result != UnityWebRequest.Result.Success
                    ? "HTTP エラー: " + statusCode + " / " + request.error
                    : "応答 JSON から構造化データを取り出せませんでした。Response ペインを確認してください。";
                ShowError(err);
                SetSending(false);
                yield break;
            }

            ShowResponse(statusCode, responseBody, structuredJson);

            if (request.result != UnityWebRequest.Result.Success)
            {
                ShowError("HTTP エラー: " + statusCode + " / " + request.error);
                SetSending(false);
                yield break;
            }

            // 4) パースしてキューブ色・背景色へ反映
            if (!TryParseAndApply(structuredJson))
            {
                ShowError("構造化 JSON のパース、または色の反映に失敗しました。Response ペインを確認してください。");
                SetSending(false);
                yield break;
            }

            SetStatus("完了", false);
        }

        SetSending(false);
    }

    // ----- リクエスト JSON -----

    // contents + generationConfig（responseMimeType / responseSchema）を組み立てる
    // Schema ペインに出す約束と同じ responseSchema を載せる
    string BuildRequestJson(string userText)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append('{');
        sb.Append("\"contents\":[{\"role\":\"user\",\"parts\":[{\"text\":\"");
        sb.Append(GeminiJson.Escape(userText));
        sb.Append("\"}]}],");
        sb.Append("\"generationConfig\":{");
        sb.Append("\"responseMimeType\":\"application/json\",");
        sb.Append("\"responseSchema\":");
        sb.Append(ResponseSchemaJson);
        sb.Append('}');
        sb.Append('}');
        return sb.ToString();
    }

    // ----- レスポンス解析と反映 -----

    // candidates[0].content.parts[0].text から構造化 JSON 文字列を取り出す
    bool TryExtractStructuredJson(string responseBody, out string structuredJson)
    {
        structuredJson = null;
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
            Debug.LogError("[TextToJSON] 応答 JSON 解析失敗: " + e.Message);
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

        structuredJson = first.content.parts[0].text;
        return !string.IsNullOrEmpty(structuredJson);
    }

    // 構造化 JSON を読み、キューブ色とカメラ背景色へ書く
    bool TryParseAndApply(string structuredJson)
    {
        StructuredColors data = null;
        try
        {
            data = JsonUtility.FromJson<StructuredColors>(structuredJson);
        }
        catch (Exception e)
        {
            Debug.LogError("[TextToJSON] 構造化 JSON のパース失敗: " + e.Message);
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
            // 共有マテリアルを汚さないよう material（インスタンス）側の色を変える
            cubeRenderer.material.color = cubeColor;
        }
        else
        {
            Debug.LogWarning("[TextToJSON] cubeRenderer が未設定です。");
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
            Debug.LogWarning("[TextToJSON] カメラが見つかりません。");
        }

        return true;
    }

    // 0〜1 の r/g/b を Color にする（範囲外はクランプ）
    static Color ToColor(ColorRgb rgb)
    {
        return new Color(
            Mathf.Clamp01(rgb.r),
            Mathf.Clamp01(rgb.g),
            Mathf.Clamp01(rgb.b),
            1f);
    }

    // ----- APIキー -----

    // Assets/Common/APIKey.txt を1行読む（リポジトリにはコミットしない）
    void LoadApiKey()
    {
        string error;
        if (!GeminiKey.TryRead(apiKeyRelativePath, out apiKey, out error))
        {
            Debug.LogError("[TextToJSON] " + error);
            SetStatus("エラー", false);
            if (responseText != null)
            {
                responseText.text = error;
            }

            return;
        }

        Debug.Log("[TextToJSON] APIキーを読み込みました（長さ " + apiKey.Length + "）。キー自体はログに出しません。");
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

    // 中央ペイン Schema: 返してほしい形（responseSchema）を見やすく表示
    void ShowSchema()
    {
        if (schemaText == null)
        {
            return;
        }

        schemaText.text = GeminiJson.PrettyPrint(ResponseSchemaJson);
    }

    // 右ペイン Response: HTTP ステータス + 構造化 JSON（取れなければ生ボディ）
    void ShowResponse(long statusCode, string responseBody, string structuredJson)
    {
        if (responseText == null)
        {
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("HTTP " + statusCode);
        sb.AppendLine();
        if (!string.IsNullOrEmpty(structuredJson))
        {
            sb.Append(GeminiJson.PrettyPrint(structuredJson));
        }
        else
        {
            sb.Append(string.IsNullOrEmpty(responseBody) ? "(empty body)" : GeminiJson.PrettyPrint(responseBody));
        }

        responseText.text = sb.ToString();
    }

    // エラーを Status と Response に出す（吹き出しは使わない）
    void ShowError(string message)
    {
        Debug.LogError("[TextToJSON] " + message);
        SetStatus("エラー", false);
        if (responseText != null)
        {
            if (string.IsNullOrEmpty(responseText.text) || responseText.text == "（まだ応答がありません）")
            {
                responseText.text = "[Error]\n" + message;
            }
            else if (!responseText.text.Contains(message))
            {
                responseText.text = responseText.text + "\n\n[Error]\n" + message;
            }
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
    }
}
