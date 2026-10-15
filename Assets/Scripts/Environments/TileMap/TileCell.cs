using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public enum E_TileState
{
    Dry,
    Wet
}

/// <summary>
/// 一个已绘制格子的独立数据；由 TileMapManager 创建并初始化，无需逐格手动挂载。
/// 同时作为植物看到的格子实现 ICellEnv：植物通过接口读取地面属性，不需要知道 Tilemap 的存在。
/// </summary>
[DisallowMultipleComponent]
public class TileCell : MonoBehaviour, ICellEnv
{
    [Header("运行时格子信息")]
    [SerializeField] private TileBase tileAsset;
    [SerializeField] private Vector3Int tilemapPosition;
    [SerializeField] private Vector2Int coordinates;
    [SerializeField] private bool hasWeather;
    [SerializeField] private E_WeatherType currentWeather;
    [SerializeField] private E_TileState currentState;
    [SerializeField, Min(0)] private int stateLayers;

    [Header("地面属性")]
    [SerializeField, Tooltip("地表类型；策划案里苔藓可以生长在石面和木面上。")]
    private E_SurfaceKind surface = E_SurfaceKind.Ground;
    [SerializeField, Min(0), Tooltip("该格的余屑份数；蘑菇与藻类在结算时读取或消耗它。")]
    private int detritus;

    [Header("植物（运行时由 PlantSettler 写入）")]
    [SerializeField] private PlantsBase subject;
    [SerializeField] private List<PlantsBase> cover = new List<PlantsBase>();

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

    // ---- ICellEnv：植物读到的地面属性 ----

    /// <summary>
    /// 湿润状态。策划已确认首版不记录干湿层数，因此直接取当前状态，
    /// 由 TileMapManager 每帧按玩家选择的天气同步。
    /// </summary>
    public bool IsWet
    {
        get { return hasWeather && currentState == E_TileState.Wet; }
    }

    public E_SurfaceKind Surface
    {
        get { return surface; }
    }

    public int Detritus
    {
        get { return detritus; }
        set { detritus = Mathf.Max(0, value); }
    }

