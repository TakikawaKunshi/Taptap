using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 苔藓。作为地表覆盖可以与主体植物共存，是蚯蚓和蜗牛的食物，也是炼化材料。
/// 雨天增长并向一个相邻空地申请扩张；晴天衰减，连续晴天会枯萎。
/// </summary>
[Serializable]
public class Moss : SporePlantsBase
{
    /// <summary>策划案：雨天活力+2。</summary>
    private const int GrowthPerRain = 2;

    /// <summary>策划案：晴天活力-1。</summary>
    private const int DecayPerSun = 1;

    /// <summary>策划案：连续晴天达到该次数时进入加速衰退。</summary>
    private const int WitherAfterSunnyNights = 2;

    /// <summary>策划案：连续晴天时在常规衰减之外再扣的点数，仍受 MaxDecayPerNight 限制。</summary>
    private const int ConsecutiveSunnyExtraDecay = 1;

    /// <summary>策划案第八章：投放的雨眠苔已经是成熟状态，初始活力最高。</summary>
    protected override int StartingVitality
    {
        get { return 5; }
    }

    protected override E_PlantSpecies SpeciesValue
    {
        get { return E_PlantSpecies.Moss; }
    }

    /// <summary>苔藓是地表覆盖，明确写出来以便后来者知道它可以与主体植物共存。</summary>
    public override E_OccupancySlot OccupancySlot
    {
        get { return E_OccupancySlot.Cover; }
    }

    /// <summary>
    /// 策划案把苔藓列为蜗牛与蚯蚓的食物，也是炼化疯长药水的必需材料。
    /// 取食由 SporePlantsBase 的 TakeBite 直接扣活力，这里只声明能否被吃。
    /// 活力归零后孢子植物会被下一次结算回收，所以那里已经不作为食物。
    /// </summary>
    public override int FoodAmount
    {
        get { return Vitality > 0 ? 1 : 0; }
    }

    /// <summary>茂密的苔藓可以为小型动物挡住捕食者的视线。</summary>
    public override bool IsShelter
    {
        get { return Vitality >= 2; }
    }

    public override string DisplayName
    {
        get { return "苔藓"; }
    }

    public override string RulePrefix
    {
        get { return "SPORE.MOSS"; }
    }

    protected override int RecycleAmount
    {
        get { return 1; }
    }

    protected override string MinimumRequirement
    {
        get { return "湿润地表、石面或木面"; }
    }

    protected override bool MeetsMinimum(ICellEnv cell)
    {
        return cell.IsWet || cell.Surface == E_SurfaceKind.Stone || cell.Surface == E_SurfaceKind.Wood;
    }

    protected override void BuildSpeciesPlan(PlantSettlePlan plan, int day, IWeatherQuery weather, ICellEnv cell)
    {
        if (!weather.Meets(E_PlantNeed.Moisture))
        {
            plan.VitalityDelta = -DecayPerSun;

            // 策划案：连续晴天可能枯萎。连续次数来自天气层，植物只判断阈值。
            if (weather.ConsecutiveSameDays < WitherAfterSunnyNights)
                return;

            // 加速后的总量是 -2，恰好等于 MaxDecayPerNight，不会突破策划案的每晚上限。
            plan.VitalityDelta = -DecayPerSun - ConsecutiveSunnyExtraDecay;
            plan.Log(RulePrefix + ".WITHER_SUN",
                DisplayName + " 连续 " + weather.ConsecutiveSameDays + " 个晴天，加速衰退。");
            return;
        }

        plan.VitalityDelta = GrowthPerRain;

        // 策划案：雨天向 1 个相邻空地扩张。目标格由结算方在固定顺序的邻接中裁决，
        // 因此这里只提出申请，不自己挑选格子。
        // 是否还有空格要检查邻接而不是本格：本格一定有自己这株苔藓。
        if (!HasFreeNeighbor(cell))
            return;

        plan.RequestsExpansion = true;
    }

    /// <summary>四邻接中是否存在还能被占用的格子。</summary>
    private static bool HasFreeNeighbor(ICellEnv cell)
    {
        IReadOnlyList<ICellEnv> neighbors = cell.Neighbors;
        for (int i = 0; i < neighbors.Count; i++)
        {
            if (neighbors[i] != null && neighbors[i].CanOccupy)
                return true;
        }

        return false;
    }
}