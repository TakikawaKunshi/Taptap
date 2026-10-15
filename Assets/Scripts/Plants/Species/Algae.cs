using System;
using UnityEngine;

/// <summary>
/// 藻类。附着在积水或湿润地表上，是雨印的水生基础，也是鱼类的食物来源。
/// 雨天增长，有余屑时额外产出光粒；晴天衰减。
/// </summary>
[Serializable]
public class Algae : SporePlantsBase
{
    /// <summary>策划案：雨天活力+2。</summary>
    private const int GrowthPerRain = 2;

    /// <summary>策划案：晴天活力-1。</summary>
    private const int DecayPerSun = 1;

    /// <summary>活力达到该值后视为成熟，才能消耗余屑产出光粒。策划案未给出数值，暂定 4。</summary>
    private const int MatureVitality = 4;

    protected override E_PlantSpecies SpeciesValue
    {
        get { return E_PlantSpecies.Algae; }
    }

    /// <summary>藻类附着在积水或湿润地表上，属于地表覆盖，不与主体植物争位。</summary>
    public override E_OccupancySlot OccupancySlot
    {
        get { return E_OccupancySlot.Cover; }
    }

    /// <summary>策划案把藻类列为蜗牛的食物，也作为水生动物的基础饵料。</summary>
    public override int FoodAmount
    {
        get { return Vitality > 0 ? 1 : 0; }
    }

    public override string DisplayName
    {
        get { return "藻类"; }
    }

    public override string RulePrefix
    {
        get { return "SPORE.ALGAE"; }
    }

    protected override int RecycleAmount
    {
        get { return 1; }
    }

    protected override string MinimumRequirement
    {
        get { return "积水或湿润地表"; }
    }

    protected override bool MeetsMinimum(ICellEnv cell)
    {
        return cell.IsWet;
    }

    protected override void BuildSpeciesPlan(PlantSettlePlan plan, int day, IWeatherQuery weather, ICellEnv cell)
    {
        if (!weather.Meets(E_PlantNeed.Moisture))
        {
            plan.VitalityDelta = -DecayPerSun;
            return;
        }

        plan.VitalityDelta = GrowthPerRain;

        // 策划案：有余屑时额外生成 1 份光粒。未成熟时只增长，不消耗余屑。
        if (cell.Detritus <= 0 || Vitality < MatureVitality)
            return;

        plan.ProduceKind = E_ResourceKind.Light;
        plan.ProduceAmount = 1;
        plan.DetritusDelta = -1;
        plan.Log(RulePrefix + ".LIGHT", DisplayName + " 消耗 1 份余屑，产出 1 份光粒。");
    }
}