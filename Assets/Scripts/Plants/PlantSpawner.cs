using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 植物投放与调试入口。
/// 首版没有植物卡片 UI，因此通过 Inspector 菜单按策划案第八章的建议初始配置铺场。
/// 动物与天气不参与本脚本的职责，它只负责把植物放到格子上并驱动一次植物结算。
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Ecology/Plant Spawner")]
public class PlantSpawner : MonoBehaviour
{
    [Serializable]
    public struct Placement
    {
        [Tooltip("相对地图中心的整数格坐标。")]
        public Vector2Int coordinates;

        [Tooltip("要放置的物种。")]
        public E_PlantSpecies species;
    }

    [Header("引用")]
    [SerializeField, Tooltip("场景中的 TileMapManager；留空时自动查找。")]
    private TileMapManager map;

    [Header("策划案建议的初始配置")]
    [SerializeField, Tooltip("按《七日生态箱系统策划案》第八章的建议初始配置摆放植物。")]
    private List<Placement> initialLayout = new List<Placement>
    {
        new Placement { coordinates = new Vector2Int(-2, -1), species = E_PlantSpecies.Moss },          // 雨眠苔，成熟，位于低处
        new Placement { coordinates = new Vector2Int(1, 0), species = E_PlantSpecies.Mushroom },      // 小蘑菇，幼体，旁边有余屑
        new Placement { coordinates = new Vector2Int(-1, 1), species = E_PlantSpecies.Angiosperm },   // 向日铃花种子
        new Placement { coordinates = new Vector2Int(2, 1), species = E_PlantSpecies.Gymnosperm }     // 星壳针木种子
    };

    [Header("额外初始余屑")]
    [SerializeField, Tooltip("策划案：蘑菇旁边有 1 余屑；这里为所有指定格子铺余屑。")]
    private List<Vector2Int> detritusSeeds = new List<Vector2Int>
    {
        new Vector2Int(1, 0)
    };

    private int lastPlacedCount;

    public TileMapManager Map
    {
        get
        {
            if (map == null)
                map = FindObjectOfType<TileMapManager>();
            return map;
        }
    }

    /// <summary>最近一次铺场实际放置的植物数量。</summary>
    public int LastPlacedCount
    {
        get { return lastPlacedCount; }
    }

    /// <summary>
    /// 按策划案建议的初始配置铺设植物。
    /// 重复调用不会重复放置：目标格已被占用时直接跳过，不会覆盖已有植物。
    /// </summary>
    [ContextMenu("铺设策划案初始配置")]
    public void SpawnInitialLayout()
    {
        TileMapManager manager = Map;
        if (manager == null)
        {
            Debug.LogWarning("PlantSpawner：场景里没有 TileMapManager，无法铺设初始配置。", this);
            return;
        }

        manager.InitializeCells();
        if (manager.CellCount == 0)
        {
            Debug.LogWarning("PlantSpawner：地图还没有有效格子，先确认 Tilemap 上已绘制 Tile。", this);
            return;
        }

        lastPlacedCount = 0;
        int skipped = 0;
        for (int i = 0; i < initialLayout.Count; i++)
        {
            Placement placement = initialLayout[i];
            if (!manager.TryGetCell(placement.coordinates, out TileCell cell))
            {
                skipped++;
                continue;
            }

            if (!cell.CanOccupy)
            {
                skipped++;
                continue;
            }

            if (!PlantSettler.Place(PlantSettler.Create(placement.species), cell))
                skipped++;
            else
                lastPlacedCount++;
        }

        for (int i = 0; i < detritusSeeds.Count; i++)
        {
            if (manager.TryGetCell(detritusSeeds[i], out TileCell cell))
                cell.Detritus += 1;
        }

        Debug.Log("PlantSpawner：已铺设 " + lastPlacedCount + " 株植物，跳过 " + skipped + " 格"
            + "（格子不存在或已被占用）。", this);
    }

    /// <summary>清空全部植物与余屑，回到开局状态；用于反复验证七天流程。</summary>
    [ContextMenu("清空全部植物")]
    public void ClearAllPlants()
    {
        TileMapManager manager = Map;
        if (manager == null)
            return;

        foreach (TileCell cell in manager.Cells)
        {
            cell.Release(cell.Subject);
            IReadOnlyList<PlantsBase> cover = cell.Cover;
            for (int i = cover.Count - 1; i >= 0; i--)
                cell.Release(cover[i]);
            cell.Detritus = 0;
        }

        lastPlacedCount = 0;
        Debug.Log("PlantSpawner：已清空全部植物与余屑。", this);
    }
}