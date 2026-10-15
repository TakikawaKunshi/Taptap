using System;
using UnityEngine;

/// <summary>
/// 蘑菇。依靠余屑与遮蔽生长，是蚯蚓、蜗牛和沙蚕的食物，也是死亡回收的主要起点。
/// 策划案规则：最低条件为余屑加遮蔽或湿润；雨天活力+2 且成熟后产出一份孢子；
/// 晴天活力-2，所在格没有余屑时额外再扣 1 点。
/// </summary>
[Serializable]
public class Mushroom : SporePlantsBase
{
    /// <summary>策划案：雨天活力+2。</summary>
    private const int GrowthPerRain = 2;

    /// <summary>策划案：晴天活力-2。</summary>
    private const int DecayPerSun = 2;

    /// <summary>策划案：没有余屑时加速衰退，在晴天基础上再扣 1 点。</summary>
    private const int StarveExtraDecay = 1;

    /// <summary>
    /// 策划案把统一衰减上限定为 2，但蘑菇的"无余屑时额外 -1"必须能被玩家观察到，
    /// 否则这条规则会被上限完全吃掉（原实现的 -3 总被截成 -2）。
    /// 蘑菇是死亡回收的主要起点，允许它放宽到 3 是最小改动且数值可控。
    /// </summary>
    protected override int MaxDecayPerNight
    {
        get { return DecayPerSun + StarveExtraDecay; }
    }

    /// <summary>活力达到该值后视为成熟，可以产出一份孢子。策划案未给出数值，暂定 5。</summary>
    private const int MatureVitality = 5;

    /// <summary>策划案：蘑菇死亡回收产出的余屑多于其他孢子植物。</summary>
    private const int RecycleDetritus = 3;

    /// <summary>策划案第八章：投放的小蘑菇是幼体，初始活力低于成熟的苔藓。</summary>
    protected override int StartingVitality
    {
        get { return 2; }
    }

    /// <summary>
    /// 策划案把蘑菇列为蜗牛、蚯蚓和蚂蚁的食物。
    /// 份量按活力给：幼体只能抵一口，成熟菌伞够两只动物分。
    /// 活力归零后进入枯萎，下一次结算即回收，因此不再作为食物。
    /// </summary>
    public override int FoodAmount
    {
        get
        {
            if (Vitality <= 0)
                return 0;
            return Vitality >= MatureVitality ? 2 : 1;
        }
    }

    /// <summary>菌伞是策划案明确列出的庇护物之一，成熟蘑菇可以为脚下的小动物遮掩。</summary>
    public override bool IsShelter
    {
        get { return Vitality >= 2; }
    }

    protected override E_PlantSpecies SpeciesValue
    {
        get { return E_PlantSpecies.Mushroom; }
    }

    /// <summary>蘑菇是独立的子实体，占主体位；它本身也可以被苔藓覆盖。</summary>
    public override E_OccupancySlot OccupancySlot
    {
        get { return E_OccupancySlot.Subject; }
    }

    public override string DisplayName
    {
        get { return "蘑菇"; }
    }

    public override string RulePrefix
    {
        get { return "SPORE.MUSHROOM"; }
    }

    protected override int RecycleAmount
    {
        get { return RecycleDetritus; }
    }

    protected override string MinimumRequirement
    {
        get { return "遮蔽或湿润；雨天还需要所在格有余屑"; }
    }

    /// <summary>
    /// 这里只判断位置条件，不把"有余屑"算进来。
    /// 因为策划案要求无余屑时反而衰减得更快，如果在这里挡掉，晴天规则就永远不会执行。
    /// 余屑条件放在雨天分支里判断。
    /// </summary>
    protected override bool MeetsMinimum(ICellEnv cell)
    {
        return cell.IsShaded || cell.IsWet;
    }

    protected override void BuildSpeciesPlan(PlantSettlePlan plan, int day, IWeatherQuery weather, ICellEnv cell)
    {
        if (!weather.Meets(E_PlantNeed.Moisture))
        {
            plan.VitalityDelta = -DecayPerSun;
            if (cell.Detritus > 0)
                return;

            plan.VitalityDelta -= StarveExtraDecay;
            plan.Log(RulePrefix + ".STARVE",
                DisplayName + " 所在格没有余屑，晴天额外衰减 " + StarveExtraDecay + " 点。");
            return;
        }

        // 策划案把"余屑"列为最低生长条件，雨天没有余屑就只衰减不增长。
        if (cell.Detritus <= 0)
        {
            plan.Log(RulePrefix + ".MIN",
                DisplayName + " 所在格没有余屑，雨天不增长。");
            return;
        }

        plan.VitalityDelta = GrowthPerRain;

        // 策划案：成熟后产生 1 份孢子。冷却期间不产出，产出后进入冷却。
        if (ProduceCooldown > 0 || Vitality < MatureVitality)
            return;

        plan.ProduceKind = E_ResourceKind.Spore;
        plan.ProduceAmount = 1;
        plan.ProduceCooldownDelta = 1;
        plan.Log(RulePrefix + ".SPORE", DisplayName + " 产出 1 份孢子，进入产出冷却。");
    }
}