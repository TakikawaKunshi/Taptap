using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 植物看到的地图格子。TileCell 实现它，植物因此不需要知道 Tilemap、坐标方案或相机存在。
/// 格子层负责回答"这里湿不湿、有没有余屑、能不能被占"，植物只负责提出变更。
/// </summary>
public interface ICellEnv
{
    /// <summary>地图坐标系中的整数位置，坐标以中心格为原点。</summary>
    Vector2Int Coordinates { get; }

    /// <summary>当天是否湿润。策划案暂不记录干湿层数，因此直接由当天天气决定。</summary>
    bool IsWet { get; }

    /// <summary>该格的余屑数量；蘑菇与藻类在结算时读取或消耗它。</summary>
    int Detritus { get; set; }

    /// <summary>该格是否还能被新的植物占用；已经有主体或覆盖时为 false。</summary>
    bool CanOccupy { get; }

    /// <summary>该格的地表类型。策划案里苔藓可以生长在石面和木面上。</summary>
    E_SurfaceKind Surface { get; }

    /// <summary>
    /// 该格是否处于遮蔽中。遮蔽来自地图上的主体植物而不是天气，
    /// 因此由格子层根据主体植物的位置计算，蘑菇等物种据此判断繁菌条件。
    /// </summary>
    bool IsShaded { get; }

    /// <summary>
    /// 四邻接格子，顺序固定为下、右、上、左。
    /// 顺序固定是为了让扩张竞争的结果可复现，不依赖字典遍历顺序。
    /// </summary>
    IReadOnlyList<ICellEnv> Neighbors { get; }

    /// <summary>该格的主体植物，至多一个；成熟种子植物为相邻格提供遮蔽。</summary>
    PlantsBase Subject { get; }

    /// <summary>该格的地表覆盖，可以与主体共存，例如苔藓。</summary>
    IReadOnlyList<PlantsBase> Cover { get; }

    /// <summary>占用主体位；已经有主体时返回 false 且不修改。</summary>
    bool TrySetSubject(PlantsBase plant);

    /// <summary>占用地表覆盖位；已经有覆盖时返回 false 且不修改。</summary>
    bool TryAddCover(PlantsBase plant);

    /// <summary>移除一株植物占用的位置；植物被回收时调用。</summary>
    void Release(PlantsBase plant);
}