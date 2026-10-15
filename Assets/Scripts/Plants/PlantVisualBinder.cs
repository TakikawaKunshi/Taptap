using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 把地图里的植物同步成可见的占位物体。
///
/// 同步走双通道：结算完成事件 + 低频轮询。铺场、拖放、扩张、结算，
/// 无论植物从哪条路进的地图都会被看到；即使某个入口忘了发事件，轮询也会兜住。
/// 同步只读地图与植物，不写任何逻辑数据，因此多跑几次不会影响结算结果。
/// </summary>
[DisallowMultipleComponent]
public class PlantVisualBinder : MonoBehaviour
{
    // 全局绘制顺序：地形 0、覆盖植物 1、主体植物 2、动物 3、携带物 4，均低于 UI。
    // 使用常量，避免场景中旧的 20 / 30 排序配置覆盖代码中的新值。
    private const int CoverOrder = 1;
    private const int SubjectOrder = 2;

    [SerializeField] private TileMapManager map;
    [SerializeField] private PlantSettlementBridge bridge;
    [SerializeField, Tooltip("临时用图；换正式美术时直接替换这张贴图即可。")]
    private Texture2D plantTexture;
    [SerializeField, Tooltip("留空时自动找 URP 的 2D 精灵着色器；AddComponent 的默认材质在 URP 下会显示为粉色。")]
    private Material spriteMaterial;
    [SerializeField, Min(0.05f), Tooltip("轮询间隔（秒），兜住没有发事件的种植路径。")]
    private float syncInterval = 0.25f;
    [SerializeField, Tooltip("同一格多个覆盖层沿对角线错开的间距。")]
    private float coverSpacing = 0.16f;

    private readonly Dictionary<PlantsBase, PlantVisual> visuals
        = new Dictionary<PlantsBase, PlantVisual>();
    private readonly HashSet<PlantsBase> seen = new HashSet<PlantsBase>();
    private readonly List<PlantsBase> gone = new List<PlantsBase>();
    private Sprite cachedSprite;
    private float nextSync;

    /// <summary>当前已经画出来的植物，供调试菜单与测试核对。</summary>
    public IReadOnlyDictionary<PlantsBase, PlantVisual> Visuals
    {
        get { return visuals; }
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

    /// <summary>立刻按地图现状重画一遍。公开出来便于铺场后马上调用。</summary>
    public void SyncNow()
    {
        nextSync = Time.realtimeSinceStartup + syncInterval;
        if (map == null)
            map = FindObjectOfType<TileMapManager>();
        if (map == null)
            return;

        seen.Clear();
        foreach (TileCell cell in map.Cells)
        {
            if (cell == null)
                continue;

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

        // 地图上已经找不到的植物（回收、被动物搬走等）连同物体一起清掉。
        gone.Clear();
        foreach (KeyValuePair<PlantsBase, PlantVisual> pair in visuals)
        {
            if (!seen.Contains(pair.Key))
                gone.Add(pair.Key);
        }
        for (int i = 0; i < gone.Count; i++)
        {
            PlantVisual visual;
            if (!visuals.TryGetValue(gone[i], out visual))
                continue;
            if (visual != null)
                Destroy(visual.gameObject);
            visuals.Remove(gone[i]);
        }
    }

    [ContextMenu("立即同步一次")]
    private void SyncFromMenu()
    {
        SyncNow();
    }

    private void Touch(PlantsBase plant, TileCell cell, bool subject, int coverIndex)
    {
        seen.Add(plant);

        PlantVisual visual;
        if (!visuals.TryGetValue(plant, out visual) || visual == null)
        {
            GameObject go = new GameObject(plant.DescribeShort());
            go.transform.SetParent(transform, false);
            visual = go.AddComponent<PlantVisual>();
            // 默认材质是内置管线的 Sprites-Default，URP 下会粉红；先换成项目在用的 2D 精灵材质。
            visual.GetComponent<SpriteRenderer>().sharedMaterial = ResolveMaterial();
            visual.Bind(plant, ResolveSprite(), subject ? SubjectOrder : CoverOrder, subject);
            visuals[plant] = visual;
        }

        visual.transform.position = cell.WorldPosition + OffsetFor(subject, coverIndex);
        visual.Refresh();
    }

    private Vector3 OffsetFor(bool subject, int coverIndex)
    {
        if (subject)
            return Vector3.zero;

        // 覆盖层沿左上对角线错开：苔藓、藻类可以和主体共存，也允许同格多层覆盖。
        float step = coverSpacing * (coverIndex + 1);
        return new Vector3(-step, step, 0f);
    }

    /// <summary>解析项目在用的 2D 精灵材质；Builder 已经绑过就直接用。</summary>
    private Material ResolveMaterial()
    {
        if (spriteMaterial != null)
            return spriteMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogWarning("PlantVisualBinder：找不到可用的精灵着色器，植物可能显示为粉色。", this);
            return null;
        }

        spriteMaterial = new Material(shader);
        return spriteMaterial;
    }

    /// <summary>
    /// 临时图是竖版插画，直接显示会变成一条细长的横幅。
    /// 这里裁出中央偏上的方形再建 Sprite：人像通常在中上部，裁出来辨识度最高。
    /// 只裁一次，之后所有植株共用同一份 Sprite。
    /// </summary>
    private Sprite ResolveSprite()
    {
        if (cachedSprite != null)
            return cachedSprite;
        if (plantTexture == null)
            return null;

        try
        {
            int side = Mathf.Min(plantTexture.width, plantTexture.height);
            int x = (plantTexture.width - side) / 2;
            int y = Mathf.Min(plantTexture.height - side, (int)(plantTexture.height * 0.2f));
            Color[] pixels = plantTexture.GetPixels(x, y, side, side);

            Texture2D cropped = new Texture2D(side, side, TextureFormat.RGBA32, false);
            cropped.SetPixels(pixels);
            cropped.Apply();
            cropped.filterMode = FilterMode.Bilinear;

            cachedSprite = Sprite.Create(cropped, new Rect(0, 0, side, side),
                new Vector2(0.5f, 0.5f), side);
            cachedSprite.name = plantTexture.name + "_crop";
            return cachedSprite;
        }
        catch (UnityException)
        {
            // 贴图没开 Read/Write 时 GetPixels 会抛异常；不阻断游戏，只是看不到植物。
            Debug.LogWarning("PlantVisualBinder：贴图 " + plantTexture.name
                             + " 未开启 Read/Write，无法生成占位图。请在导入设置里勾选。", this);
            return null;
        }
    }
}
