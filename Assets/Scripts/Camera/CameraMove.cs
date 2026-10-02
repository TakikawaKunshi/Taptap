using UnityEngine;

public enum Camera_State
{
    FreeMove,
    BirdView
}

/// <summary>
/// 2D 正交相机：背景为 XY 平面上的矩形，支持 World Space RectTransform 或 SpriteRenderer。
/// Bird View 保持画面比例并完整显示背景；背景与屏幕比例不同时会留边。
/// </summary>
[RequireComponent(typeof(Camera))]
[DisallowMultipleComponent]
[AddComponentMenu("Camera/Camera Move")]
public class CameraMove : MonoBehaviour
{
    [Tooltip("固定背景：World Space 画布/矩形 UI，或带 SpriteRenderer 的矩形背景。")]
    public GameObject BackGround;

    [SerializeField] private Camera_State initialState = Camera_State.FreeMove;
    [SerializeField, Min(0.001f)] private float minimumSize = 1f;
    [SerializeField, Min(0.01f)] private float zoomSensitivity = 0.15f;
    [SerializeField, Min(0.01f)] private float zoomSmoothTime = 0.15f;
    [SerializeField, Min(0.01f)] private float transitionDuration = 0.6f;

    public Camera_State State { get; private set; } = Camera_State.FreeMove;
    public bool IsTransitioning => transitioning;

    private struct View
    {
        public Vector3 position;
        public Quaternion rotation;
        public float size;
    }

    private Camera controlledCamera;
    private readonly Vector3[] corners = new Vector3[4];
    private Vector3 backgroundCenter;
    private Vector3 backgroundRight;
    private Vector3 backgroundUp;
    private float halfWidth;
    private float halfHeight;
    private View savedFreeView;
    private View transitionStart;
    private float transitionElapsed;
    private float targetSize;
    private float zoomVelocity;
    private Vector3 previousMousePosition;
    private bool dragging;
    private bool initialized;
    private bool transitioning;
    private string lastWarning;

    private void Awake()
    {
        controlledCamera = GetComponent<Camera>();
    }

    private void LateUpdate()
    {
        if (!TryReadBackground())
        {
            dragging = false;
            return;
        }

        InitializeView();

        if (transitioning)
        {
            UpdateTransition();
        }
        else if (State == Camera_State.BirdView)
        {
            ApplyView(GetBirdView());
        }
        else
        {
            UpdateFreeMove();
        }
    }

    /// <summary>外部调用可切换状态；过渡期间再次调用，会从当前画面平滑转向新目标。</summary>
    public void ChangeCameraState()
    {
        if (!isActiveAndEnabled || !TryReadBackground())
            return;

        InitializeView();
        if (State == Camera_State.FreeMove)
        {
            // 返回途中再次切换时，不能用中间画面覆盖上一次真正的 Free Move 视角。
            if (!transitioning)
                savedFreeView = ClampFreeView(CaptureView());

            State = Camera_State.BirdView;
        }
        else
        {
            State = Camera_State.FreeMove;
        }

        BeginTransition();
    }

    private void InitializeView()
    {
        if (initialized)
            return;

        savedFreeView = ClampFreeView(CaptureView());
        ApplyView(savedFreeView);
        targetSize = savedFreeView.size;
        State = initialState;
        initialized = true;
        if (State == Camera_State.BirdView)
            BeginTransition();
    }

    private void BeginTransition()
    {
        transitionStart = CaptureView();
        transitionElapsed = 0f;
        zoomVelocity = 0f;
        dragging = false;
        transitioning = true;
    }

