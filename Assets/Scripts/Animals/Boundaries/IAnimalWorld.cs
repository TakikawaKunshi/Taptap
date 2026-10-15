using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 动物看到的那一晚的世界。
///
/// 与植物的 <see cref="IWeatherQuery"/> 同理：只暴露"这里有什么"和"能不能过去"，
/// 不暴露地图与天气的实现，动物逻辑因此可以在不启动 WeatherBase 的情况下单独测试。
/// 策划案要求动物"寻找资源、取食、捕食、躲藏和搬运"，所以这里提供
/// 格子、天气、按格索引的动物与精确的距离查询，但不替动物做决定。
/// </summary>
public interface IAnimalWorld
{
    /// <summary>当天的天气是否成立；动物据此改变活动标签评分。</summary>
    bool Meets(E_PlantNeed need);

    /// <summary>连续同一天气的天数。</summary>
    int ConsecutiveSameDays { get; }

    /// <summary>今天到目的地的可选格子集合（已地图内且可放置动物）。</summary>
    IReadOnlyList<ICellEnv> Cells { get; }

    /// <summary>本格上存活的动物，顺序固定，便于可复现。</summary>
    IReadOnlyList<AnimalsBase> AnimalsAt(ICellEnv cell);

    /// <summary>借助洞/攀爬等标签时用于判断能否进入某格。</summary>
    bool CanEnter(AnimalsBase animal, ICellEnv cell);

    /// <summary>两格之间的步数；返回 1 表示相邻，0 表示同格，负数表示不连通。</summary>
    int StepDistance(ICellEnv from, ICellEnv to);
}
