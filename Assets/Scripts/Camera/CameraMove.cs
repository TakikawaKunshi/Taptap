using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>相机的两种状态：自由移动和地图全景。</summary>
public enum Camera_State
{
    FreeMove,
    BirdView
}

/// <summary>
/// 控制平面地图上的透视或正交相机。支持 Grid/Tilemap、精灵、Renderer 和 World Space UI。
/// Free Move 保留初始观察角度；Bird View 正对地图。最大视野完整显示背景，允许露出地图外部。
/// </summary>
[RequireComponent(typeof(Camera))]
[DisallowMultipleComponent]
[AddComponentMenu("Camera/Camera Move")]
[DefaultExecutionOrder(-100)]
public class CameraMove : MonoBehaviour
{
    [Tooltip("拖入场景中的 Grid、Tilemap、背景精灵或 World Space UI；会读取其子物体的地图范围。")]
    public GameObject BackGround;

    [SerializeField, Tooltip("启动时使用的相机状态。")]
    private Camera_State initialState = Camera_State.FreeMove;
    [SerializeField, Min(0.001f), Tooltip("最小视野：透视相机为距观察点的距离；正交相机为画面半高。")]
    private float minimumSize = 1f;
    [SerializeField, Min(0.01f), Tooltip("滚轮缩放灵敏度，越大缩放越快。")]
    private float zoomSensitivity = 0.15f;
    [SerializeField, Min(0.01f), Tooltip("缩放平滑时间，单位为秒。")]
    private float zoomSmoothTime = 0.15f;
    [SerializeField, Min(0.01f), Tooltip("Free Move 与 Bird View 的切换时长，单位为秒。")]
    private float transitionDuration = 0.6f;

    /// <summary>当前状态；切换期间表示正在前往的状态。</summary>
    public Camera_State State { get; private set; } = Camera_State.FreeMove;
    /// <summary>是否正在进行视角切换。</summary>
    public bool IsTransitioning => transitioning;

    // 观察点位于地图平面；位置由“观察点 - 朝向 * 距离”得到，便于平滑转动视角。
    private struct View
    {
        public Vector3 focus;
        public Quaternion rotation;
        public float distance;
        public float size;
    }

    private const float FitPadding = 1.02f;
    private Camera controlledCamera;
    private readonly List<Renderer> backgroundRenderers = new List<Renderer>();
    private readonly Vector3[] rectangleCorners = new Vector3[4];
    private readonly Vector3[] backgroundCorners = new Vector3[8];
    private Bounds backgroundBounds;
    private Vector3 backgroundOrigin;
    private Vector3 backgroundCenter;
    private Quaternion backgroundRotation;
    private Quaternion inverseBackgroundRotation;
    private Plane backgroundPlane;
    private View savedFreeView;
    private View transitionStart;
    private float transitionElapsed;
    private float targetZoom;
    private float zoomVelocity;
    private Vector3 previousMousePosition;
    private bool initialized;
    private bool transitioning;
    private bool dragging;
    private string lastWarning;

    /// <summary>取得当前物体的 Camera 组件。无参数。</summary>
    private void Awake()
    {
        controlledCamera = GetComponent<Camera>();
    }

    /// <summary>读取地图并更新当前状态。无参数；提前执行，让 FacingCamera 随后读取本帧相机角度。</summary>
    private void Update()
    {
        if (!TryReadBackground() || !InitializeView())
        {
            dragging = false;
            return;
        }

        if (transitioning)
            UpdateTransition();
        else if (State == Camera_State.BirdView)
            ApplyView(GetBirdView());
        else
            UpdateFreeMove();
    }

       /// <summary>供按钮或其他脚本调用，在两种状态间切换。无参数；连续调用也会保留原来的自由视角。</summary>
    public void ChangeCameraState()
    {
        if (!isActiveAndEnabled || !TryReadBackground() || !InitializeView())
            return;
        if (!TryCaptureView(out View current))
            return;

        if (State == Camera_State.FreeMove)
        {
            // 返回途中再次切换时，不用过渡画面覆盖原先的自由视角。
            if (!transitioning)
                savedFreeView = ClampFreeView(current);
            State = Camera_State.BirdView;
        }
        else
        {
            State = Camera_State.FreeMove;
        }

        BeginTransition(current);
    }

    /// <summary>首次运行时记录场景相机视角，并应用初始状态。无参数；返回是否已成功初始化。</summary>
    private bool InitializeView()
    {
        if (initialized)
            return true;
        if (!TryCaptureView(out View current))
            return false;

        savedFreeView = ClampFreeView(current);
        ApplyView(savedFreeView);
        targetZoom = GetZoom(savedFreeView);
        State = initialState;
        initialized = true;
        if (State == Camera_State.BirdView)
            BeginTransition(savedFreeView);
        return true;
    }