    private void UpdateTransition()
    {
        transitionElapsed += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(transitionElapsed / Mathf.Max(0.01f, transitionDuration));
        float blend = Mathf.SmoothStep(0f, 1f, progress);
        View destination = State == Camera_State.BirdView
            ? GetBirdView()
            : ClampFreeView(savedFreeView);

        // 过渡期间允许经过全景范围，结束后 Free Move 才恢复严格的边界约束。
        ApplyView(new View
        {
            position = Vector3.Lerp(transitionStart.position, destination.position, blend),
            rotation = Quaternion.Slerp(transitionStart.rotation, destination.rotation, blend),
            size = Mathf.Lerp(transitionStart.size, destination.size, blend)
        });

        if (progress >= 1f)
        {
            ApplyView(destination);
            targetSize = destination.size;
            transitioning = false;
        }
    }

    private void UpdateFreeMove()
    {
        View view = CaptureView();
        float maximumSize = GetMaximumFreeSize(view.rotation);
        float lowerLimit = Mathf.Min(Mathf.Max(0.001f, minimumSize), maximumSize);
        targetSize = Mathf.Clamp(targetSize, lowerLimit, maximumSize);

        float scroll = Input.mouseScrollDelta.y;
        if (scroll != 0f)
        {
            float scale = Mathf.Exp(Mathf.Clamp(-scroll * zoomSensitivity, -10f, 10f));
            targetSize = Mathf.Clamp(targetSize * scale, lowerLimit, maximumSize);
        }

        Vector3 mousePosition = Input.mousePosition;
        if (Input.GetMouseButton(2) && Application.isFocused)
        {
            if (dragging)
            {
                Vector3 delta = mousePosition - previousMousePosition;
                float unitsPerPixel = 2f * view.size / Mathf.Max(1, controlledCamera.pixelHeight);
                // 背景跟随鼠标移动，因此相机向鼠标位移的反方向移动。
                view.position -= (transform.right * delta.x + transform.up * delta.y) * unitsPerPixel;
            }
            dragging = true;
        }
        else
        {
            dragging = false;
        }
        previousMousePosition = mousePosition;

        view.size = Mathf.SmoothDamp(view.size, targetSize, ref zoomVelocity,
            Mathf.Max(0.01f, zoomSmoothTime), Mathf.Infinity, Time.unscaledDeltaTime);
        // 每帧按实际显示范围限制位置，缩放至边缘时也不会露出背景外的内容。
        ApplyView(ClampFreeView(view));
    }

    private View GetBirdView()
    {
        return new View
        {
            position = new Vector3(backgroundCenter.x, backgroundCenter.y, savedFreeView.position.z),
            rotation = Quaternion.LookRotation(Vector3.forward, backgroundUp),
            size = Mathf.Max(halfHeight, halfWidth / controlledCamera.aspect)
        };
    }

    private Vector2 GetViewExtents(Quaternion rotation)
    {
        Vector3 right = rotation * Vector3.right;
        Vector3 up = rotation * Vector3.up;
        float aspect = controlledCamera.aspect;
        return new Vector2(
            Mathf.Abs(Vector3.Dot(right, backgroundRight)) * aspect + Mathf.Abs(Vector3.Dot(up, backgroundRight)),
            Mathf.Abs(Vector3.Dot(right, backgroundUp)) * aspect + Mathf.Abs(Vector3.Dot(up, backgroundUp)));
    }

    private float GetMaximumFreeSize(Quaternion rotation)
    {
        Vector2 extents = GetViewExtents(rotation);
        return Mathf.Min(halfWidth / extents.x, halfHeight / extents.y);
    }

    private View ClampFreeView(View view)
    {
        float maximumSize = GetMaximumFreeSize(view.rotation);
        float lowerLimit = Mathf.Min(Mathf.Max(0.001f, minimumSize), maximumSize);
        view.size = Mathf.Clamp(view.size, lowerLimit, maximumSize);
        Vector2 extents = GetViewExtents(view.rotation) * view.size;
        float limitX = Mathf.Max(0f, halfWidth - extents.x);
        float limitY = Mathf.Max(0f, halfHeight - extents.y);
        Vector3 offset = view.position - backgroundCenter;
        float x = Vector3.Dot(offset, backgroundRight);
        float y = Vector3.Dot(offset, backgroundUp);
        view.position += backgroundRight * (Mathf.Clamp(x, -limitX, limitX) - x)
                       + backgroundUp * (Mathf.Clamp(y, -limitY, limitY) - y);
        return view;
    }

