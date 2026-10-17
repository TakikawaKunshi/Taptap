using System;
using UnityEngine;

/// <summary>
/// 孢子植物基类。策划案规定孢子植物不使用根、茎、叶数据，只保留
/// 活力值、扩张值、附着地块、产出冷却四个字段。
/// 雨天增长、晴天衰减；默认枯萎后下一晚回收，蘑菇可覆写为活力归零立即回收。
/// 每晚的增长与衰减都套用策划案给出的上限，避免一天满屏爆发或全部清空。
/// </summary>
[Serializable]
public abstract class SporePlantsBase : PlantsBase
{
    /// <summary>策划案上限：孢子植物每晚最多增长 2 点。</summary>
    public const int MaxGrowthPerNight = 2;

    /// <summary>策划案给孢子植物的统一默认衰减上限。</summary>
    public const int DefaultMaxDecayPerNight = 2;

    private Vector2Int attachedCell;
    private int vitality;
    private int expansion;
    private int produceCooldown;

    /// <summary>
    /// 在基类构造函数里调用虚成员通常不被推荐；这里刻意如此，
    /// 因为 <see cref="StartingVitality"/> 的派生实现只返回常量，
    /// 不依赖任何尚未初始化的派生字段。好处是物种只需覆写一个属性，
    /// 就能表达策划案初始配置里的"成熟苔藓""幼体蘑菇"这类差异。
    /// </summary>
    protected SporePlantsBase()
    {
        vitality = StartingVitality;
    }

    /// <summary>孢子植物没有成长点，阶段直接由活力表达。</summary>
    public override string DescribeShort()
    {
        return DisplayName + " 活力" + vitality + StatusSuffix();
    }

    /// <summary>活力值。雨天增长、晴天衰减，归零后进入枯萎。</summary>
    public int Vitality
    {
        get { return vitality; }
    }

    /// <summary>扩张值。雨天满足最低条件时申请一次扩张，由结算方裁决目标。</summary>
    public int Expansion
    {
        get { return expansion; }
    }

    /// <summary>产出冷却。冷却期间不再产出，遇到雨天时减一。</summary>
    public int ProduceCooldown
    {
        get { return produceCooldown; }
    }

    /// <summary>附着的格子。策划案把它和所在位置分开列出，方便后续出现跨格附着的物种。</summary>
    public Vector2Int AttachedCell
    {
        get { return attachedCell; }
    }

    /// <summary>孢子植物默认作为地表覆盖，可与主体植物共存；蘑菇等例外自行覆写。</summary>
    public override E_OccupancySlot OccupancySlot
    {
        get { return E_OccupancySlot.Cover; }
    }

    /// <summary>枯萎回收时产出的余屑份数。</summary>
    protected abstract int RecycleAmount { get; }

    protected virtual bool RecycleImmediatelyWhenDepleted => false;

    /// <summary>
    /// 投放时的初始活力。策划案第八章要求雨眠苔投放即为"成熟"、小蘑菇为"幼体"，
    /// 所以这个值必须由物种覆写，不能写死成一个数。
    /// </summary>
    protected virtual int StartingVitality
    {
        get { return 3; }
    }

    /// <summary>
    /// 本物种每晚允许的最大衰减量。策划案建议统一上限 2；
    /// 但蘑菇"无余屑时加速衰退"必须让玩家看得见，因此允许物种放宽自己的上限。
    /// </summary>
    protected virtual int MaxDecayPerNight
    {
        get { return DefaultMaxDecayPerNight; }
    }

    /// <summary>最低生长条件的中文描述，只用于日志说明。</summary>
    protected abstract string MinimumRequirement { get; }

    public override void PlaceAt(Vector2Int position)
    {
        base.PlaceAt(position);
        attachedCell = position;
    }

    protected sealed override PlantSettlePlan BuildPlan(int day, IWeatherQuery weather, ICellEnv cell)
    {
        // 判定枯萎与回收以写回之后的活力为准，因此先看上一晚写回的结果。
        // 活力在结算开始前就是 0，说明它已经被标记过枯萎或已被取食清空。
        if (IsWithering || vitality == 0)
        {
            var recycled = new PlantSettlePlan();
            recycled.RecycleToDetritus = RecycleAmount;
            recycled.Log(RulePrefix + ".RECYCLE",
                DisplayName + " 枯萎回收，所在格产出 " + RecycleAmount + " 份余屑。");
            return recycled;
        }

        var plan = new PlantSettlePlan();

        // 最低生长条件只约束"能不能长"，不约束"会不会枯"。
        // 策划案要求晴天让土地变干、孢子植物衰减，因此晴天一律执行衰减，
        // 即使本格已经不满足生长条件；否则植物会被冻结在原地，既不长也不死。
        if (weather.Meets(E_PlantNeed.Moisture) && !MeetsMinimum(cell))
        {
            plan.Log(RulePrefix + ".MIN",
                DisplayName + " 不满足最低生长条件（" + MinimumRequirement + "），雨天不增长。");
            return plan;
        }

        TickCooldown(plan, weather);
        BuildSpeciesPlan(plan, day, weather, cell);
        ApplyGrowthCap(plan, weather);
        LogGrowth(plan, weather);
        return plan;
    }

