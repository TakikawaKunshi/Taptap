using System.Collections.Generic;
using UnityEngine;

/// <summary>测试用的多格地图。邻接按坐标真实计算，因此扩张裁决可以被完整验证。</summary>
public class FakeMap
{
    private readonly Dictionary<Vector2Int, FakeCell> cells = new Dictionary<Vector2Int, FakeCell>();

    public IReadOnlyCollection<FakeCell> Cells
    {
        get { return cells.Values; }
    }

    public FakeCell Add(Vector2Int coordinates, bool isWet = true, int detritus = 0,
        E_SurfaceKind surface = E_SurfaceKind.Ground, bool isShaded = false)
    {
        var cell = new FakeCell(coordinates, isWet, detritus, surface, isShaded);
        cells[coordinates] = cell;
        return cell;
    }

    public FakeCell Get(Vector2Int coordinates)
    {
        cells.TryGetValue(coordinates, out FakeCell cell);
        return cell;
    }

    public FakeCell GetOrAdd(Vector2Int coordinates, bool isWet = true)
    {
        if (cells.TryGetValue(coordinates, out FakeCell cell))
            return cell;
        return Add(coordinates, isWet);
    }

    /// <summary>把列表洗成一个新列表，用来验证"与遍历顺序无关"。</summary>
    public List<FakeCell> Shuffled(System.Random random)
    {
        var list = new List<FakeCell>(cells.Values);
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            FakeCell tmp = list[i];
            list[i] = list[j];
            list[j] = tmp;
        }

        return list;
    }
}