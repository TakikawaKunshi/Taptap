using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 按物种实例化用户配置的植物预设体；未配置的物种不创建显示物体。
/// 结算事件与低频轮询只同步显示，不修改植物逻辑数据。
/// </summary>
[DisallowMultipleComponent]
public class PlantVisualBinder : MonoBehaviour
{
    [SerializeField] private TileMapManager map;
    [SerializeField] private PlantSettlementBridge bridge;

    [Header("物种预设体（留空不创建，外观由预设体决定）")]
    [SerializeField, InspectorName("藻类 (Algae)")] private GameObject algaePrefab;
    [SerializeField, InspectorName("苔藓 (Moss)")] private GameObject mossPrefab;
    [SerializeField, InspectorName("蘑菇 (Mushroom)")] private GameObject mushroomPrefab;
    [SerializeField, InspectorName("裸子植物 (Gymnosperm)")] private GameObject gymnospermPrefab;
    [SerializeField, InspectorName("被子植物 (Angiosperm)")] private GameObject angiospermPrefab;

    [Header("蘑菇活力表现")]
    [SerializeField, Min(1), Tooltip("达到此活力时使用蘑菇预设体的原始大小；低于此值缩小，高于此值放大。")]
    private int mushroomReferenceVitality = 5;

    [Header("余屑显示")]
    [SerializeField, Tooltip("自行挂载余屑预设体。每个有余屑的格子显示一堆，余屑耗尽时移除；留空只保留资源数据。")]
    private GameObject detritusPrefab;
    [SerializeField, Tooltip("余屑相对格子中心的世界坐标偏移，便于调整贴地位置。")]
    private Vector3 detritusOffset;

    [Header("同步")]
    [SerializeField, Min(0.05f), Tooltip("轮询间隔（秒），兜住没有发事件的种植路径。")]
    private float syncInterval = 0.25f;
    [SerializeField, Tooltip("同一格多个覆盖层沿对角线错开的间距。")]
    private float coverSpacing = 0.16f;

    private readonly Dictionary<PlantsBase, PlantVisual> visuals
        = new Dictionary<PlantsBase, PlantVisual>();
    private readonly HashSet<PlantsBase> seen = new HashSet<PlantsBase>();
    private readonly List<PlantsBase> gone = new List<PlantsBase>();
    private readonly Dictionary<TileCell, GameObject> detritusVisuals = new Dictionary<TileCell, GameObject>();
    private readonly HashSet<TileCell> seenDetritus = new HashSet<TileCell>();
    private readonly List<TileCell> goneDetritus = new List<TileCell>();
    private GameObject displayedDetritusPrefab;
    private float nextSync;

    /// <summary>当前已经实例化的植物，供调试菜单与测试核对。</summary>
    public IReadOnlyDictionary<PlantsBase, PlantVisual> Visuals => visuals;
    public IReadOnlyDictionary<TileCell, GameObject> DetritusVisuals => detritusVisuals;

    public TileMapManager Map
    {
        get
        {
            if (map == null)
                map = FindObjectOfType<TileMapManager>();
            return map;
        }
    }

    public GameObject GetPrefab(E_PlantSpecies species)
    {
        switch (species)
        {
            case E_PlantSpecies.Algae: return algaePrefab;
            case E_PlantSpecies.Moss: return mossPrefab;
            case E_PlantSpecies.Mushroom: return mushroomPrefab;
            case E_PlantSpecies.Gymnosperm: return gymnospermPrefab;
            case E_PlantSpecies.Angiosperm: return angiospermPrefab;
            default: return null;
        }
    }

    private void OnEnable()
    {
        if (bridge != null)
            bridge.Settled += OnSettled;
        SyncNow();
    }

    private void OnDisable()
    {
        if (bridge != null)
            bridge.Settled -= OnSettled;
    }

    private void Update()
    {
        if (!Application.isPlaying || Time.realtimeSinceStartup < nextSync)
            return;
        SyncNow();
    }

    private void OnSettled(PlantSettleReport report)
    {
        SyncNow();
    }

