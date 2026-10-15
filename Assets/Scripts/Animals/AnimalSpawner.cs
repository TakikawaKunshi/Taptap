using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 动物投放器。对应策划案第八章"建议初始配置"里的动物部分：
/// 青蛙、蚯蚓、蜗牛、蚂蚁、蜘蛛各一只，初始饱腹 2，分别落在湿格、隐蔽点等起始位置上。
///
/// 与 PlantSpawner 的分工不同：植物占格并由 TileMapManager 持有，
/// 而动物会移动、不属于任何格子，所以它们统一由本组件登记在洞口一份列表里，
/// 地图层不需要为动物增加任何字段。
/// </summary>
public class AnimalSpawner : MonoBehaviour
{
    [Serializable]
    public struct Placement
    {
        [Tooltip("相对地图中心的整数格坐标。")]
        public Vector2Int coordinates;

        [Tooltip("投放的物种；首版只支持 MVP 五种。")]
        public E_AnimalSpecies species;

        [Tooltip("初始饱腹，策划案建议 2。")]
        public int satiety;

        public Placement(Vector2Int coordinates, E_AnimalSpecies species, int satiety)
        {
            this.coordinates = coordinates;
            this.species = species;
            this.satiety = satiety;
        }
    }

    [Header("引用")]
    [SerializeField, Tooltip("动物所在的地图；已经有格子后才能投放。")]
    private TileMapManager map;

    [Header("初始配置")]
    [SerializeField, Tooltip("策划案第八章的开局配置：五种 MVP 动物各一只。")]
    private List<Placement> initialLayout = new List<Placement>
    {
        new Placement(new Vector2Int(0, -1), E_AnimalSpecies.Frog, 2),
        new Placement(new Vector2Int(1, 1), E_AnimalSpecies.Earthworm, 2),
        new Placement(new Vector2Int(-1, 0), E_AnimalSpecies.Snail, 2),
        new Placement(new Vector2Int(2, 0), E_AnimalSpecies.Ant, 2),
        new Placement(new Vector2Int(-2, 1), E_AnimalSpecies.Spider, 2)
    };

    private readonly List<AnimalsBase> animals = new List<AnimalsBase>();
    private int lastPlacedCount;

    /// <summary>登记在案的存活动物。</summary>
    public IReadOnlyList<AnimalsBase> Animals => animals;

    /// <summary>还活着的动物数量。</summary>
    public int LiveCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < animals.Count; i++)
            {
                if (animals[i] != null && !animals[i].IsDead)
                    count++;
            }

            return count;
        }
    }

    /// <summary>最近一次投放成功放置的数量。</summary>
    public int LastPlacedCount => lastPlacedCount;

    /// <summary>
    /// 按策划案建议的初始配置投放动物。
    /// 重复调用不会重复放置：同格已有同种动物时跳过，不覆盖已有个体。
    /// </summary>
    [ContextMenu("铺设策划案初始动物")]
    public void SpawnInitialLayout()
    {
        TileMapManager manager = Map;
        if (manager == null)
        {
            Debug.LogWarning("AnimalSpawner：场景里没有 TileMapManager，无法投放动物。", this);
            return;
        }

        manager.InitializeCells();
        if (manager.CellCount == 0)
        {
            Debug.LogWarning("AnimalSpawner：地图还没有有效格子，请先在 ECB 上绘制 Tile。", this);
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

            if (HasSameSpeciesHere(cell, placement.species))
            {
                skipped++;
                continue;
            }

            try
            {
                AnimalsBase animal = AnimalSettler.Create(placement.species, placement.satiety);
                AnimalSettler.Place(animal, cell);
                animals.Add(animal);
                lastPlacedCount++;
            }
            catch (Exception ex)
            {
                // 扩展物种还没实现，或者该格已经有动物时，给出可读提示而不是整段中断。
                Debug.LogWarning("AnimalSpawner：投放 " + placement.species + " 失败：" + ex.Message, this);
                skipped++;
            }
        }

        Debug.Log("AnimalSpawner：已投放 " + lastPlacedCount + " 只动物，跳过 " + skipped
                  + " 个（格子不存在或已有同种动物）。", this);
    }

    /// <summary>清空全部动物，回到开局状态；用于反复验证七天流程。</summary>
    [ContextMenu("清空全部动物")]
    public void ClearAllAnimals()
    {
        animals.Clear();
        lastPlacedCount = 0;
        Debug.Log("AnimalSpawner：已清空全部动物。", this);
    }

    /// <summary>把已经死亡个体移出登记表，避免它们继续占着列表。</summary>
    public void RemoveDead()
    {
        for (int i = animals.Count - 1; i >= 0; i--)
        {
            if (animals[i] != null && animals[i].IsDead)
                animals.RemoveAt(i);
        }
    }

    /// <summary>当前使用的地图；与 PlantSpawner 一样留空时自动查找，便于校验接线。</summary>
    public TileMapManager Map
    {
        get
        {
            if (map == null)
                map = FindObjectOfType<TileMapManager>();
            return map;
        }
    }

    private bool HasSameSpeciesHere(TileCell cell, E_AnimalSpecies species)
    {
        for (int i = 0; i < animals.Count; i++)
        {
            AnimalsBase animal = animals[i];
            if (animal == null || animal.IsDead)
                continue;
            if (animal.CurrentCell == cell && animal.Species == species)
                return true;
        }

        return false;
    }
}
