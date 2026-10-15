using System;
using UnityEngine;

/// <summary>
/// 被子植物。策划案的候选实现是向日铃花：晴雨交替完成后开花，
/// 成熟后吸引昆虫并产生可传播的种子，蜘蛛会在花旁结网，蚂蚁取蜜，蜗牛取食幼芽。
/// </summary>
[Serializable]
public class Angiosperm : SeedPlantsBase
{
    /// <summary>策划案：成长点达到 3 时进入成熟株。</summary>
    protected override int MatureGrowthPoints
    {
        get { return 3; }
    }

    /// <summary>策划案：成熟后每两个雨天产生 1 份可传播的种子。</summary>
    private const int RainNightsPerSeed = 2;

    private int rainNightsSinceSeed;

    /// <summary>是否已经开花。策划案：蜘蛛在花旁结网，开花是蜘蛛结网的位置条件。</summary>
    public bool IsFlowering { get; private set; }

    protected override E_PlantSpecies SpeciesValue
    {
        get { return E_PlantSpecies.Angiosperm; }
    }

    public override string DisplayName
    {
        get { return "向日铃花"; }
    }

    public override string RulePrefix
    {
        get { return "SEED.ANGIO"; }
    }

    protected override PlantSettlePlan BuildPlan(int day, IWeatherQuery weather, ICellEnv cell)
    {
        PlantSettlePlan plan = base.BuildPlan(day, weather, cell);

        // 策划案：成熟后每两个雨天产生一份可传播的种子；未成熟时不累计雨天。
        // 只读阶段不修改 rainNightsSinceSeed，改为把"要不要计数／要不要清零"写进提案，
        // 这样重复调用 Propose 做预览不会把雨夜数偷偷加上去。
        if (Stage < E_PlantStage.Mature || !weather.Meets(E_PlantNeed.Moisture))
            return plan;

        plan.CountRainNight = true;
        if (rainNightsSinceSeed + 1 < RainNightsPerSeed)
            return plan;

        plan.CountRainNight = false;
        plan.ResetRainNightCounter = true;
        plan.ProduceKind = E_ResourceKind.Seed;
        plan.ProduceAmount = 1;
        plan.Log(RulePrefix + ".SEED",
            DisplayName + " 连续 " + RainNightsPerSeed + " 个雨天，产生 1 份可传播的种子。");
        return plan;
    }

    protected override void ApplyPlan(PlantSettlePlan plan, ICellEnv cell)
    {
        if (plan.CountRainNight)
            rainNightsSinceSeed++;
        if (plan.ResetRainNightCounter)
            rainNightsSinceSeed = 0;

        base.ApplyPlan(plan, cell);
    }

    /// <summary>
    /// 策划案：开花是蜘蛛结网和蚂蚁取蜜的条件，因此只要成熟就开花。
    /// 写在钩子而不是 ApplyPlan 里，是为了让蚯蚓催熟这条路径也能开花。
    /// </summary>
    protected override void OnStageAdvanced(PlantSettlePlan plan)
    {
        if (IsFlowering || Stage < E_PlantStage.Mature)
            return;

        IsFlowering = true;
        if (plan != null)
            plan.Log(RulePrefix + ".FLOWER",
                DisplayName + " 开花，吸引昆虫，蜘蛛可以在旁边结网。");
    }

    /// <summary>距离下一次产生种子还差几个雨天；供界面显示"再等一个雨天"。</summary>
    public int RainNightsUntilNextSeed
    {
        get { return Stage < E_PlantStage.Mature ? -1 : RainNightsPerSeed - rainNightsSinceSeed; }
    }
}