    /// <summary>开始平滑切换；current 是切换开始时实际显示的视角。</summary>
    private void BeginTransition(View current)
    {
        transitionStart = current;
        transitionElapsed = 0f;
        zoomVelocity = 0f;
        dragging = false;
        transitioning = true;
    }

    /// <summary>平滑改变观察点、旋转和视野，完成后恢复正常控制。无参数。</summary>
    private void UpdateTransition()
    {
        transitionElapsed += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(transitionElapsed / Mathf.Max(0.01f, transitionDuration));
        float blend = Mathf.SmoothStep(0f, 1f, progress);
        View destination = State == Camera_State.BirdView ? GetBirdView() : ClampFreeView(savedFreeView);

        ApplyView(new View
        {
            focus = Vector3.Lerp(transitionStart.focus, destination.focus, blend),
            rotation = Quaternion.Slerp(transitionStart.rotation, destination.rotation, blend),
            distance = Mathf.Lerp(transitionStart.distance, destination.distance, blend),
            size = Mathf.Lerp(transitionStart.size, destination.size, blend)
        });

        if (progress >= 1f)
        {
            ApplyView(destination);
            targetZoom = GetZoom(destination);
            transitioning = false;
        }
    }

    /// <summary>处理自由状态下的中键拖动和滚轮缩放。无参数；透视缩放通过改变距离实现，保持 FOV 不变。</summary>
    private void UpdateFreeMove()
    {
        if (!TryCaptureView(out View view))
            return;

        Vector3 mousePosition = Input.mousePosition;
        if (Input.GetMouseButton(2) && Application.isFocused)
        {
            if (dragging
                && TryGetMousePoint(previousMousePosition, out Vector3 previousPoint)
                && TryGetMousePoint(mousePosition, out Vector3 currentPoint))
            {
                view.focus += previousPoint - currentPoint;
            }
            dragging = true;
        }
        else
        {
            dragging = false;
        }
        previousMousePosition = mousePosition;

        float minimum = GetMinimumZoom();
        float maximum = GetMaximumZoom(view.rotation);
        targetZoom = Mathf.Clamp(targetZoom, minimum, maximum);
        float scroll = Input.mouseScrollDelta.y;
        if (scroll != 0f)
        {
            float scale = Mathf.Exp(Mathf.Clamp(-scroll * zoomSensitivity, -10f, 10f));
            targetZoom = Mathf.Clamp(targetZoom * scale, minimum, maximum);
        }

        float zoom = GetZoom(view);
        if (Time.unscaledDeltaTime > 0f)
        {
            zoom = Mathf.SmoothDamp(zoom, targetZoom, ref zoomVelocity,
                Mathf.Max(0.01f, zoomSmoothTime), Mathf.Infinity, Time.unscaledDeltaTime);
        }

        // 到达目标时精确对齐，确保最大视野最终完全居中。
        if (Mathf.Abs(zoom - targetZoom) < 0.0001f)
        {
            zoom = targetZoom;
            zoomVelocity = 0f;
        }
        if (controlledCamera.orthographic)
            view.size = zoom;
        else
            view.distance = zoom;

        ApplyView(ClampFreeView(view));
    }

    /// <summary>求鼠标射线与地图平面的交点；screenPosition 是屏幕像素坐标，point 输出世界坐标，返回是否相交。</summary>
    private bool TryGetMousePoint(Vector3 screenPosition, out Vector3 point)
    {
        Ray ray = controlledCamera.ScreenPointToRay(screenPosition);
        if (backgroundPlane.Raycast(ray, out float distance))
        {
            point = ray.GetPoint(distance);
            return true;
        }
        point = default;
        return false;
    }

    /// <summary>生成保持自由视角 2.5D 透视角度的 Bird View。无参数；移到地图中心并缩放至完整显示背景。</summary>
    private View GetBirdView()
    {
        Quaternion rotation = savedFreeView.rotation;
        float zoom = GetMaximumZoom(rotation);
        return new View
        {
            focus = backgroundCenter,
            rotation = rotation,
            distance = controlledCamera.orthographic
                ? Mathf.Max(savedFreeView.distance, GetClipDistance(rotation)) : zoom,
            size = controlledCamera.orthographic ? zoom : savedFreeView.size
        };
    }

    /// <summary>读取视野数值；view 是要读取的视角，透视返回距离，正交返回画面半高。</summary>
    private float GetZoom(View view)
    {
        return controlledCamera.orthographic ? view.size : view.distance;
    }

    /// <summary>取得允许的最小视野，透视模式还会避开相机近裁剪面。无参数。</summary>
    private float GetMinimumZoom()
    {
        return Mathf.Max(Mathf.Max(0.001f, minimumSize),
            controlledCamera.orthographic ? 0.001f : controlledCamera.nearClipPlane + 0.01f);
    }

