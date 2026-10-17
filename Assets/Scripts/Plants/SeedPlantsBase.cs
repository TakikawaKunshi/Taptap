using System;
using UnityEngine;

/// <summary>
/// 种子植物基类。策划案规定种子植物不按现实月级时间成长，而是采用"成长回合"抽象：
/// 雨天给水合标记，晴天给光合标记，两个标记配对后消耗并产生一个成长点。
/// 成长点累积到阈值时推进生长阶段，成熟后一次性产出种子。
/// </summary>
[Serializable]
public abstract class SeedPlantsBase : PlantsBase
{
    /// <summary>策划案：晴雨交替完成一轮成长时产生 1 个成长点。</summary>
    private const int GrowthPointsPerPair = 1;

    private bool hasHydration;
    private bool hasPhotosynthesis;
    private int growthPoints;
    private bool seedDroppedOnce;
    private bool seedOnGround;

    /// <summary>水合标记，雨天获得。单独一个雨天不完成生长。</summary>
    public bool HasHydration
    {
        get { return hasHydration; }
    }

    /// <summary>光合标记，晴天获得。</summary>
    public bool HasPhotosynthesis
    {
        get { return hasPhotosynthesis; }
    }

    /// <summary>成长点，由晴雨标记配对产生。</summary>
    public int GrowthPoints
    {
        get { return growthPoints; }
    }

    /// <summary>生长阶段的中文名，用于日志与占位可视化。</summary>
    public string StageName
    {
        get
        {
            switch (Stage)
            {
                case E_PlantStage.Seed: return "种子";
                case E_PlantStage.Sprout: return "幼芽";
                case E_PlantStage.Seedling: return "幼株";
                case E_PlantStage.Mature: return "成熟";
                case E_PlantStage.Giant: return "巨型";
                default: return Stage.ToString();
            }
        }
    }

    public override string DescribeShort()
    {
        string extra = PotionUsed ? " 已用药水" : string.Empty;
        string seed = HasSeedOnGround ? " 地上有种子" : string.Empty;
        return DisplayName + "（" + StageName + "）成长点" + growthPoints + extra + seed + StatusSuffix();
    }

    /// <summary>当前生长阶段，由成长点换算。</summary>
    public E_PlantStage Stage { get; private set; }

    /// <summary>种子是否已经掉落过。策划案要求成熟后一次性掉落，不重复产出。</summary>
    public bool HasDroppedSeed
    {
        get { return seedDroppedOnce; }
    }

    /// <summary>掉落的种子是否还留在原地可被搬运；被取走后为 false。</summary>
    public bool HasSeedOnGround
    {
        get { return seedOnGround; }
    }

    /// <summary>尚未萌芽的种子本体，或成熟株掉落的种子，可被搬运。</summary>
    public bool HasTransportableSeed => !IsDead && (Stage == E_PlantStage.Seed || seedOnGround);

    /// <summary>实际取走一份种子；搬走种子本体时释放原位置，不产生死亡余屑。</summary>
    public bool TryTakeSeed()
    {
        if (!HasTransportableSeed)
            return false;
        if (Stage == E_PlantStage.Seed)
        {
            MarkDead();
            CurrentCell?.Release(this);
        }
        OnSeedTaken();
        return true;
    }

    /// <summary>是否已经使用过疯长药水。策划案要求不能对同一株重复使用。</summary>
    public bool PotionUsed { get; private set; }

    /// <summary>本物种成熟所需的成长点数量。</summary>
    protected abstract int MatureGrowthPoints { get; }

    /// <summary>成熟种子植物占主体格，可以为相邻格提供遮蔽。</summary>
    public override E_OccupancySlot OccupancySlot
    {
        get { return E_OccupancySlot.Subject; }
    }

    /// <summary>本物种作为庇护所的条件；首版只有幼株及以上才算庇护。</summary>
    public override bool IsShelter
    {
        get { return Stage >= E_PlantStage.Seedling; }
    }

