using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 动物在结算时使用的世界视图。
///
/// 它把当天的事实（格子、天气、每格上有哪些动物、两格之间的步数）预先算好，
/// 让动物本身只需要回答"我想去哪、想做什么"，不需要自己去遍历地图，
/// 因此所有动物看到的是同一份快照，不会因为各自查询顺序不同而产生不同的结果。
///
/// 与植物的 PlantWeatherCondition 同理：它只汇总事实、不替动物做决定，
/// 也不依赖 UnityEngine 对象，所以整套逻辑可以在 EditMode 下直接跑。
/// </summary>
public sealed class AnimalWorldView : IAnimalWorld
{
    private readonly IWeatherQuery weather;
    private readonly List<ICellEnv> cells;
    private readonly Dictionary<ICellEnv, List<AnimalsBase>> occupancy;
    private readonly Dictionary<ICellEnv, Dictionary<ICellEnv, int>> distances;

    public AnimalWorldView(IWeatherQuery weather, IEnumerable<ICellEnv> cells, IEnumerable<AnimalsBase> animals)
    {
        if (weather == null)
            throw new System.ArgumentNullException(nameof(weather));

        this.weather = weather;
        this.cells = new List<ICellEnv>();
        occupancy = new Dictionary<ICellEnv, List<AnimalsBase>>(ReferenceComparer.Instance);
        distances = new Dictionary<ICellEnv, Dictionary<ICellEnv, int>>(ReferenceComparer.Instance);

        if (cells != null)
        {
            foreach (ICellEnv cell in cells)
            {
                if (cell != null)
                    this.cells.Add(cell);
            }
        }

        if (animals != null)
        {
            foreach (AnimalsBase animal in animals)
            {
                if (animal == null || animal.IsDead || animal.CurrentCell == null)
                    continue;

                List<AnimalsBase> list;
                if (!occupancy.TryGetValue(animal.CurrentCell, out list))
                {
                    list = new List<AnimalsBase>();
                    occupancy[animal.CurrentCell] = list;
                }

                list.Add(animal);
            }
        }
    }

    public bool Meets(E_PlantNeed need) => weather.Meets(need);

    public int ConsecutiveSameDays => weather.ConsecutiveSameDays;

    public IReadOnlyList<ICellEnv> Cells => cells;

    public IReadOnlyList<AnimalsBase> AnimalsAt(ICellEnv cell)
    {
        List<AnimalsBase> list;
        return occupancy.TryGetValue(cell, out list) ? list : EmptyAnimals;
    }

    /// <summary>
    /// 能否进入某一格。
    /// 动物可以走进有苔藓或蘑菇的格子；只有被成熟植株占据的格子才构成障碍，
    /// 此时需要攀爬或穴居标签才能通过。这是活动标签唯一影响通行的地方。
    /// </summary>
    public bool CanEnter(AnimalsBase animal, ICellEnv cell)
    {
        if (animal == null || cell == null)
            return false;
        if (cell.Subject == null || cell.Subject.IsDead)
            return true;

        return animal.HasTag(E_ActivityTag.Climbing) || animal.HasTag(E_ActivityTag.Burrowing);
    }

    /// <summary>
    /// 两格之间的步数。同格为 0，相邻为 1，不连通为 -1。
    /// 距离只描述地图本身的连通关系，不代入个体能否通过——
    /// 否则同一个距离查询会因为查询者不同而变化，破坏可复现性。
    /// </summary>
    public int StepDistance(ICellEnv from, ICellEnv to)
    {
        if (from == null || to == null)
            return -1;
        if (ReferenceEquals(from, to))
            return 0;

        Dictionary<ICellEnv, int> map;
        if (!distances.TryGetValue(from, out map))
        {
            map = BreadthFirstSearch(from);
            distances[from] = map;
        }

        int value;
        return map.TryGetValue(to, out value) ? value : -1;
    }

    /// <summary>把一只动物登记到新格子上；移动到目标格后必须调用，否则后续查询会看到过期位置。</summary>
    public void MoveAnimal(AnimalsBase animal, ICellEnv from, ICellEnv to)
    {
        if (animal == null)
            return;

        List<AnimalsBase> list;
        if (from != null && occupancy.TryGetValue(from, out list))
        {
            list.Remove(animal);
            if (list.Count == 0)
                occupancy.Remove(from);
        }

        if (to == null)
            return;

        if (!occupancy.TryGetValue(to, out list))
        {
            list = new List<AnimalsBase>();
            occupancy[to] = list;
        }

        list.Add(animal);
    }

    public void RemoveAnimal(AnimalsBase animal, ICellEnv cell)
    {
        if (animal == null)
            return;

        List<AnimalsBase> list;
        if (cell != null && occupancy.TryGetValue(cell, out list))
        {
            list.Remove(animal);
            if (list.Count == 0)
                occupancy.Remove(cell);
        }
    }

    /// <summary>从起点做一次广度优先搜索，得到到所有可达格子的最少步数。</summary>
    private Dictionary<ICellEnv, int> BreadthFirstSearch(ICellEnv start)
    {
        var result = new Dictionary<ICellEnv, int>(ReferenceComparer.Instance) { { start, 0 } };
        var queue = new Queue<ICellEnv>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            ICellEnv current = queue.Dequeue();
            int next = result[current] + 1;

            IReadOnlyList<ICellEnv> neighbors = current.Neighbors;
            for (int i = 0; i < neighbors.Count; i++)
            {
                ICellEnv neighbor = neighbors[i];
                if (neighbor == null || result.ContainsKey(neighbor))
                    continue;

                result[neighbor] = next;
                queue.Enqueue(neighbor);
            }
        }

        return result;
    }

    private static readonly AnimalsBase[] EmptyAnimals = new AnimalsBase[0];

    /// <summary>格子与动物都要求"同一个对象"，用引用比较避免任何自定义的相等逻辑干扰结算。</summary>
    private sealed class ReferenceComparer : IEqualityComparer<ICellEnv>
    {
        public static readonly ReferenceComparer Instance = new ReferenceComparer();

        public bool Equals(ICellEnv left, ICellEnv right) => ReferenceEquals(left, right);

        public int GetHashCode(ICellEnv cell) => cell.GetHashCode();
    }
}
