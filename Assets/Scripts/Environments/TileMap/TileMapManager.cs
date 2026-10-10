using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

/// <summary>管理本物体 Tilemap 上的有效格子，统一同步天气并处理点击。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Tilemap))]
public class TileMapManager : MonoBehaviour
{
    [SerializeField, Tooltip("自动获取本物体上的 Tilemap。")]
    private Tilemap tilemap;
    [SerializeField, Tooltip("拖入天气对象上的 WeatherBase，与天气下拉框使用同一个对象。")]
    private WeatherBase weather;
    [SerializeField, Tooltip("拖入 UI 中用于显示格子状态的 Text-TextMeshPro。")]
    private TMP_Text message;
    [SerializeField, Tooltip("用于点击地图的相机；留空时使用 MainCamera。")]
    private Camera inputCamera;

    private readonly Dictionary<Vector3Int, TileCell> cells = new Dictionary<Vector3Int, TileCell>();
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    private Transform cellRoot;
    private bool initialized;
    private bool weatherSynchronized;
    private WeatherBase lastWeatherSource;
    private bool lastHasWeather;
    private E_WeatherType lastWeather;

    public Tilemap Tilemap => tilemap;
    public WeatherBase Weather => weather;
    public TMP_Text Message => message;
    public IReadOnlyCollection<TileCell> Cells => cells.Values;
    public int CellCount => cells.Count;
    public TileCell SelectedCell { get; private set; }
    public Vector3Int OriginCellPosition { get; private set; }
    public Vector3 OriginWorldPosition => tilemap != null ? tilemap.GetCellCenterWorld(OriginCellPosition) : transform.position;
    private Vector3 geometricCenterLocal;
    public Vector3 GeometricCenterWorld => transform.TransformPoint(geometricCenterLocal);

    private void Reset()
    {
        tilemap = GetComponent<Tilemap>();
    }

    private void Awake()
    {
        tilemap = GetComponent<Tilemap>();
    }

    private void Start()
    {
        InitializeCells();
        if (weather == null)
            Debug.LogWarning("TileMapManager：请绑定 WeatherBase，格子才能同步天气状态。", this);
        if (message == null)
            Debug.LogWarning("TileMapManager：请绑定 Message，用于显示点击的格子状态。", this);
    }

    private void OnEnable()
    {
        RefreshWeather();
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
            TrySelectCellAtScreenPosition(Input.mousePosition);
    }

    private void LateUpdate()
    {
        // WeatherBase 当前天气是公开字段。只比较一次，值变化时才通知所有格子。
        RefreshWeather();
    }

    /// <summary>开局扫描一次。重复调用不会创建重复物体或重置已有格子数据。</summary>
    public void InitializeCells()
    {
        if (initialized || !Application.isPlaying)
            return;
        if (tilemap == null)
            tilemap = GetComponent<Tilemap>();
        if (tilemap.cellLayout != GridLayout.CellLayout.Rectangle)
        {
            Debug.LogWarning("TileMapManager：当前坐标方案需要矩形 Tilemap。", this);
            return;
        }

        var positions = new List<Vector3Int>();
        Vector3 centerSum = Vector3.zero;
        foreach (Vector3Int position in tilemap.cellBounds.allPositionsWithin)
        {
            if (!tilemap.HasTile(position))
                continue;
            if (positions.Count > 0 && position.z != positions[0].z)
            {
                Debug.LogWarning("TileMapManager：请将不同 Z 层放在不同 Tilemap 上，每张地图只管理一个平面。", this);
                return;
            }
            positions.Add(position);
            centerSum += tilemap.GetCellCenterLocal(position);
        }

        initialized = true;
        if (positions.Count == 0)
            return;

        // 每个有效格子等权计算中心，空格不参与；距离相同时固定选左下方的格子。
        geometricCenterLocal = centerSum / positions.Count;
        OriginCellPosition = positions[0];
        float nearestDistance = float.PositiveInfinity;
        foreach (Vector3Int position in positions)
        {
            float distance = (tilemap.GetCellCenterWorld(position) - GeometricCenterWorld).sqrMagnitude;
            bool tied = Mathf.Approximately(distance, nearestDistance);
            bool earlier = position.y < OriginCellPosition.y
                || (position.y == OriginCellPosition.y && position.x < OriginCellPosition.x);
            if ((!tied && distance < nearestDistance) || (tied && earlier))
            {
                nearestDistance = distance;
                OriginCellPosition = position;
            }
        }

        cellRoot = new GameObject("Tile Cells").transform;
        cellRoot.SetParent(transform, false);
        foreach (Vector3Int position in positions)
        {
            var coordinate = new Vector2Int(position.x - OriginCellPosition.x, position.y - OriginCellPosition.y);
            var cellObject = new GameObject($"TileCell ({coordinate.x}, {coordinate.y})");
            cellObject.transform.SetParent(cellRoot, false);
            cellObject.transform.localPosition = tilemap.GetCellCenterLocal(position);
            var cell = cellObject.AddComponent<TileCell>();
            cell.Initialize(this, tilemap.GetTile(position), position, coordinate);
            cells.Add(position, cell);
        }
        RefreshWeather();
    }