    /// <summary>幼芽与幼株可以被动物取食；返回可被吃掉的份量。</summary>
    public override int FoodAmount
    {
        get { return Stage == E_PlantStage.Seed || Stage == E_PlantStage.Sprout ? 1 : 0; }
    }

    protected override PlantSettlePlan BuildPlan(int day, IWeatherQuery weather, ICellEnv cell)
    {
        var plan = new PlantSettlePlan();

        if (Stage == E_PlantStage.Giant)
        {
            plan.Log(RulePrefix + ".GIANT", DisplayName + " 已是巨型株，不再变化。");
            return plan;
        }

        // 纯读：只把"本晚要拿到什么标记"写进提案，绝不碰 hasHydration / hasPhotosynthesis，
        // 否则做 preview 之类的只读调用会污染植物状态。
        if (weather.Meets(E_PlantNeed.Moisture))
            CollectHydration(plan);
        else if (weather.Meets(E_PlantNeed.Light))
            CollectPhotosynthesis(plan);

        PairMark(plan);
        ApplyStageChange(plan);
        return plan;
    }

    protected override void ApplyPlan(PlantSettlePlan plan, ICellEnv cell)
    {
        if (plan.GainHydration)
            hasHydration = true;
        if (plan.GainPhotosynthesis)
            hasPhotosynthesis = true;

        // 只在配对成功时消耗两个标记；单独的雨天或晴天必须保留标记，
        // 否则"晴雨交替才成长"会退化成每晚都成长。
        if (plan.GrowthPointsDelta > 0)
        {
            hasHydration = false;
            hasPhotosynthesis = false;
        }

        growthPoints = Mathf.Max(0, growthPoints + plan.GrowthPointsDelta);

        if (plan.StageChanged && plan.NewStage.HasValue)
            Stage = plan.NewStage.Value;

        // 策划案：成熟后一次性掉落种子。
        // 这里用只增不减的 latch，而不是可被 OnSeedTaken 复位的标记，
        // 否则蚂蚁每搬走一颗，第二天就又结一颗，种子会凭空无限产出。
        DropSeedOnce(plan);
        if (Stage >= E_PlantStage.Mature)
            OnStageAdvanced(plan);
    }

    /// <summary>雨天记录水合标记。已持有标记时不再重复获得。</summary>
    private void CollectHydration(PlantSettlePlan plan)
    {
        if (hasHydration)
        {
            plan.Log(RulePrefix + ".HYDRATE_KEEP", DisplayName + " 已有水合标记，本晚不重复获得。");
            return;
        }

        plan.GainHydration = true;
        plan.Log(RulePrefix + ".HYDRATE", DisplayName + " 获得水合标记。");
    }

    /// <summary>晴天记录光合标记。已持有标记时不再重复获得。</summary>
    private void CollectPhotosynthesis(PlantSettlePlan plan)
    {
        if (hasPhotosynthesis)
        {
            plan.Log(RulePrefix + ".PHOTO_KEEP", DisplayName + " 已有光合标记，本晚不重复获得。");
            return;
        }

        plan.GainPhotosynthesis = true;
        plan.Log(RulePrefix + ".PHOTO", DisplayName + " 获得光合标记。");
    }

    /// <summary>
    /// 两个标记同时存在时配对，消耗两个标记并产生一个成长点。
    /// 这是"晴与雨交替时才能成长"这条规则的唯一实现点。
    /// </summary>
    private void PairMark(PlantSettlePlan plan)
    {
        bool hydration = hasHydration || plan.GainHydration;
        bool photosynthesis = hasPhotosynthesis || plan.GainPhotosynthesis;
        if (!hydration || !photosynthesis)
            return;

        plan.GrowthPointsDelta = GrowthPointsPerPair;
        plan.Log(RulePrefix + ".PAIR",
            DisplayName + " 水合与光合标记配对，成长点+" + GrowthPointsPerPair + "。");
    }

