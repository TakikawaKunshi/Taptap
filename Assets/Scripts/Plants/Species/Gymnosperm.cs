using System;
using UnityEngine;

/// <summary>
/// 裸子植物。策划案的候选实现是星壳针木：晴雨交替完成后长出硬壳球果，
/// 成熟后一次性掉落种子，蚂蚁可以搬运球果，麻雀可以取食种子，奶蛙会把树下当庇护。
/// </summary>
[Serializable]
public class Gymnosperm : SeedPlantsBase
{
    /// <summary>策划案：成长点达到 3 时进入成熟株。</summary>
    protected override int MatureGrowthPoints
    {
        get { return 3; }
    }

    protected override E_PlantSpecies SpeciesValue
    {
        get { return E_PlantSpecies.Gymnosperm; }
    }

    public override string DisplayName
    {
        get { return "星壳针木"; }
    }

    public override string RulePrefix
    {
        get { return "SEED.GYMNO"; }
    }

    /// <summary>球果是否已经长出。策划案：交替完成后长出硬壳球果。</summary>
    public bool HasCone { get; private set; }

    /// <summary>
    /// 策划案：长到成熟及以上就长出硬壳球果，球果可以交给蚂蚁搬运。
    /// 写在钩子而不是 ApplyPlan 里，是为了让蚯蚓催熟这条路径也能长出球果。
    /// </summary>
    protected override void OnStageAdvanced(PlantSettlePlan plan)
    {
        if (HasCone || Stage < E_PlantStage.Mature)
            return;

        HasCone = true;
        if (plan != null)
            plan.Log(RulePrefix + ".CONE", DisplayName + " 长出硬壳球果，蚂蚁可以搬运。");
    }
}