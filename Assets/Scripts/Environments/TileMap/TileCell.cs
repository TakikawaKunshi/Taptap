using UnityEngine;
using UnityEngine.Tilemaps;

public enum E_TileState
{
    Dry,
    Wet
}

/// <summary>一个已绘制格子的独立数据；由 TileMapManager 创建并初始化，无需逐格手动挂载。</summary>
[DisallowMultipleComponent]
public class TileCell : MonoBehaviour
{
    [Header("运行时格子信息")]
    [SerializeField] private TileBase tileAsset;
    [SerializeField] private Vector3Int tilemapPosition;
    [SerializeField] private Vector2Int coordinates;
    [SerializeField] private bool hasWeather;
    [SerializeField] private E_WeatherType currentWeather;
    [SerializeField] private E_TileState currentState;
    [SerializeField, Min(0)] private int stateLayers;

    [Header("预留数据（暂不参与游戏逻辑）")]
    [SerializeField] private GameObject occupyingObject;
    [SerializeField] private bool isReachable;

    public TileMapManager Manager { get; private set; }
    public TileBase TileAsset => tileAsset;
    public Vector3Int TilemapPosition => tilemapPosition;
    public Vector2Int Coordinates => coordinates;
    public Vector3 WorldPosition => transform.position;
    public bool HasWeather => hasWeather;
    public E_WeatherType CurrentWeather => currentWeather;
    public E_TileState CurrentState => currentState;
    public int StateLayers => stateLayers;
    public GameObject OccupyingObject => occupyingObject;
    public bool IsReachable => isReachable;

    /// <summary>绑定所属地图、原始格子位置及以中心格为原点的整数坐标。</summary>
    internal void Initialize(TileMapManager manager, TileBase tile, Vector3Int position, Vector2Int coordinate)
    {
        Manager = manager;
        tileAsset = tile;
        tilemapPosition = position;
        coordinates = coordinate;
    }

    /// <summary>同步当前选定天气；每次均保持 1 层，不随刷新或过夜累计。</summary>
    internal void ApplyWeather(E_WeatherType weather)
    {
        hasWeather = true;
        currentWeather = weather;
        currentState = weather == E_WeatherType.Rain ? E_TileState.Wet : E_TileState.Dry;
        stateLayers = 1;
    }

    internal void ClearWeather()
    {
        hasWeather = false;
        stateLayers = 0;
    }

    /// <summary>由管理器在鼠标选中格子时调用；其他交互也可以调用此方法显示状态。</summary>
    public void ShowStatus()
    {
        if (Manager != null)
            Manager.ShowCellStatus(this);
    }

    public string GetStatusText()
    {
        string weatherText = hasWeather ? (currentWeather == E_WeatherType.Sun ? "晴天（Sun）" : "雨天（Rain）") : "未绑定天气";
        string stateText = hasWeather ? (currentState == E_TileState.Dry ? "干燥（Dry）" : "湿润（Wet）") : "未初始化";
        return $"格子坐标：({coordinates.x}, {coordinates.y})\n"
            + $"当前天气：{weatherText}\n"
            + $"当前状态：{stateText}\n"
            + $"状态层数：{stateLayers}\n"
            + "格子上的物体：预留\n"
            + "是否可达：预留";
    }
}