    /// <summary>按成长点换算阶段。</summary>
    private void ApplyStageChange(PlantSettlePlan plan)
    {
        int total = growthPoints + plan.GrowthPointsDelta;
        E_PlantStage next = StageFor(total, plan.GrowthPointsDelta > 0);
        if (next == Stage)
            return;

        plan.StageChanged = true;
        plan.NewStage = next;
        plan.Log(RulePrefix + ".STAGE",
            DisplayName + " 进入 " + next + " 阶段。");
    }

    /// <summary>
    /// 成长点换算成阶段。策划案：0 种子、1 幼芽、2 幼株、3 成熟株、巨型株由药水触发。
    /// 巨型株要求"下一次有效交替之后才发生"，所以只有本晚真的完成了一次配对才能跳级；
    /// 连续同样的天气不会因为喝了药水就提前巨大化。
    /// </summary>
    protected virtual E_PlantStage StageFor(int total, bool advancedTonight)
    {
        if (advancedTonight && PotionUsed && total >= MatureGrowthPoints)
            return E_PlantStage.Giant;
        if (total >= MatureGrowthPoints)
            return E_PlantStage.Mature;
        if (total >= 2)
            return E_PlantStage.Seedling;
        if (total >= 1)
            return E_PlantStage.Sprout;
        return E_PlantStage.Seed;
    }

    /// <summary>
    /// 成熟（或更高阶段）达成时的一次性变身，例如裸子植物长球果、被子植物开花。
    /// 单独抽出来是因为成熟不一定由过夜结算触发：
    /// 蚯蚓松土的 <see cref="GrantGrowth"/> 也会把植株催熟，那时同样要开花结果。
    /// </summary>
    protected virtual void OnStageAdvanced(PlantSettlePlan plan)
    {
    }

    /// <summary>成熟后结一次种子；重复调用不会产出第二份。</summary>
    private void DropSeedOnce(PlantSettlePlan plan)
    {
        if (Stage < E_PlantStage.Mature || seedDroppedOnce)
            return;

        seedDroppedOnce = true;
        seedOnGround = true;
        if (plan == null)
            return;

        plan.ProduceKind = E_ResourceKind.Seed;
        plan.ProduceAmount = 1;
        plan.Log(RulePrefix + ".SEED", DisplayName + " 成熟，掉落 1 份种子。");
    }

    /// <summary>种子植物不吃东西，被取食时返回 false。</summary>
    public override bool TakeBite(int amount)
    {
        return false;
    }

    /// <summary>蚯蚓松土或授粉带来的额外成长点，会立刻推进阶段。</summary>
    public override void GrantGrowth(int points)
    {
        if (points <= 0 || Stage == E_PlantStage.Giant)
            return;

        growthPoints += points;
        E_PlantStage next = StageFor(growthPoints, true);
        if (next != Stage)
            Stage = next;

        // GrantGrowth 不走 Proposal/Commit，成熟后的一次性掉落与开花结果要在这里补上，
        // 否则被蚯蚓催熟的植株永远不会结种子、不开花、不长球果。
        DropSeedOnce(null);
        if (Stage >= E_PlantStage.Mature)
            OnStageAdvanced(null);

        NotifyChanged();
    }

    /// <summary>
    /// 种子被蚂蚁搬走或被动物吃掉。只表示"地上那颗没了"，
    /// 不解锁下一次掉落：策划案的成熟株只结一次种子。
    /// </summary>
    public override void OnSeedTaken()
    {
        seedOnGround = false;
        NotifyChanged();
    }

    /// <summary>
    /// 使用疯长药水。策划案：目标成长点+2，下次有效晴雨交替后进入巨型株；
    /// 不能对成熟株重复使用，药水不能用于孢子植物。
    /// </summary>
    public bool ApplyGrowthPotion()
    {
        if (PotionUsed || Stage >= E_PlantStage.Mature)
            return false;

        PotionUsed = true;
        growthPoints += 2;
        NotifyChanged();
        return true;
    }
}