    protected override void ApplyPlan(PlantSettlePlan plan, ICellEnv cell)
    {
        if (plan.RecycleToDetritus > 0)
        {
            cell.Detritus += plan.RecycleToDetritus;
            MarkDead();
            return;
        }

        vitality = Mathf.Max(0, vitality + plan.VitalityDelta);
        produceCooldown = Mathf.Max(0, produceCooldown + plan.ProduceCooldownDelta);

        // 只有被裁决批准的扩张才累计扩张值；被拒绝的申请不算扩张。
        if (plan.ExpansionGranted)
            expansion++;

        if (plan.DetritusDelta != 0)
            cell.Detritus = Mathf.Max(0, cell.Detritus + plan.DetritusDelta);

        // 活力归零不在只读阶段判断：只有写回之后才知道本晚的结果。
        if (vitality == 0 && !IsWithering)
        {
            if (TryRecycleDepleted())
            {
                plan.RecycleToDetritus = RecycleAmount;
                plan.Log(RulePrefix + ".RECYCLE",
                    DisplayName + " 活力归零，立即回收为 " + RecycleAmount + " 份余屑。");
            }
            else
            {
                MarkWithering();
                plan.Log(RulePrefix + ".WITHER",
                    DisplayName + " 活力归零，进入枯萎，下一晚回收成余屑。");
            }
        }
    }

    private bool TryRecycleDepleted()
    {
        if (!RecycleImmediatelyWhenDepleted || vitality > 0 || IsDead || CurrentCell == null)
            return false;

        CurrentCell.Detritus += RecycleAmount;
        MarkDead();
        CurrentCell.Release(this);
        return true;
    }

    /// <summary>各物种自己的数值规则，只有满足最低条件时才会被调用。</summary>
    protected abstract void BuildSpeciesPlan(PlantSettlePlan plan, int day, IWeatherQuery weather, ICellEnv cell);

    /// <summary>是否满足策划案给出的最低生长条件。</summary>
    protected abstract bool MeetsMinimum(ICellEnv cell);

    /// <summary>
    /// 被取食。每份扣除一点活力，不会降到 0 以下；活力已经归零或份数非正时返回 false。
    /// 取食不属于过夜结算，因此不会推进结算天数，只影响活力并立即通知状态变化。
    /// </summary>
    public override bool TakeBite(int amount)
    {
        if (amount <= 0 || vitality <= 0 || IsDead)
            return false;

        vitality = Mathf.Max(0, vitality - amount);
        if (vitality == 0 && !IsWithering && !TryRecycleDepleted())
            MarkWithering();
        NotifyChanged();
        return true;
    }

    /// <summary>套用策划案的每晚上限：雨天封顶增长，晴天封底衰减。</summary>
    private void ApplyGrowthCap(PlantSettlePlan plan, IWeatherQuery weather)
    {
        if (weather.Meets(E_PlantNeed.Moisture))
            plan.VitalityDelta = Mathf.Min(plan.VitalityDelta, MaxGrowthPerNight);
        else if (weather.Meets(E_PlantNeed.Heat))
            plan.VitalityDelta = Mathf.Max(plan.VitalityDelta, -MaxDecayPerNight);
    }

    /// <summary>
    /// 为本晚的活力变化记一条规则日志。
    /// 规则编号统一为 SPORE.XXX.MOIST（雨天增长）或 SPORE.XXX.HEAT（晴天衰减），
    /// 便于晨报复盘时统计"有多少株因雨增长、因晴衰减"。
    /// </summary>
    private void LogGrowth(PlantSettlePlan plan, IWeatherQuery weather)
    {
        if (plan.VitalityDelta > 0)
            plan.Log(RulePrefix + ".MOIST", DisplayName + " 活力+" + plan.VitalityDelta + "。");
        else if (plan.VitalityDelta < 0)
            plan.Log(RulePrefix + ".HEAT", DisplayName + " 活力" + plan.VitalityDelta + "。");
    }

    /// <summary>产出冷却只在雨天结束时减一，晴天保持不变。</summary>
    private void TickCooldown(PlantSettlePlan plan, IWeatherQuery weather)
    {
        if (produceCooldown <= 0 || !weather.Meets(E_PlantNeed.Moisture))
            return;

        plan.ProduceCooldownDelta = -1;
        plan.Log(RulePrefix + ".COOLDOWN", DisplayName + " 的产出冷却结束，恢复产出。");
    }
}