    /// <summary>同步实例、位置和名称；更换或清空预设体配置也会在下次同步时生效。</summary>
    public void SyncNow()
    {
        nextSync = Time.realtimeSinceStartup + syncInterval;
        if (map == null)
            map = FindObjectOfType<TileMapManager>();
        if (map == null)
            return;

        if (displayedDetritusPrefab != detritusPrefab)
        {
            foreach (GameObject visual in detritusVisuals.Values)
                RemoveObject(visual);
            detritusVisuals.Clear();
            displayedDetritusPrefab = detritusPrefab;
        }

        seen.Clear();
        seenDetritus.Clear();
        foreach (TileCell cell in map.Cells)
        {
            if (cell == null)
                continue;

            TouchDetritus(cell);
            PlantsBase subject = cell.Subject;
            if (subject != null && !subject.IsDead)
                Touch(subject, cell, true, 0);

            IReadOnlyList<PlantsBase> cover = cell.Cover;
            for (int i = 0; i < cover.Count; i++)
            {
                PlantsBase coverPlant = cover[i];
                if (coverPlant == null || coverPlant.IsDead)
                    continue;
                Touch(coverPlant, cell, false, i);
            }
        }

        // 回收、移走或清空预设体配置的植物，连同显示物体一起清理。
        gone.Clear();
        foreach (KeyValuePair<PlantsBase, PlantVisual> pair in visuals)
        {
            if (!seen.Contains(pair.Key))
                gone.Add(pair.Key);
        }
        for (int i = 0; i < gone.Count; i++)
        {
            RemoveVisual(visuals[gone[i]]);
            visuals.Remove(gone[i]);
        }

        goneDetritus.Clear();
        foreach (TileCell cell in detritusVisuals.Keys)
            if (!seenDetritus.Contains(cell))
                goneDetritus.Add(cell);
        foreach (TileCell cell in goneDetritus)
        {
            RemoveObject(detritusVisuals[cell]);
            detritusVisuals.Remove(cell);
        }
    }

    [ContextMenu("立即同步一次")]
    private void SyncFromMenu()
    {
        SyncNow();
    }

    private void Touch(PlantsBase plant, TileCell cell, bool subject, int coverIndex)
    {
        GameObject prefab = GetPrefab(plant.Species);
        if (prefab == null)
            return;

        seen.Add(plant);

        PlantVisual visual;
        visuals.TryGetValue(plant, out visual);
        if (visual != null && visual.SourcePrefab != prefab)
        {
            RemoveVisual(visual);
            visual = null;
        }
        if (visual == null)
        {
            GameObject go = Instantiate(prefab, transform, false);
            visual = go.GetComponent<PlantVisual>();
            if (visual == null)
                visual = go.AddComponent<PlantVisual>();
            visual.Bind(plant, prefab);
            visuals[plant] = visual;
        }

        // 蘑菇按活力缩放；其他外观属性保持预设体配置。
        visual.transform.position = cell.WorldPosition + OffsetFor(subject, coverIndex);
        visual.Refresh(mushroomReferenceVitality);
    }

    private void TouchDetritus(TileCell cell)
    {
        if (detritusPrefab == null || cell.Detritus <= 0)
            return;

        seenDetritus.Add(cell);
        if (!detritusVisuals.TryGetValue(cell, out GameObject visual) || visual == null)
        {
            visual = Instantiate(detritusPrefab, transform, false);
            detritusVisuals[cell] = visual;
        }
        visual.transform.position = cell.WorldPosition + detritusOffset;
        visual.name = "余屑 ×" + cell.Detritus + " " + cell.Coordinates;
    }

    private Vector3 OffsetFor(bool subject, int coverIndex)
    {
        if (subject)
            return Vector3.zero;

        float step = coverSpacing * (coverIndex + 1);
        return new Vector3(-step, step, 0f);
    }

    private void RemoveVisual(PlantVisual visual)
    {
        if (visual != null)
            RemoveObject(visual.gameObject);
    }

    private void RemoveObject(GameObject visual)
    {
        if (visual == null)
            return;

        visual.SetActive(false);
        if (Application.isPlaying)
            Destroy(visual);
        else
            DestroyImmediate(visual);
    }
}
