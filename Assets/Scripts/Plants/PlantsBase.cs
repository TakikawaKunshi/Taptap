using System;
using UnityEngine;

/// <summary>
/// 植物基类。策划案把植物分成孢子植物与种子植物两类，两类共用同一套约定：
/// 记录所在格子、同一天只结算一次、枯萎后下一晚回收成余屑、状态全部更新后再发事件。
/// 沿用 WeatherBase 的写法：历史值只读，写入走幂等方法，状态更新完触发一次事件。
/// 本类是纯数据，不挂载在任何场景物体上，由格子持有并驱动。
/// </summary>
[Serializable]
public abstract class PlantsBase : IPlantInteraction
{
    private Vector2Int coordinates;
    private int lastSettledDay;
    private bool placed;

    public E_PlantSpecies Species
    {
        get { return SpeciesValue; }
    }

    /// <summary>本物种的枚举值；由各物种自行覆写，避免出现与实际类型不一致的默认值。</summary>
    protected abstract E_PlantSpecies SpeciesValue { get; }

    /// <summary>地图坐标系中的整数位置，坐标以中心格为原点。</summary>
    public Vector2Int Coordinates => coordinates;

    /// <summary>最近一次完成结算的天数；同一天重复结算会被拦截。</summary>
    public int LastSettledDay => lastSettledDay;

    public bool IsPlaced => placed;

    /// <summary>活力归零后为真。枯萎的植物本晚不再生长，下一晚回收成余屑并移出地图。</summary>
    public bool IsWithering { get; private set; }

    /// <summary>已经回收成余屑并从地图移除。</summary>
    public bool IsDead { get; private set; }

    /// <summary>用于日志的中文名。</summary>
    public abstract string DisplayName { get; }

    /// <summary>
    /// 本物种占用的位置。由各物种自行声明，结算方据此决定放进主体位还是地表覆盖位。
    /// 策划案允许苔藓与藻类和成熟植物共存，因此不能按"孢子／种子"的基类二分。
    /// </summary>
    public abstract E_OccupancySlot OccupancySlot { get; }

    /// <summary>本物种的规则编号前缀，例如 SPORE.MOSS。</summary>
    public abstract string RulePrefix { get; }

    /// <summary>
    /// 给界面与调试用的一行状态描述，例如"雨眠苔 活力5"。
    /// 派生类覆写后补上自己的进度数值；基类的用途是让占位可视化能直接把状态写进物体名。
    /// </summary>
    public virtual string DescribeShort()
    {
        return DisplayName + StatusSuffix();
    }

    /// <summary>派生类拼 DescribeShort 时共用的状态后缀。</summary>
    protected string StatusSuffix()
    {
        if (IsDead)
            return "（已回收）";
        return IsWithering ? "（枯萎）" : string.Empty;
    }

    /// <summary>状态全部更新完成后触发。结算方与界面都靠它刷新，不在结算中轮询。</summary>
    public event Action Changed;

    /// <summary>派生类内部用于触发 Changed；外部只能使用 += 与 -= 订阅。</summary>
    protected void NotifyChanged()
    {
        if (Changed != null)
            Changed();
    }

    /// <summary>
    /// 过夜结算入口。
    /// 同一天重复结算、已经回收的植物都返回 false，且不改变任何数据；
    /// 天数非法或没有放到格子上时抛出异常，避免把配置错误当成正常结算。
    /// </summary>
    public bool PlantSettle(int day, IWeatherQuery weather, ICellEnv cell)
    {
        if (day < 1)
            throw new ArgumentOutOfRangeException(nameof(day), "天数必须从 1 开始。");
        if (weather == null)
            throw new ArgumentNullException(nameof(weather));
        if (cell == null)
            throw new ArgumentNullException(nameof(cell));
        if (!placed)
            throw new InvalidOperationException(DisplayName + " 还没有被放到任何格子上，不能结算。");
        if (IsDead || day <= lastSettledDay)
            return false;

        PlantSettlePlan plan = BuildPlan(day, weather, cell);
        ApplyPlan(plan, cell);
        lastSettledDay = day;
        NotifyChanged();
        return true;
    }

    /// 纯读：算出本晚想做什么，不修改任何状态，也不写格子。
    /// </summary>
    public PlantSettlePlan Propose(int day, IWeatherQuery weather, ICellEnv cell)
    {
        if (day < 1)
            throw new ArgumentOutOfRangeException(nameof(day), "天数必须从 1 开始。");
        if (weather == null)
            throw new ArgumentNullException(nameof(weather));
        if (cell == null)
            throw new ArgumentNullException(nameof(cell));
        if (!placed)
            throw new InvalidOperationException(DisplayName + " 还没有被放到任何格子上，不能结算。");
        if (IsDead || day <= lastSettledDay)
            return null;

        return BuildPlan(day, weather, cell);
    }

    /// <summary>
    /// 把已经裁决过的提案写回，并把结算天数推进到当天。
    /// 同一天重复提交会被拒绝而不是覆盖结果，因此编排器重试也不会重复发放。
    /// </summary>
    public bool Commit(int day, ICellEnv cell, PlantSettlePlan plan)
    {
        if (day < 1)
            throw new ArgumentOutOfRangeException(nameof(day), "天数必须从 1 开始。");
        if (cell == null)
            throw new ArgumentNullException(nameof(cell));
        if (plan == null)
            throw new ArgumentNullException(nameof(plan));
        if (IsDead || day <= lastSettledDay)
            return false;

        ApplyPlan(plan, cell);
        lastSettledDay = day;
        NotifyChanged();
        return true;
    }

    /// <summary>
    /// 把该植物放到指定格子，只能调用一次。
    /// 重复放置说明格子层或结算方的占位逻辑有问题，直接抛出而不是静默覆盖。
    /// </summary>
    public virtual void PlaceAt(Vector2Int position)
    {
        if (placed)
            throw new InvalidOperationException(
                DisplayName + " 已经被放到 " + coordinates + "，不能再次放到 " + position + "。");
        coordinates = position;
        placed = true;
    }

    /// <summary>纯读：算出本晚想做什么，不修改任何状态，也不写格子。</summary>
    protected abstract PlantSettlePlan BuildPlan(int day, IWeatherQuery weather, ICellEnv cell);

    /// <summary>统一写回提案。此处允许追加日志，例如活力归零这类在写回时才确定的结果。</summary>
    protected abstract void ApplyPlan(PlantSettlePlan plan, ICellEnv cell);

    protected void MarkWithering()
    {
        IsWithering = true;
    }

    protected void MarkDead()
    {
        IsWithering = false;
        IsDead = true;
    }

    // ---- 以下是动物可以调用到植物的入口，首版默认都不产生效果 ----

    /// <summary>孢子植物默认不可食；蘑菇与苔藓各自覆写。</summary>
    public virtual bool TakeBite(int amount)
    {
        return false;
    }

    /// <summary>额外成长点。孢子植物不使用成长点，默认忽略。</summary>
    public virtual void GrantGrowth(int points)
    {
    }

    /// <summary>种子被搬走。孢子植物没有种子，默认忽略。</summary>
    public virtual void OnSeedTaken()
    {
    }

    /// <summary>首版没有庇护判定，种子植物接入成熟阶段后再覆写。</summary>
    public virtual bool IsShelter
    {
        get { return false; }
    }

    /// <summary>首版没有食物份量，动物系统接入后再覆写。</summary>
    public virtual int FoodAmount
    {
        get { return 0; }
    }
}