    /// <summary>计算完整显示背景的最大视野；rotation 是观察角度，会同时考虑宽高比、透视深度和近裁剪面。</summary>
    private float GetMaximumZoom(Quaternion rotation)
    {
        Quaternion inverse = Quaternion.Inverse(rotation);
        float verticalTangent = Mathf.Tan(controlledCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float horizontalTangent = verticalTangent * controlledCamera.aspect;
        float result = GetMinimumZoom();
        for (int i = 0; i < backgroundCorners.Length; i++)
        {
            Vector3 point = inverse * (backgroundCorners[i] - backgroundCenter);
            float required = controlledCamera.orthographic
                ? Mathf.Max(Mathf.Abs(point.x) / controlledCamera.aspect, Mathf.Abs(point.y)) * FitPadding
                : GetRequiredDistance(point.x, point.y, point.z, horizontalTangent,
                    verticalTangent, controlledCamera.nearClipPlane);
            result = Mathf.Max(result, required);
        }
        return result;
    }

    /// <summary>
    /// 计算容纳单个顶点所需的相机距离。x/y/z 是顶点相对地图中心、在相机方向下的坐标；
    /// horizontalTangent/verticalTangent 是水平/垂直半视角的正切，nearClip 是近裁剪距离。
    /// </summary>
    private static float GetRequiredDistance(float x, float y, float z,
        float horizontalTangent, float verticalTangent, float nearClip)
    {
        float horizontal = Mathf.Abs(x) * FitPadding / horizontalTangent - z;
        float vertical = Mathf.Abs(y) * FitPadding / verticalTangent - z;
        return Mathf.Max(Mathf.Max(horizontal, vertical), nearClip + 0.01f - z);
    }

    /// <summary>计算让背景位于近裁剪面前方的距离；rotation 是相机角度，用于正交相机的前后定位。</summary>
    private float GetClipDistance(Quaternion rotation)
    {
        Vector3 forward = rotation * Vector3.forward;
        float distance = controlledCamera.nearClipPlane + 0.01f;
        for (int i = 0; i < backgroundCorners.Length; i++)
            distance = Mathf.Max(distance, controlledCamera.nearClipPlane + 0.01f
                - Vector3.Dot(backgroundCorners[i] - backgroundCenter, forward));
        return distance;
    }

    /// <summary>
    /// 限制自由视角；view 是待修正视角。视野越大，可拖动范围越小，最大时居中显示整张地图。
    /// 只限制观察点与缩放，不要求画面四角都在背景内，因此允许显示背景外部。
    /// </summary>
    private View ClampFreeView(View view)
    {
        float minimum = GetMinimumZoom();
        float maximum = GetMaximumZoom(view.rotation);
        float zoom = Mathf.Clamp(GetZoom(view), minimum, maximum);
        if (controlledCamera.orthographic)
        {
            view.size = zoom;
            view.distance = Mathf.Max(view.distance, GetClipDistance(view.rotation));
        }
        else
        {
            view.distance = zoom;
        }

        float movement = maximum > minimum
            ? 1f - Mathf.InverseLerp(minimum, maximum, zoom) : 0f;
        Vector3 localFocus = inverseBackgroundRotation * (view.focus - backgroundCenter);
        Vector3 limits = backgroundBounds.extents * movement;
        localFocus.x = Mathf.Clamp(localFocus.x, -limits.x, limits.x);
        localFocus.y = Mathf.Clamp(localFocus.y, -limits.y, limits.y);
        localFocus.z = 0f;
        view.focus = backgroundCenter + backgroundRotation * localFocus;
        return view;
    }

    /// <summary>读取实际相机视角；view 输出观察点、旋转、距离和正交半高，返回相机是否朝向地图平面。</summary>
    private bool TryCaptureView(out View view)
    {
        Ray ray = new Ray(transform.position, transform.forward);
        view = default;
        if (!backgroundPlane.Raycast(ray, out float distance) || distance <= 0.0001f)
            return Warn("相机需要朝向地图平面，并与地图保持距离。");

        view = new View
        {
            focus = ray.GetPoint(distance),
            rotation = transform.rotation,
            distance = distance,
            size = controlledCamera.orthographicSize
        };
        lastWarning = null;
        return true;
    }

    /// <summary>应用指定视角；view 包含观察点、角度和缩放。必要时扩大远裁剪距离，避免全景被裁掉。</summary>
    private void ApplyView(View view)
    {
        Vector3 forward = view.rotation * Vector3.forward;
        Vector3 position = view.focus - forward * view.distance;
        transform.SetPositionAndRotation(position, view.rotation);
        if (controlledCamera.orthographic)
            controlledCamera.orthographicSize = view.size;

        float farDistance = controlledCamera.nearClipPlane + 1f;
        for (int i = 0; i < backgroundCorners.Length; i++)
            farDistance = Mathf.Max(farDistance, Vector3.Dot(backgroundCorners[i] - position, forward) + 1f);
        if (farDistance > controlledCamera.farClipPlane)
            controlledCamera.farClipPlane = farDistance;
    }

    /// <summary>读取 BackGround 及其子物体的范围，并建立地图平面。无参数；返回背景配置是否可用，不修改背景。</summary>
    private bool TryReadBackground()
    {
        if (controlledCamera == null)
            controlledCamera = GetComponent<Camera>();
        if (BackGround == null)
            return Warn("请将场景中的 Grid 或 Tilemap 拖入 BackGround。");
        if (BackGround.transform.IsChildOf(gameObject.transform))
            return Warn("BackGround 不能是相机本身或相机的子物体。");
        if (controlledCamera.aspect <= 0f)
            return Warn("相机的画面宽高比必须大于零。");

        Canvas canvas = BackGround.GetComponentInParent<Canvas>();
        if (canvas != null && canvas.rootCanvas.renderMode != RenderMode.WorldSpace)
            return Warn("背景画布需要使用 World Space，不能随屏幕移动。");

        backgroundOrigin = BackGround.transform.position;
        backgroundRotation = BackGround.transform.rotation;
        inverseBackgroundRotation = Quaternion.Inverse(backgroundRotation);
        Bounds bounds = default;
        bool found = false;
        RectTransform rectangle = BackGround.GetComponent<RectTransform>();
        if (rectangle != null)
        {
            rectangle.GetWorldCorners(rectangleCorners);
            for (int i = 0; i < rectangleCorners.Length; i++)
                EncapsulateWorldPoint(rectangleCorners[i], ref bounds, ref found);
        }
        else
        {
            backgroundRenderers.Clear();
            BackGround.GetComponentsInChildren(false, backgroundRenderers);
            for (int i = 0; i < backgroundRenderers.Count; i++)
            {
                Renderer renderer = backgroundRenderers[i];
                if (!renderer.enabled)
                    continue;
                Bounds localBounds = renderer.localBounds;
                if (renderer is TilemapRenderer)
                {
                    Tilemap tilemap = renderer.GetComponent<Tilemap>();
                    if (tilemap == null || tilemap.GetUsedTilesCount() == 0)
                        continue;
                    localBounds = tilemap.localBounds;
                }
                for (int corner = 0; corner < 8; corner++)
                    EncapsulateWorldPoint(renderer.transform.TransformPoint(GetBoundsCorner(localBounds, corner)),
                        ref bounds, ref found);
            }
        }

        if (!found || bounds.size.x < 0.0001f || bounds.size.y < 0.0001f)
            return Warn("背景需要有效的 Tilemap、Renderer 或矩形 UI，且地图平面的宽高不能为零。");

        backgroundBounds = bounds;
        backgroundCenter = backgroundOrigin + backgroundRotation * bounds.center;
        backgroundPlane = new Plane(backgroundRotation * Vector3.forward, backgroundCenter);
        for (int i = 0; i < backgroundCorners.Length; i++)
            backgroundCorners[i] = backgroundOrigin + backgroundRotation * GetBoundsCorner(bounds, i);
        return true;
    }

    /// <summary>将一个世界坐标点加入地图范围；point 是点坐标，bounds 是累计范围，found 表示是否已有第一个点。</summary>
    private void EncapsulateWorldPoint(Vector3 point, ref Bounds bounds, ref bool found)
    {
        Vector3 localPoint = inverseBackgroundRotation * (point - backgroundOrigin);
        if (found)
            bounds.Encapsulate(localPoint);
        else
        {
            bounds = new Bounds(localPoint, Vector3.zero);
            found = true;
        }
    }

    /// <summary>取得包围盒的一个顶点；bounds 是包围盒，index 为 0 到 7 的顶点编号。</summary>
    private static Vector3 GetBoundsCorner(Bounds bounds, int index)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        return new Vector3((index & 1) == 0 ? min.x : max.x,
            (index & 2) == 0 ? min.y : max.y, (index & 4) == 0 ? min.z : max.z);
    }

    /// <summary>输出配置提示；message 是提示文字。相同提示尽量不重复输出，返回 false 以停止本次处理。</summary>
    private bool Warn(string message)
    {
        if (lastWarning != message)
        {
            Debug.LogWarning("Camera Move: " + message, this);
            lastWarning = message;
        }
        return false;
    }

    /// <summary>组件停用时清除拖动和缩放惯性，避免重新启用后突然移动。无参数。</summary>
    private void OnDisable()
    {
        dragging = false;
        zoomVelocity = 0f;
    }

    /// <summary>窗口焦点改变时处理拖动；hasFocus 表示窗口是否获得焦点，失去焦点时停止拖动。</summary>
    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            dragging = false;
    }
}
