using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>レクチャー用ズーム。右クリックで開始/解除、ホイールで倍率、中ボタンドラッグで移動、Escで解除。</summary>
[RequireComponent(typeof(Camera)), DisallowMultipleComponent]
[DefaultExecutionOrder(10000)]
public sealed class LectureZoom : MonoBehaviour
{
    [Min(1f)] public float initialZoom = 2f;
    [Min(1f)] public float maxZoom = 5f;
    [Min(0.01f)] public float wheelSensitivity = 0.2f;

    public bool IsZoomed { get; private set; }
    public float Zoom { get; private set; } = 1f;
    Camera view;
    Vector2 anchor;
    Vector2 panOffset;
    Vector2 previousDragPosition;
    bool isDragging;
    Vector2Int screenSize;
    bool fullscreen;
    bool applied;
    Rect originalRect;
    Matrix4x4 originalProjection;
    bool originalCameraEnabled;
    readonly List<Canvas> canvases = new List<Canvas>();
    readonly List<TransformState> transforms = new List<TransformState>();
    readonly List<ScrollState> scrolls = new List<ScrollState>();

    struct TransformState
    {
        public Transform target;
        public Vector3 position, scale;
    }
    struct ScrollState
    {
        public ScrollRect target;
        public float sensitivity;
    }

    void Awake() => view = GetComponent<Camera>();

    // EventSystem は前フレームの表示位置で入力を処理する。その後に元へ戻し、
    // 各デモの LateUpdate が通常の座標でプレビューを配置できるようにする。
    void Update()
    {
        RestoreFrame();
        if (IsZoomed && (screenSize != new Vector2Int(Screen.width, Screen.height) || fullscreen != Screen.fullScreen))
            ResetZoom();
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            ResetZoom();
            return;
        }
        var mouse = Mouse.current;
        if (mouse == null) return;
        if (mouse.rightButton.wasPressedThisFrame)
        {
            if (IsZoomed) ResetZoom();
            else
            {
                Vector2 point = mouse.position.ReadValue();
                if (point.x >= 0 && point.y >= 0 && point.x < Screen.width && point.y < Screen.height)
                    BeginZoom(point);
            }
            return;
        }
        if (IsZoomed)
        {
            Vector2 position = mouse.position.ReadValue();
            if (mouse.middleButton.wasPressedThisFrame)
            {
                isDragging = position.x >= 0 && position.y >= 0 && position.x < Screen.width && position.y < Screen.height;
                previousDragPosition = position;
            }
            if (isDragging && mouse.middleButton.isPressed)
            {
                // 倍率に関係なく、画面上でマウスと同じ距離だけ移動する。
                panOffset += position - previousDragPosition;
                previousDragPosition = position;
            }
            else isDragging = false;
            float wheel = mouse.scroll.ReadValue().y;
            if (wheel != 0f)
            {
                float nextZoom = Mathf.Clamp(Zoom * Mathf.Exp(Mathf.Sign(wheel) * wheelSensitivity), 1f, Mathf.Max(1f, maxZoom));
                // 現在のマウス位置にある内容を固定して倍率を変更する。
                // 表示座標 = anchor + panOffset + Zoom * (元の座標 - anchor)
                panOffset = position - anchor + (nextZoom / Zoom) * (anchor + panOffset - position);
                Zoom = nextZoom;
            }
        }
    }

    public void BeginZoom(Vector2 screenPoint)
    {
        ResetZoom();
        if (Screen.width <= 0 || Screen.height <= 0) return;
        anchor = new Vector2(Mathf.Clamp(screenPoint.x, 0, Screen.width), Mathf.Clamp(screenPoint.y, 0, Screen.height));
        screenSize = new Vector2Int(Screen.width, Screen.height);
        fullscreen = Screen.fullScreen;
        Zoom = Mathf.Clamp(initialZoom, 1f, Mathf.Max(1f, maxZoom));
        IsZoomed = true;
        // 入れ子の Canvas は親と一緒に変換する。別シーンの UI には触れない。
        canvases.Clear();
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.gameObject.scene == gameObject.scene)
            {
                canvases.Add(canvas);
                foreach (var scroll in canvas.GetComponentsInChildren<ScrollRect>(true))
                {
                    scrolls.Add(new ScrollState { target = scroll, sensitivity = scroll.scrollSensitivity });
                    scroll.scrollSensitivity = 0f; // ズーム用ホイールで本文まで動かさない
                }
            }
    }

    void LateUpdate()
    {
        if (!IsZoomed) return;
        Canvas.ForceUpdateCanvases();
        foreach (var canvas in canvases)
        {
            if (canvas == null || !canvas.isActiveAndEnabled) continue;
            // Canvas 自体は画面サイズに駆動されるため、その直下だけを変換する。
            foreach (Transform child in canvas.transform)
            {
                transforms.Add(new TransformState { target = child, position = child.localPosition, scale = child.localScale });
                Vector3 p = child.position;
                child.position = new Vector3(anchor.x + Zoom * (p.x - anchor.x) + panOffset.x, anchor.y + Zoom * (p.y - anchor.y) + panOffset.y, p.z);
                child.localScale *= Zoom;
            }
        }
        originalRect = view.rect;
        originalProjection = view.projectionMatrix;
        originalCameraEnabled = view.enabled;
        applied = true;
        ApplyCameraZoom();
    }

    void ApplyCameraZoom()
    {
        Vector2 a = new Vector2(anchor.x / Screen.width, anchor.y / Screen.height);
        Vector2 pan = new Vector2(panOffset.x / Screen.width, panOffset.y / Screen.height);
        Rect expanded = new Rect(a + Zoom * (originalRect.position - a) + pan, originalRect.size * Zoom);
        float left = Mathf.Max(0f, expanded.xMin), bottom = Mathf.Max(0f, expanded.yMin);
        float right = Mathf.Min(1f, expanded.xMax), top = Mathf.Min(1f, expanded.yMax);
        if (right <= left || top <= bottom)
        {
            view.enabled = false;
            return;
        }
        Rect clipped = Rect.MinMaxRect(left, bottom, right, top);
        // 画面外へ出た viewport を切り取り、投影行列で元のパースを維持する。
        Matrix4x4 crop = Matrix4x4.identity;
        crop.m00 = expanded.width / clipped.width;
        crop.m11 = expanded.height / clipped.height;
        crop.m03 = 2f * (expanded.center.x - clipped.center.x) / clipped.width;
        crop.m13 = 2f * (expanded.center.y - clipped.center.y) / clipped.height;
        view.rect = clipped;
        view.projectionMatrix = crop * originalProjection;
    }

    void RestoreFrame()
    {
        foreach (var state in transforms)
            if (state.target != null)
            {
                state.target.localPosition = state.position;
                state.target.localScale = state.scale;
            }
        transforms.Clear();
        if (!applied || view == null) return;
        view.rect = originalRect;
        // 通常時は Unity の自動投影に戻し、以後の画面サイズ変更にも追従する。
        view.ResetProjectionMatrix();
        view.enabled = originalCameraEnabled;
        applied = false;
    }

    public void ResetZoom()
    {
        RestoreFrame();
        foreach (var state in scrolls)
            if (state.target != null) state.target.scrollSensitivity = state.sensitivity;
        scrolls.Clear();
        canvases.Clear();
        IsZoomed = false;
        Zoom = 1f;
        panOffset = Vector2.zero;
        isDragging = false;
    }

    void OnDisable() => ResetZoom();

}
