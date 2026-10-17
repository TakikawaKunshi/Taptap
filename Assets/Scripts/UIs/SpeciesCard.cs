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
    [SerializeField] private PlantVisualBinder plantVisualBinder;
    [SerializeField] private AnimalVisualBinder animalVisualBinder;

    private TimeManager subscribedTimeManager;
    private int lastRefilledDay;
    private bool initialized;
    private bool dragging;
    private int dragPointerId;
    private RectTransform dragIcon;
    private Canvas dragCanvas;
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();

    public SpeciesInfo Info => species;
    public GameObject Prefab
    {
        get
        {
            if (environment == null || !species.IsConfigured)
                return null;
            if (species.Kind == SpeciesKind.Plant)
                return plantVisualBinder != null && plantVisualBinder.isActiveAndEnabled
                    && plantVisualBinder.Map == environment
                    ? plantVisualBinder.GetPrefab(species.PlantSpecies) : null;
            AnimalSpawner spawner = animalVisualBinder != null ? animalVisualBinder.Spawner : null;
            return animalVisualBinder != null && animalVisualBinder.isActiveAndEnabled
                && spawner != null && spawner.Map == environment
                ? animalVisualBinder.GetPrefab(species.AnimalSpecies) : null;
        }
    }
    public int CurrentCount => currentCount;
    public bool CanPlace => initialized && isActiveAndEnabled && currentCount > 0
        && Prefab != null && environment != null && environment.isActiveAndEnabled
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
        ResolveVisualBinders();
        if (!species.IsConfigured)
            Debug.LogWarning("SpeciesCard：请为 " + species.Name + " 选择已实现的植物或动物物种。", this);
        else if (Prefab == null)
            Debug.LogWarning("SpeciesCard：请为 " + species.Name + " 在对应的 Visual Binder 中配置预设体，并确认 Binder 管理当前地图。", this);
    }

    /// <summary>初始化新卡片；info 选择逻辑物种，预设体来自对应的 Binder。</summary>
    public void Initialize(SpeciesInfo info, TimeManager manager = null,
        TileMapManager map = null, Camera inputCamera = null,
        PlantVisualBinder plants = null, AnimalVisualBinder animals = null)
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
        if (plants != null)
            plantVisualBinder = plants;
        if (animals != null)
            animalVisualBinder = animals;
        if (timeManager == null)
            timeManager = FindObjectOfType<TimeManager>();
        if (environment == null)
            environment = FindObjectOfType<TileMapManager>();
        ResolveVisualBinders();
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
        ResolveVisualBinders();
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

    /// <summary>将屏幕落点转换成有效格子，再投放参与生态结算的物种。</summary>
    public bool TryPlaceAtScreenPosition(Vector2 screenPosition)
    {
        ResolveVisualBinders();
        if (!CanPlace || IsPointerOverUI(screenPosition)
            || !TryGetEnvironmentCell(screenPosition, out TileCell cell))
            return false;
        return TryPlaceOnCell(cell);
    }

    /// <summary>登记成功才扣库存和操作次数；植物遵循主体/覆盖占位规则，动物进入 Spawner 的登记表。</summary>
    public bool TryPlaceOnCell(TileCell cell)
    {
        ResolveVisualBinders();
        if (!CanPlace || cell == null || cell.Manager != environment
            || !environment.TryGetCellAtTilemapPosition(cell.TilemapPosition, out TileCell registered)
            || registered != cell)
            return false;

        bool placed = timeManager.TryPerformFreeAction(() =>
        {
            bool success;
            if (species.Kind == SpeciesKind.Plant)
                success = PlantSettler.Place(PlantSettler.Create(species.PlantSpecies), cell);
            else
                success = animalVisualBinder.Spawner.TrySpawn(species.AnimalSpecies, cell, out _);
            if (!success)
                return false;
            currentCount--;
            return true;
        });

        // 显示在逻辑登记与扣费完成后刷新，卡片不另行实例化，避免生成重复外观。
        if (placed)
        {
            RefreshDisplay();
            if (species.Kind == SpeciesKind.Plant)
                plantVisualBinder.SyncNow();
            else
                animalVisualBinder.SyncNow();
        }
        return placed;
    }

    private void ResolveVisualBinders()
    {
        if (environment == null)
            return;

        if (species.Kind == SpeciesKind.Plant && plantVisualBinder == null)
        {
            PlantVisualBinder match = null;
            foreach (PlantVisualBinder candidate in FindObjectsOfType<PlantVisualBinder>())
            {
                if (candidate.Map != environment)
                    continue;
                if (match != null)
                    return;
                match = candidate;
            }
            plantVisualBinder = match;
        }
        else if (species.Kind == SpeciesKind.Animal && animalVisualBinder == null)
        {
            AnimalVisualBinder match = null;
            foreach (AnimalVisualBinder candidate in FindObjectsOfType<AnimalVisualBinder>())
            {
                if (candidate.Spawner == null || candidate.Spawner.Map != environment)
                    continue;
                if (match != null)
                    return;
                match = candidate;
            }
            animalVisualBinder = match;
        }
    }

    private bool TryGetEnvironmentCell(Vector2 screenPosition, out TileCell cell)
    {
        cell = null;
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
        Vector3 position = ray.GetPoint(distance);
        Vector3Int cellPosition = tilemap.WorldToCell(position);
        cellPosition.z = environment.OriginCellPosition.z;
        return tilemap.HasTile(cellPosition) && environment.TryGetCellAtTilemapPosition(cellPosition, out cell);
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
