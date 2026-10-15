using System.Collections.Generic;
using UnityEngine;

/// <summary>测试用的固定格子。记录余屑的写入并提供固定顺序的四邻接。</summary>
public class FakeCell : ICellEnv
{
    private readonly List<PlantsBase> cover = new List<PlantsBase>();

    public FakeCell(Vector2Int coordinates, bool isWet, int detritus = 0,
        E_SurfaceKind surface = E_SurfaceKind.Ground, bool isShaded = false)
    {
        Coordinates = coordinates;
        IsWet = isWet;
        Detritus = detritus;
        Surface = surface;
        IsShaded = isShaded;
    }

    public Vector2Int Coordinates { get; private set; }

    public bool IsWet { get; set; }

    public int Detritus { get; set; }

    public E_SurfaceKind Surface { get; set; }

    public bool IsShaded { get; set; }

    public bool CanOccupy
    {
        get
        {
            // 与 TileCell 一致：主体与覆盖都为空才算可用。
            if (Subject != null && !Subject.IsDead)
                return false;
            for (int i = 0; i < cover.Count; i++)
            {
                if (cover[i] != null && !cover[i].IsDead)
                    return false;
            }

            return true;
        }
    }

    /// <summary>四邻接顺序固定为下、右、上、左。</summary>
    public IReadOnlyList<ICellEnv> Neighbors { get; set; } = new List<ICellEnv>();

    /// <summary>按坐标计算真实四邻接，用于验证扩张裁决。</summary>
    public void LinkNeighbors(FakeCell down, FakeCell right, FakeCell up, FakeCell left)
    {
        var list = new List<ICellEnv>();
        if (down != null) list.Add(down);
        if (right != null) list.Add(right);
        if (up != null) list.Add(up);
        if (left != null) list.Add(left);
        Neighbors = list;
    }

    public PlantsBase Subject { get; private set; }

    public IReadOnlyList<PlantsBase> Cover
    {
        get { return cover; }
    }

    public bool TrySetSubject(PlantsBase plant)
    {
        if (Subject != null)
            return false;
        Subject = plant;
        return true;
    }

    public bool TryAddCover(PlantsBase plant)
    {
        cover.Add(plant);
        return true;
    }

    public void Release(PlantsBase plant)
    {
        if (Subject == plant)
            Subject = null;
        cover.Remove(plant);
    }
}