    /// <summary>
    /// 是否还能被新植物占用。
    /// 主体位与地表覆盖都为空才算可用：策划案允许苔藓与主体共存，
    /// 但同一格不应叠放两株覆盖，否则扩张结果会失去意义。
    /// </summary>
    public bool CanOccupy
    {
        get
        {
            if (subject != null && !subject.IsDead)
                return false;
            for (int i = 0; i < cover.Count; i++)
            {
                if (cover[i] != null && !cover[i].IsDead)
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// 遮蔽来自地图上的主体植物而不是天气。本格主体达到幼株，
    /// 或四邻接任一格有达到幼株的主体，都算遮蔽，这样巨型株能为邻格提供庇护。
    /// </summary>
    public bool IsShaded
    {
        get
        {
            if (ShadesGround(subject))
                return true;
            for (int i = 0; i < NeighborPositions.Length; i++)
            {
                if (Manager == null || !Manager.TryGetCellAtTilemapPosition(tilemapPosition + NeighborPositions[i], out TileCell neighbor))
                    continue;
                if (ShadesGround(neighbor.subject))
                    return true;
            }

            return false;
        }
    }

    /// <summary>主体植物；至多一株。种子植物占用主体位，孢子植物中的苔藓与藻类占用地表覆盖。</summary>
    public PlantsBase Subject
    {
        get { return subject == null || subject.IsDead ? null : subject; }
    }

    public IReadOnlyList<PlantsBase> Cover
    {
        get { return cover; }
    }

    /// <summary>四邻接顺序固定为下、右、上、左，与策划案要求的可复现性一致。</summary>
    private static readonly Vector3Int[] NeighborPositions =
    {
        Vector3Int.down,
        Vector3Int.right,
        Vector3Int.up,
        Vector3Int.left
    };

    public IReadOnlyList<ICellEnv> Neighbors
    {
        get
        {
            var result = new List<ICellEnv>(NeighborPositions.Length);
            for (int i = 0; i < NeighborPositions.Length; i++)
            {
                if (Manager != null && Manager.TryGetCellAtTilemapPosition(tilemapPosition + NeighborPositions[i], out TileCell neighbor))
                    result.Add(neighbor);
            }

            return result;
        }
    }

    public bool TrySetSubject(PlantsBase plant)
    {
        if (plant == null || Subject != null)
            return false;
        subject = plant;
        return true;
    }

    public bool TryAddCover(PlantsBase plant)
    {
        if (plant == null)
            return false;
        for (int i = 0; i < cover.Count; i++)
        {
            if (cover[i] == plant)
                return false;
        }

        cover.Add(plant);
        return true;
    }

    public void Release(PlantsBase plant)
    {
        if (subject == plant)
            subject = null;
        cover.Remove(plant);
    }

    /// <summary>是否已经回收；回收的植物不再占用格子。</summary>
    public void SweepDeadPlants()
    {
        if (subject != null && subject.IsDead)
            subject = null;
        for (int i = cover.Count - 1; i >= 0; i--)
        {
            if (cover[i] == null || cover[i].IsDead)
                cover.RemoveAt(i);
        }
    }

    private static bool ShadesGround(PlantsBase plant)
    {
        return plant != null && !plant.IsDead && plant.IsShelter;
    }

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
        string plantsText = DescribePlants();
        return $"格子坐标：({coordinates.x}, {coordinates.y})\n"
            + $"当前天气：{weatherText}\n"
            + $"当前状态：{stateText}\n"
            + $"状态层数：{stateLayers}\n"
            + $"地表类型：{Surface}\n"
            + $"余屑：{detritus}\n"
            + $"遮蔽：{(IsShaded ? "有" : "无")}\n"
            + $"可占用：{(CanOccupy ? "是" : "否")}\n"
            + $"格子上的物体：{(occupyingObject != null ? occupyingObject.name : "预留")}\n"
            + $"是否可达：{(isReachable ? "是" : "预留")}\n"
            + plantsText;
    }

    /// <summary>列出本格的主体植物与地表覆盖，让玩家能直接回答"这里为什么长了这个"。</summary>
    private string DescribePlants()
    {
        SweepDeadPlants();
        var text = new System.Text.StringBuilder();
        if (Subject != null)
        {
            text.Append("【主体】").Append(Subject.DisplayName).Append('\n');
            text.Append("  ").Append(DescribeVitality(Subject)).Append('\n');
        }

        for (int i = 0; i < cover.Count; i++)
        {
            PlantsBase plant = cover[i];
            if (plant == null || plant.IsDead)
                continue;
            if (i == 0)
                text.Append("【地表覆盖】").Append(plant.DisplayName).Append('\n');
            else
                text.Append("  另有 ").Append(plant.DisplayName).Append('\n');
            text.Append("  ").Append(DescribeVitality(plant)).Append('\n');
        }

        return text.Length == 0 ? "本格没有植物\n" : text.ToString();
    }

    /// <summary>孢子植物显示活力，种子植物显示阶段和标记。</summary>
    private static string DescribeVitality(PlantsBase plant)
    {
        SporePlantsBase spore = plant as SporePlantsBase;
        if (spore != null)
        {
            string state = spore.IsWithering ? "，已枯萎" : string.Empty;
            return "活力 " + spore.Vitality + state
                + "，产出冷却 " + spore.ProduceCooldown
                + "，最近结算第 " + spore.LastSettledDay + " 天";
        }

        SeedPlantsBase seed = plant as SeedPlantsBase;
        if (seed != null)
        {
            return "阶段 " + seed.Stage
                + "，成长点 " + seed.GrowthPoints
                + "，水合 " + (seed.HasHydration ? "有" : "无")
                + "，光合 " + (seed.HasPhotosynthesis ? "有" : "无");
        }

        return plant.DisplayName;
    }
}