    /// <summary>读取当前选定天气；同一天切换天气即可刷新，层数始终为 1。</summary>
    public void RefreshWeather()
    {
        if (!initialized)
            return;
        bool hasWeather = weather != null;
        E_WeatherType current = hasWeather ? weather.weatherType : default;
        hasWeather &= current == E_WeatherType.Sun || current == E_WeatherType.Rain;
        if (weatherSynchronized && lastWeatherSource == weather && lastHasWeather == hasWeather && lastWeather == current)
            return;

        weatherSynchronized = true;
        lastWeatherSource = weather;
        lastHasWeather = hasWeather;
        lastWeather = current;
        foreach (TileCell cell in cells.Values)
        {
            if (cell == null)
                continue;
            if (hasWeather)
                cell.ApplyWeather(current);
            else
                cell.ClearWeather();
        }
        if (SelectedCell != null && message != null)
            message.text = SelectedCell.GetStatusText();
    }

    public bool TryGetCell(Vector2Int coordinates, out TileCell cell)
    {
        return cells.TryGetValue(new Vector3Int(coordinates.x + OriginCellPosition.x,
            coordinates.y + OriginCellPosition.y, OriginCellPosition.z), out cell) && cell != null;
    }

    public bool TryGetCellAtTilemapPosition(Vector3Int position, out TileCell cell)
    {
        return cells.TryGetValue(position, out cell) && cell != null;
    }

    /// <summary>射线与地图平面求交后定位格子，兼容正交及倾斜透视相机，无需逐格碰撞体。</summary>
    public bool TrySelectCellAtScreenPosition(Vector2 screenPosition)
    {
        if (!isActiveAndEnabled || !initialized || cells.Count == 0 || IsPointerOverUI(screenPosition))
            return false;
        Camera camera = inputCamera != null ? inputCamera : Camera.main;
        if (camera == null || !camera.pixelRect.Contains(screenPosition))
            return false;

        Vector3 origin = OriginWorldPosition;
        Vector3 right = tilemap.GetCellCenterWorld(OriginCellPosition + Vector3Int.right) - origin;
        Vector3 up = tilemap.GetCellCenterWorld(OriginCellPosition + Vector3Int.up) - origin;
        Vector3 normal = Vector3.Cross(right, up);
        if (normal.sqrMagnitude < 0.000001f)
            return false;
        var plane = new Plane(normal.normalized, origin);
        Ray ray = camera.ScreenPointToRay(screenPosition);
        if (!plane.Raycast(ray, out float distance))
            return false;

        Vector3Int position = tilemap.WorldToCell(ray.GetPoint(distance));
        // 平面求交的小数误差可能使 Z 被向下取整到相邻层。
        position.z = OriginCellPosition.z;
        if (!tilemap.HasTile(position) || !TryGetCellAtTilemapPosition(position, out TileCell cell))
            return false;
        cell.ShowStatus();
        return true;
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

    public void ShowCellStatus(TileCell cell)
    {
        if (cell == null || cell.Manager != this)
            return;
        RefreshWeather();
        SelectedCell = cell;
        if (message != null)
            message.text = cell.GetStatusText();
    }

    private void OnDestroy()
    {
        if (cellRoot != null)
            Destroy(cellRoot.gameObject);
    }
}