    private View CaptureView()
    {
        return new View
        {
            position = transform.position,
            rotation = transform.rotation,
            size = controlledCamera.orthographicSize
        };
    }

    private void ApplyView(View view)
    {
        transform.SetPositionAndRotation(view.position, view.rotation);
        controlledCamera.orthographicSize = view.size;
    }

    private bool TryReadBackground()
    {
        if (controlledCamera == null)
            controlledCamera = GetComponent<Camera>();
        if (!controlledCamera.orthographic || Vector3.Dot(transform.forward, Vector3.forward) < 0.99999f)
            return Warn("需要使用朝向世界 +Z 的 2D 正交相机，允许绕 Z 轴旋转。");
        if (BackGround == null)
            return Warn("请将固定背景拖入 BackGroud。");
        if (BackGround.transform.IsChildOf(transform))
            return Warn("BackGroud 不能是相机的子物体，否则背景会随相机移动。");
        if (controlledCamera.aspect <= 0f)
            return Warn("相机的画面宽高比必须大于零。");

        Canvas canvas = BackGround.GetComponentInParent<Canvas>();
        if (canvas != null && canvas.rootCanvas.renderMode != RenderMode.WorldSpace)
            return Warn("背景画布必须使用 World Space；屏幕空间画布会跟随相机，无法作为固定边界。");

        RectTransform rectangle = BackGround.GetComponent<RectTransform>();
        if (rectangle != null)
        {
            rectangle.GetWorldCorners(corners);
        }
        else
        {
            SpriteRenderer sprite = BackGround.GetComponent<SpriteRenderer>();
            if (sprite == null || sprite.sprite == null)
                return Warn("BackGroud 需要 RectTransform 或具有精灵的 SpriteRenderer。");

            Bounds bounds = sprite.localBounds;
            Transform source = sprite.transform;
            corners[0] = source.TransformPoint(new Vector3(bounds.min.x, bounds.min.y, bounds.center.z));
            corners[1] = source.TransformPoint(new Vector3(bounds.min.x, bounds.max.y, bounds.center.z));
            corners[2] = source.TransformPoint(new Vector3(bounds.max.x, bounds.max.y, bounds.center.z));
            corners[3] = source.TransformPoint(new Vector3(bounds.max.x, bounds.min.y, bounds.center.z));
        }

        Vector3 horizontal = corners[3] - corners[0];
        Vector3 vertical = corners[1] - corners[0];
        if (horizontal.magnitude < 0.0001f || vertical.magnitude < 0.0001f)
            return Warn("背景的宽高和缩放必须非零。");
        if (Mathf.Abs(horizontal.normalized.z) > 0.00001f || Mathf.Abs(vertical.normalized.z) > 0.00001f
            || Mathf.Abs(Vector3.Dot(horizontal.normalized, vertical.normalized)) > 0.00001f)
            return Warn("背景必须是平行于 XY 平面的矩形；请避免倾斜或父物体非均匀缩放造成的剪切。");

        backgroundCenter = (corners[0] + corners[2]) * 0.5f;
        backgroundRight = horizontal.normalized;
        backgroundUp = vertical.normalized;
        halfWidth = horizontal.magnitude * 0.5f;
        halfHeight = vertical.magnitude * 0.5f;
        lastWarning = null;
        return true;
    }

    private bool Warn(string message)
    {
        if (lastWarning != message)
        {
            Debug.LogWarning("Camera Move: " + message, this);
            lastWarning = message;
        }
        return false;
    }

    private void OnDisable()
    {
        dragging = false;
        zoomVelocity = 0f;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            dragging = false;
    }
}
