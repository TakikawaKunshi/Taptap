using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>显示物种信息，处理左键拖放，并在成功过夜后补充每日投放数量。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class SpeciesCard : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("物种配置（也可由 SpeciesList / Initialize 传入）")]
    [SerializeField] private SpeciesInfo species;
    [SerializeField, Min(0)] private int currentCount;

    [Header("BackGround 下的显示组件，留空按子物体名称查找")]
    [SerializeField] private Image iconImage;
    [SerializeField] private Text speciesName;
    [SerializeField] private Text speciesDescription;
    [SerializeField] private Text speciesCount;
    [Header("兼容现有预设体的 TMP 文本")]
    [SerializeField] private TMP_Text speciesNameTMP;
    [SerializeField] private TMP_Text speciesDescriptionTMP;
    [SerializeField] private TMP_Text speciesCountTMP;

    [Header("场景引用，由 SpeciesList 统一传入")]
    [SerializeField] private TimeManager timeManager;
    [SerializeField] private TileMapManager environment;
    [SerializeField] private Camera placementCamera;
    [SerializeField, Tooltip("与环境 Tilemap 同级且挂有 FacingCamera 的物体；留空时自动查找。")]
    private Transform speciesParent;

    private TimeManager subscribedTimeManager;
    private int lastRefilledDay;
    private bool initialized;
    private bool dragging;
    private int dragPointerId;
    private RectTransform dragIcon;
    private Canvas dragCanvas;
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();

    public SpeciesInfo Info => species;
    public GameObject Prefab => species.Prefab;
    public int CurrentCount => currentCount;
    public bool CanPlace => initialized && isActiveAndEnabled && currentCount > 0
        && species.Prefab != null && environment != null && environment.isActiveAndEnabled
        && timeManager != null && timeManager.CanOperate;

    private void Reset()
    {
        CacheDisplay();
    }

    private void Awake()
    {
        // 不活跃的卡片也可能已被外部初始化，不能在随后 Awake 时覆盖剩余数量。
        if (!initialized)
            Initialize(species);
    }

    private void OnEnable()
    {
        BindTimeManager();
        SynchronizeDay();
    }

    private void Start()
    {
        // 所有 Awake 完成后再同步一次，兼容非第 1 天开局。
        SynchronizeDay();
        if (timeManager == null)
            Debug.LogWarning("SpeciesCard：请绑定 TimeManager，才能投放生物、扣除自由操作次数并在过夜后补充数量。", this);
        if (environment == null)
            Debug.LogWarning("SpeciesCard：请绑定环境的 TileMapManager。", this);
    }

    /// <summary>初始化新卡片；info 包含头像、名称、描述、每日数量与物种预设体。</summary>
    public void Initialize(SpeciesInfo info, TimeManager manager = null,
        TileMapManager map = null, Camera inputCamera = null, Transform parent = null)
    {
        CancelDrag();
        UnbindTimeManager();
        species = info;
        species.DailyCount = Mathf.Max(0, species.DailyCount);
        currentCount = species.DailyCount;
        if (manager != null)
            timeManager = manager;
        if (map != null)
            environment = map;
        if (inputCamera != null)
            placementCamera = inputCamera;
        if (parent != null)
            speciesParent = parent;
        if (timeManager == null)
            timeManager = FindObjectOfType<TimeManager>();
        if (environment == null)
            environment = FindObjectOfType<TileMapManager>();
        lastRefilledDay = timeManager != null ? timeManager.CurrentDay : 1;
        initialized = true;
        CacheDisplay();
        RefreshDisplay();
        BindTimeManager();
    }

    private void CacheDisplay()
    {
        Transform background = transform.Find("BackGround");
        if (background == null)
            return;
        Transform icon = background.Find("IconImage");
        if (iconImage == null && icon != null)
            iconImage = icon.GetComponent<Image>();
        BindText(background, "SpeciesName", ref speciesName, ref speciesNameTMP);
        BindText(background, "SpeciesDescription", ref speciesDescription, ref speciesDescriptionTMP);
        BindText(background, "SpeciesCount", ref speciesCount, ref speciesCountTMP);
    }

    private static void BindText(Transform background, string childName, ref Text text, ref TMP_Text tmp)
    {
        Transform child = background.Find(childName);
        if (child == null)
            return;
        if (text == null)
            text = child.GetComponent<Text>();
        if (tmp == null)
            tmp = child.GetComponent<TMP_Text>();
    }

    public void RefreshDisplay()
    {
        if (iconImage != null)
            iconImage.sprite = species.Icon;
        SetText(speciesName, speciesNameTMP, species.Name ?? string.Empty);
        SetText(speciesDescription, speciesDescriptionTMP, species.Description ?? string.Empty);
        SetText(speciesCount, speciesCountTMP, currentCount.ToString());
    }

    private static void SetText(Text text, TMP_Text tmp, string value)
    {
        if (text != null)
            text.text = value;
        if (tmp != null)
            tmp.text = value;
    }

    private void BindTimeManager()
    {
        if (!isActiveAndEnabled || subscribedTimeManager == timeManager)
            return;
        UnbindTimeManager();
        subscribedTimeManager = timeManager;
        if (subscribedTimeManager != null)
        {
            subscribedTimeManager.DayChanged += OnDayChanged;
            subscribedTimeManager.StateChanged += OnTimeStateChanged;
        }
    }

    private void UnbindTimeManager()
    {
        if (subscribedTimeManager != null)
        {
            subscribedTimeManager.DayChanged -= OnDayChanged;
            subscribedTimeManager.StateChanged -= OnTimeStateChanged;
        }
        subscribedTimeManager = null;
    }

    private void SynchronizeDay()
    {
        if (initialized && timeManager != null)
            OnDayChanged(timeManager.CurrentDay);
    }

    private void OnDayChanged(int day)
    {
        // 同一天重新打开 UI 不补充；关闭 UI 期间过夜则在重新打开时补充。
        if (!initialized || day <= lastRefilledDay)
            return;
        CancelDrag();
        lastRefilledDay = day;
        currentCount = Mathf.Max(0, species.DailyCount);
        RefreshDisplay();
    }

    private void OnTimeStateChanged()
    {
        if (!CanPlace)
            CancelDrag();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || dragging || !CanPlace)
            return;
        dragging = true;
        dragPointerId = eventData.pointerId;
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
            return;
        dragCanvas = canvas.rootCanvas;
        var preview = new GameObject("Species Drag Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        preview.layer = dragCanvas.gameObject.layer;
        dragIcon = preview.GetComponent<RectTransform>();
        dragIcon.SetParent(dragCanvas.transform, false);
        dragIcon.SetAsLastSibling();
        dragIcon.sizeDelta = new Vector2(64f, 64f);
        Image image = preview.GetComponent<Image>();
        image.sprite = species.Icon;
        image.preserveAspect = true;
        image.color = new Color(1f, 1f, 1f, 0.75f);
        image.raycastTarget = false;
        MoveDragIcon(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!dragging || eventData.pointerId != dragPointerId)
            return;
        if (!CanPlace)
            CancelDrag();
        else
            MoveDragIcon(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!dragging || eventData.pointerId != dragPointerId)
            return;
        CancelDrag();
        if (eventData.button == PointerEventData.InputButton.Left)
            TryPlaceAtScreenPosition(eventData.position);
    }

    private void MoveDragIcon(PointerEventData eventData)
    {
        if (dragIcon == null || dragCanvas == null)
            return;
        Camera camera = dragCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null
            : (dragCanvas.worldCamera != null ? dragCanvas.worldCamera : eventData.pressEventCamera);
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)dragCanvas.transform,
            eventData.position, camera, out Vector2 point))
            dragIcon.localPosition = new Vector3(point.x, point.y, 0f);
    }

    /// <summary>在有效环境位置投放；仅成功创建后扣除一只库存和一次全局自由操作。</summary>
    public bool TryPlaceAtScreenPosition(Vector2 screenPosition)
    {
        if (!CanPlace || IsPointerOverUI(screenPosition)
            || !TryGetEnvironmentPosition(screenPosition, out Vector3 position)
            || !TryGetSpeciesParent(out Transform parent))
            return false;
        return timeManager.TryPerformFreeAction(() =>
        {
            // 传入世界坐标，父物体自身的位移、旋转不会改变鼠标对应的投放位置。
            GameObject instance = Instantiate(species.Prefab, position, species.Prefab.transform.rotation, parent);
            if (instance == null)
                return false;
            currentCount--;
            RefreshDisplay();
            return true;
        });
    }

    private bool TryGetSpeciesParent(out Transform parent)
    {
        parent = speciesParent;
        if (parent != null)
        {
            if (IsSpeciesParent(parent))
                return true;
            Debug.LogWarning("SpeciesCard：Species Parent 必须与环境 Tilemap 同级，并挂有 FacingCamera。", this);
            return false;
        }

        Transform mapParent = environment.transform.parent;
        FacingCamera[] candidates = mapParent != null
            ? mapParent.GetComponentsInChildren<FacingCamera>(true)
            : FindObjectsOfType<FacingCamera>(true);
        foreach (FacingCamera candidate in candidates)
        {
            if (!IsSpeciesParent(candidate.transform))
                continue;
            if (parent != null)
            {
                Debug.LogWarning("SpeciesCard：Tilemap 同级存在多个 FacingCamera，请在 SpeciesList 中指定 Species Parent。", this);
                return false;
            }
            parent = candidate.transform;
        }
        if (parent != null)
            return true;
        Debug.LogWarning("SpeciesCard：未找到与 Tilemap 同级且挂有 FacingCamera 的物种父物体，本次未投放。", this);
        return false;
    }

    private bool IsSpeciesParent(Transform candidate)
    {
        return candidate != environment.transform
            && candidate.parent == environment.transform.parent
            && candidate.gameObject.scene == environment.gameObject.scene
            && candidate.GetComponent<FacingCamera>() != null;
    }

    private bool TryGetEnvironmentPosition(Vector2 screenPosition, out Vector3 position)
    {
        position = default;
        Camera camera = placementCamera != null ? placementCamera : Camera.main;
        if (camera == null || !camera.pixelRect.Contains(screenPosition))
            return false;
        environment.InitializeCells();
        if (environment.Tilemap == null || environment.CellCount == 0)
            return false;

        // 与现有格子选取使用相同的地图平面，支持倾斜透视相机且无需 Collider。
        var tilemap = environment.Tilemap;
        Vector3 origin = environment.OriginWorldPosition;
        Vector3 right = tilemap.GetCellCenterWorld(environment.OriginCellPosition + Vector3Int.right) - origin;
        Vector3 up = tilemap.GetCellCenterWorld(environment.OriginCellPosition + Vector3Int.up) - origin;
        Vector3 normal = Vector3.Cross(right, up);
        if (normal.sqrMagnitude < 0.000001f)
            return false;
        Ray ray = camera.ScreenPointToRay(screenPosition);
        if (!new Plane(normal.normalized, origin).Raycast(ray, out float distance))
            return false;
        position = ray.GetPoint(distance);
        Vector3Int cellPosition = tilemap.WorldToCell(position);
        cellPosition.z = environment.OriginCellPosition.z;
        return tilemap.HasTile(cellPosition) && environment.TryGetCellAtTilemapPosition(cellPosition, out _);
    }

    private bool IsPointerOverUI(Vector2 screenPosition)
    {
        if (EventSystem.current == null)
            return false;
        var pointer = new PointerEventData(EventSystem.current) { position = screenPosition };
        uiHits.Clear();
        EventSystem.current.RaycastAll(pointer, uiHits);
        foreach (RaycastResult hit in uiHits)
        {
            if (hit.module is GraphicRaycaster)
                return true;
        }
        return false;
    }

    private void CancelDrag()
    {
        dragging = false;
        if (dragIcon != null)
            Destroy(dragIcon.gameObject);
        dragIcon = null;
        dragCanvas = null;
    }

    private void OnDisable()
    {
        CancelDrag();
        UnbindTimeManager();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            CancelDrag();
    }
}
