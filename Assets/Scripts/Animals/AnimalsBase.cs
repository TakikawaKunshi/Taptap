using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 动物基类。与 <see cref="PlantsBase"/> 同一套约定：
/// 自身是纯数据，不挂载在任何场景物体上；每晚分"只读提案"和"统一写回"两步；
/// 同一天重复结算会被拦截；状态更新完之后触发一次事件。
///
/// 策划案第五章的通用状态在这里落地：饱腹 0-3、饥饿计数 0-3、当前目标、活动标签和记忆。
/// 第四章结束后级联到第六章的饥饿闭环：饥饿计数达到 3 的动物死亡并转为余屑。
///
/// 决策优先级由本类的 <see cref="BuildPlan"/> 固定下来——先危险、再携带物、再饥饿、最后常规行为。
/// 子类通过覆写四个钩子来提供物种差异，因此不会漏掉某一层优先级。
/// </summary>
[Serializable]
public abstract class AnimalsBase
{
    /// <summary>饱腹上限；策划案取值建议 0 至 3。</summary>
    public const int MaxSatiety = 3;

    /// <summary>成功进食一次恢复的饱腹。</summary>
    public const int SatietyPerMeal = 2;

    /// <summary>每晚自然下降的饱腹。</summary>
    public const int SatietyDecayPerNight = 1;

    /// <summary>饥饿计数达到此值时死亡回收。</summary>
    public const int MaxStarvation = 3;

    /// <summary>策划案：每只动物每晚最多移动 3 格。</summary>
    public const int BaseMoveSteps = 3;

    /// <summary>
    /// 感知范围相对当晚步数放宽的格数。
    /// 策划案没有给出具体数值，但要求"路径不可达时保留目标、连续两晚无法推进才换目标"，
    /// 也就是说动物要能发现走不到的远处资源并连续靠近，而不是只有三格内的东西才存在。
    /// </summary>
    public const int ExtraPerception = 2;

    private ICellEnv currentCell;
    private int satiety;
    private int starvation;
    private int failedNights;
    private int lastSettledDay;
    private bool placed;
    private bool dead;

    protected AnimalsBase(int startingSatiety)
    {
        satiety = Mathf.Clamp(startingSatiety, 0, MaxSatiety);
    }

    // ---- 只读状态 ----

    public E_AnimalSpecies Species => SpeciesValue;

    /// <summary>本物种的枚举值；由各物种自行覆写。</summary>
    protected abstract E_AnimalSpecies SpeciesValue { get; }

    /// <summary>用于日志的中文名。</summary>
    public abstract string DisplayName { get; }

    /// <summary>本物种的规则编号前缀，例如 PREY.FROG。</summary>
    public abstract string RulePrefix { get; }

    /// <summary>活动标签，决定晴雨与地形下的评分和通行能力。</summary>
    public abstract E_ActivityTag Tags { get; }

    /// <summary>是否被捕食者追捕时的小型/大型判定，用于衰弱时限制捕食对象。</summary>
    public abstract E_BodySize BodySize { get; }

    /// <summary>死亡后回收的余屑数量。</summary>
    protected abstract int RecycleAmount { get; }

    /// <summary>对外暴露死亡后的回收量，供编排器在动物死亡时把余屑落到格子上。</summary>
    public int RecycleYield => RecycleAmount;

    /// <summary>当前所在格；没有落到地图时为 null。</summary>
    public ICellEnv CurrentCell => currentCell;

    public Vector2Int Coordinates => currentCell != null ? currentCell.Coordinates : Vector2Int.zero;

    /// <summary>饱腹 0 至 3。</summary>
    public int Satiety => satiety;

    /// <summary>饥饿计数 0 至 3；达到 3 时死亡。</summary>
    public int Starvation => starvation;

    public bool IsPlaced => placed;

    public bool IsDead => dead;

    /// <summary>连续多少晚无法朝目标推进；连续两晚才允许换目标。</summary>
    public int FailedNights => failedNights;

    public int LastSettledDay => lastSettledDay;

    /// <summary>当前携带的资源，未携带时为 None。</summary>
    public E_ResourceKind Carried { get; private set; } = E_ResourceKind.None;

    /// <summary>
    /// 携带的种子属于哪个植物物种；为 null 表示手上没有种子。
    /// 播种发生在"送达"的那一晚，而提案是每晚新建的，
    /// 因此种子来源必须记在动物身上，否则跨晚就丢失了。
    /// </summary>
    public E_PlantSpecies? CarriedSeedSpecies { get; private set; }

    /// <summary>是否正处在有效庇护中；在庇护里的动物不能被捕食。</summary>
    public bool IsSheltered { get; private set; }

    /// <summary>本晚的主要食物目标：植物、余屑或猎物。供 UI 显示目标提示。</summary>
    public E_AnimalGoal Goal { get; private set; } = E_AnimalGoal.None;

    /// <summary>记忆里最近一次找到食物的位置。</summary>
    public ICellEnv RememberedFood { get; private set; }

    /// <summary>记忆里最近一次的安全点。</summary>
    public ICellEnv RememberedSafety { get; private set; }

    public E_HungerState HungerState
    {
        get
        {
            if (starvation >= MaxStarvation || dead)
                return E_HungerState.Dead;
            if (satiety > 0)
                return E_HungerState.Normal;
            return starvation >= 2 ? E_HungerState.Weak : E_HungerState.Hungry;
        }
    }

    /// <summary>今晚能走多少格；饥饿找食物更远，衰弱则缩短。</summary>
    public int MaxStepsForTonight
    {
        get
        {
            if (HungerState == E_HungerState.Hungry)
                return BaseMoveSteps + 1;
            return HungerState == E_HungerState.Weak ? BaseMoveSteps - 1 : BaseMoveSteps;
        }
    }

    /// <summary>
    /// 感知范围：能看到的资源比当晚能走到的更远。
    /// 觅食与选址都用它来搜索，真正走过去时再受 <see cref="MaxStepsForTonight"/> 限制，
    /// 于是动物会朝着远处目标连续几晚靠近，而不是因为一次走不到就当它不存在。
    /// </summary>
    public int PerceptionForTonight => MaxStepsForTonight + ExtraPerception;

    /// <summary>状态全部更新完成后触发。结算方与界面都靠它刷新，不在结算中轮询。</summary>
    public event Action Changed;

    protected void NotifyChanged()
    {
        if (Changed != null)
            Changed();
    }

    // ---- 结算入口 ----

    /// <summary>
    /// 纯读：算出今晚想去哪、想做什么。
    /// 编排器拿到提案后才统一裁决位移和资源竞争，因此这里不能修改动物以外的任何状态。
    /// </summary>
    public AnimalSettlePlan Propose(int day, IAnimalWorld world, ICellEnv cell)
    {
        if (day < 1)
            throw new ArgumentOutOfRangeException(nameof(day), "天数必须从 1 开始。");
        if (world == null)
            throw new ArgumentNullException(nameof(world));
        if (cell == null)
            throw new ArgumentNullException(nameof(cell));
        if (!placed)
            throw new InvalidOperationException(DisplayName + " 还没有落到地图格子上，不能结算。");
        if (dead || day <= lastSettledDay)
            return null;

        return BuildPlan(day, world, cell);
    }

    /// <summary>
    /// 把已经裁决过的提案写回到自身，并推进结算天数。
    /// 目标格子由编排器在确定是否走得通之后传入，位移本身不在这一步发生。
    /// </summary>
    public bool Commit(int day, IAnimalWorld world, ICellEnv cell, AnimalSettlePlan plan)
    {
        if (day < 1)
            throw new ArgumentOutOfRangeException(nameof(day), "天数必须从 1 开始。");
        if (cell == null)
            throw new ArgumentNullException(nameof(cell));
        if (plan == null)
            throw new ArgumentNullException(nameof(plan));
        if (dead || day <= lastSettledDay)
            return false;

        Goal = plan.Goal;
        ApplyPlan(plan, world, cell);
        lastSettledDay = day;
        NotifyChanged();
        return true;
    }

    /// <summary>落到指定格子，只能调用一次。</summary>
    public void PlaceAt(ICellEnv cell)
    {
        if (cell == null)
            throw new ArgumentNullException(nameof(cell));
        if (placed)
            throw new InvalidOperationException(DisplayName + " 已经在地图上了，不能再次放置。");
        currentCell = cell;
        placed = true;
    }

    /// <summary>被编排器搬到新格子。超过每晚移动上限的判断由编排器负责。</summary>
    public void MoveTo(ICellEnv cell)
    {
        if (cell == null)
            throw new ArgumentNullException(nameof(cell));
        currentCell = cell;
    }

    /// <summary>被捕食；本身已经死亡或取食失败时返回 false。</summary>
    public bool TakeBite(int amount)
    {
        if (amount <= 0 || dead)
            return false;
        if (BodySize == E_BodySize.Large && amount < 2)
            return false;

        MarkDead();
        return true;
    }

    /// <summary>改变了携带物，供蚂蚁搬运等场景写入。</summary>
    public void SetCarried(E_ResourceKind kind)
    {
        Carried = kind;
    }

    /// <summary>给界面与占位可视化用的一行状态描述。</summary>
    public virtual string DescribeShort()
    {
        string carried = Carried != E_ResourceKind.None ? " 携带" + Carried : string.Empty;
        return DisplayName + " 饱腹" + satiety + " 饥饿" + starvation + carried;
    }

    /// <summary>
    /// 决策优先级的固定骨架。
    /// 策划案要求先处理危险、再携带物、再饥饿、最后常规行为；
    /// 顺序写在这里，子类只需给出各层的物种实现，不会漏掉某一层。
    /// </summary>
    private AnimalSettlePlan BuildPlan(int day, IAnimalWorld world, ICellEnv cell)
    {
        var plan = new AnimalSettlePlan();

        IsSheltered = false;

        // 1. 危险优先：附近有捕食者时先逃或找安全点，本晚不再去取食物。
        if (TryEscape(plan, day, world, cell))
            return plan;

        // 2. 携带物：已经拿着东西就先送到位，不允许同一晚再去捕食。
        if (TryDeliver(plan, day, world, cell))
            return plan;

        // 3. 饥饿：饱腹为 0 时才专门找食物。
        if (satiety == 0 && TryForage(plan, day, world, cell))
            return plan;

        // 4. 常规行为；没有合法目标时保持 None，本晚休眠。
        TryRoutine(plan, day, world, cell);
        return plan;
    }

    /// <summary>第一层：试图逃离危险或躲进庇护。返回是否已确定今晚的计划。</summary>
    protected virtual bool TryEscape(AnimalSettlePlan plan, int day, IAnimalWorld world, ICellEnv cell)
    {
        return false;
    }

    /// <summary>第二层：把携带物送到合法位置。返回是否已确定今晚的计划。</summary>
    protected virtual bool TryDeliver(AnimalSettlePlan plan, int day, IAnimalWorld world, ICellEnv cell)
    {
        return false;
    }

    /// <summary>第三层：找食物。仅在饱腹为 0 时被调用。</summary>
    protected virtual bool TryForage(AnimalSettlePlan plan, int day, IAnimalWorld world, ICellEnv cell)
    {
        return false;
    }

    /// <summary>第四层：筑巢、搬运、巡游或停留等常规行为。</summary>
    protected virtual void TryRoutine(AnimalSettlePlan plan, int day, IAnimalWorld world, ICellEnv cell)
    {
        Rest(plan, "无事可做，原地停留。");
    }

    /// <summary>
    /// 统一写回。基础数值（饱腹衰减、饥饿累加、携带与记忆）在这里处理，
    /// 对植物和格子的写入由编排器按裁决结果执行，因此这里不会出现隐藏的顺序依赖。
    /// </summary>
    protected virtual void ApplyPlan(AnimalSettlePlan plan, IAnimalWorld world, ICellEnv cell)
    {
        if (plan.StarvedToDeath)
        {
            MarkDead();
            return;
        }

        // 策划案：每晚自然下降 1；成功进食后恢复 2，最高 3。
        satiety = Mathf.Clamp(satiety - SatietyDecayPerNight + plan.SatietyGain, 0, MaxSatiety);

        // 策划案：饱腹归零且未进食时才累加饥饿；吃到东西就清零。
        // 衰减与累加统一在这里发生，BuildPlan 只做预测，避免重复计数。
        if (plan.SatietyGain > 0)
            starvation = 0;
        else if (satiety == 0)
            starvation = Mathf.Min(MaxStarvation, starvation + 1);

        if (plan.PickUpCarried)
        {
            Carried = plan.CarryKind;
            if (plan.SeedSource != null)
                CarriedSeedSpecies = plan.SeedSource.Species;
        }

        if (plan.PutDownCarried)
        {
            Carried = E_ResourceKind.None;
            CarriedSeedSpecies = null;
        }

        IsSheltered = plan.EntersShelter;

        if (plan.RememberFood != null)
            RememberedFood = plan.RememberFood;
        if (plan.RememberSafety != null)
            RememberedSafety = plan.RememberSafety;

        failedNights = plan.ResetFailedNights
            ? 0
            : plan.AdvanceFailedNights
                ? failedNights + 1
                : failedNights;
    }

    /// <summary>原地停留，并记录一行可读的原因。本方法是纯读的，数值变化交给 ApplyPlan。</summary>
    protected void Rest(AnimalSettlePlan plan, string reason)
    {
        plan.Goal = E_AnimalGoal.None;
        plan.Action = E_AnimalAction.None;
        plan.Log(RulePrefix + ".REST", DisplayName + reason);
    }

    /// <summary>
    /// 预测本晚吃不到东西的后果。
    /// 这里只算不改： starvation 达到上限时把 StarvedToDeath 标进提案，
    /// 由编排器在写回阶段真正死亡并落余屑。
    /// </summary>
    protected void Starve(AnimalSettlePlan plan)
    {
        int predictedSatiety = Mathf.Max(0, satiety - SatietyDecayPerNight);
        if (predictedSatiety > 0)
            return;

        if (starvation + 1 >= MaxStarvation)
        {
            plan.StarvedToDeath = true;
            plan.RecycleToDetritus = RecycleAmount;
            plan.Log(RulePrefix + ".STARVED",
                DisplayName + " 连续 " + (starvation + 1) + " 晚没有进食，死亡并转为 "
                + RecycleAmount + " 份余屑。");
            return;
        }

        plan.Log(RulePrefix + ".HUNGRY",
            DisplayName + " 没有找到食物，饥饿计数上升到 " + (starvation + 1) + "。");
    }

    /// <summary>成功吃到东西：恢复饱腹并清零饥饿。</summary>
    protected void Feed(AnimalSettlePlan plan, E_AnimalAction action, string what)
    {
        plan.Action = action;
        plan.SatietyGain = SatietyPerMeal;
        plan.ResetStarvation = true;
        plan.ResetFailedNights = true;
        plan.Log(RulePrefix + ".FEED", DisplayName + " 取食" + what + "，饱腹恢复。");
    }

    /// <summary>目标无法推进：保留目标并累计失败夜数。</summary>
    protected void FailToAdvance(AnimalSettlePlan plan, string reason)
    {
        plan.AdvanceFailedNights = true;
        plan.Log(RulePrefix + ".STUCK", DisplayName + reason);
    }

    /// <summary>
    /// 是否愿意在当前天气活动。雨行动物晴天打折、昼行动物雨天打折，
    /// 但都不直接禁止行动——策划案要求天气只提供机会，不替动物做决定。
    /// </summary>
    protected float WeatherPreference(IAnimalWorld world)
    {
        bool rain = world.Meets(E_PlantNeed.Moisture);
        if (HasTag(E_ActivityTag.RainActive))
            return rain ? 1.25f : 0.8f;
        if (HasTag(E_ActivityTag.Diurnal))
            return rain ? 0.8f : 1.15f;
        return 1f;
    }

    public bool HasTag(E_ActivityTag tag)
    {
        return (Tags & tag) == tag;
    }

    /// <summary>该目标是否可以作为猎物：满足食物网关系、没有有效庇护、且体型合适。</summary>
    protected bool CanHunt(AnimalsBase target, IAnimalWorld world)
    {
        if (target == null || target.IsDead || target == this)
            return false;
        if (!AnimalFoodWeb.CanPrey(Species, target.Species))
            return false;
        // 策划案：目标没有有效庇护才允许捕食。
        if (AnimalFoodWeb.HasShelter(target.CurrentCell))
            return false;
        // 衰弱时不能主动捕食大型目标。
        return HungerState != E_HungerState.Weak || target.BodySize != E_BodySize.Large;
    }

    /// <summary>该植物是否可食：取<see cref="IPlantInteraction.FoodAmount"/>判断。</summary>
    protected bool CanGraze(PlantsBase plant)
    {
        return plant != null && !plant.IsDead && plant.FoodAmount > 0 && CanEatPlant(plant.Species);
    }

    /// <summary>本物种是否把该植物列为食物。</summary>
    protected abstract bool CanEatPlant(E_PlantSpecies species);

    /// <summary>本物种是否吃格子上的余屑。</summary>
    protected abstract bool EatsDetritus { get; }

    protected void MarkDead()
    {
        dead = true;
    }

    /// <summary>
    /// 与 <see cref="PickBest"/> 相同，但排除出发格本身。
    /// 逃跑、送货这类行为的目标必须是"换个地方"，留在原地不算达成本层优先级。
    /// </summary>
    protected static ICellEnv PickBestElsewhere(IAnimalWorld world, ICellEnv from, int maxSteps,
        Func<ICellEnv, float> score)
    {
        return PickBest(world, from, maxSteps,
            candidate => ReferenceEquals(candidate, from) ? float.NegativeInfinity : score(candidate));
    }

    /// <summary>
    /// 在固定的邻域里按评分挑最佳格子。
    /// 评分完全由调用方的 <paramref name="score"/> 决定，这里只走固定的遍历顺序，
    /// 因此相同输入必然得到相同结果。
    /// </summary>
    protected static ICellEnv PickBest(IAnimalWorld world, ICellEnv from, int maxSteps, Func<ICellEnv, float> score)
    {
        ICellEnv best = null;
        float bestScore = float.NegativeInfinity;

        IReadOnlyList<ICellEnv> cells = world.Cells;
        for (int i = 0; i < cells.Count; i++)
        {
            ICellEnv candidate = cells[i];
            if (candidate == null)
                continue;

            int distance = world.StepDistance(from, candidate);
            if (distance < 0 || distance > maxSteps)
                continue;

            float value = score(candidate);
            if (value <= bestScore)
                continue;

            bestScore = value;
            best = candidate;
        }

        return bestScore > float.NegativeInfinity ? best : null;
    }

    /// <summary>本格是否可以提供庇护：任一存活植物声明自己可作庇护。</summary>
    protected static bool CellHasShelter(ICellEnv cell)
    {
        return AnimalFoodWeb.HasShelter(cell);
    }

    // ---- 以下三个感知 helper 是所有食性的动物共用的，放在基类避免五个物种各写一遍 ----

    /// <summary>
    /// 在今晚可达范围内找一只可捕食的猎物。
    /// 按固定顺序遍历地图，因此相同状态永远选中同一只，满足可复现性。
    /// </summary>
    protected AnimalsBase FindPrey(IAnimalWorld world, int maxSteps)
    {
        AnimalsBase best = null;
        int bestDistance = int.MaxValue;

        IReadOnlyList<ICellEnv> cells = world.Cells;
        for (int i = 0; i < cells.Count; i++)
        {
            ICellEnv cell = cells[i];
            if (cell == null)
                continue;

            IReadOnlyList<AnimalsBase> here = world.AnimalsAt(cell);
            for (int a = 0; a < here.Count; a++)
            {
                AnimalsBase candidate = here[a];
                if (!CanHunt(candidate, world))
                    continue;

                int distance = world.StepDistance(currentCell, cell);
                if (distance < 0 || distance > maxSteps || distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>在可达范围内找一株可食的植物，优先份量多、距离近的。</summary>
    protected PlantsBase FindEdiblePlant(IAnimalWorld world, int maxSteps)
    {
        PlantsBase best = null;
        float bestScore = float.NegativeInfinity;

        IReadOnlyList<ICellEnv> cells = world.Cells;
        for (int i = 0; i < cells.Count; i++)
        {
            ICellEnv cell = cells[i];
            if (cell == null)
                continue;

            int distance = world.StepDistance(currentCell, cell);
            if (distance < 0 || distance > maxSteps)
                continue;

            PlantsBase candidate = BestFoodOn(cell);
            if (!CanGraze(candidate))
                continue;

            // 策划案：同类目标按距离、资源量和安全度评分。安全度已在 CanHunt 侧处理。
            float score = candidate.FoodAmount * 2f - distance;
            if (score <= bestScore)
                continue;

            bestScore = score;
            best = candidate;
        }

        return best;
    }

    /// <summary>找出植物所在的格子；感知到植物再走过去时需要它。</summary>
    protected static ICellEnv FindCellOf(IAnimalWorld world, PlantsBase plant)
    {
        if (world == null || plant == null)
            return null;

        IReadOnlyList<ICellEnv> cells = world.Cells;
        for (int i = 0; i < cells.Count; i++)
        {
            ICellEnv cell = cells[i];
            if (cell == null)
                continue;
            if (cell.Subject == plant)
                return cell;

            IReadOnlyList<PlantsBase> cover = cell.Cover;
            for (int c = 0; c < cover.Count; c++)
            {
                if (cover[c] == plant)
                    return cell;
            }
        }

        return null;
    }

    /// <summary>取这一格上份量最多、且尚可食用的那株植物；没有则返回 null。</summary>
    private static PlantsBase BestFoodOn(ICellEnv cell)
    {
        PlantsBase best = null;

        PlantsBase subject = cell.Subject;
        if (subject != null && !subject.IsDead && subject.FoodAmount > 0)
            best = subject;

        IReadOnlyList<PlantsBase> cover = cell.Cover;
        for (int i = 0; i < cover.Count; i++)
        {
            PlantsBase candidate = cover[i];
            if (candidate == null || candidate.IsDead || candidate.FoodAmount <= 0)
                continue;
            if (best == null || candidate.FoodAmount > best.FoodAmount)
                best = candidate;
        }

        return best;
    }

    /// <summary>在可达范围内找一份余屑；取食成本最低的资源，优先近的。</summary>
    protected ICellEnv FindDetritus(IAnimalWorld world, int maxSteps)
    {        if (!EatsDetritus)
            return null;

        ICellEnv best = null;
        float bestScore = float.NegativeInfinity;

        IReadOnlyList<ICellEnv> cells = world.Cells;
        for (int i = 0; i < cells.Count; i++)
        {
            ICellEnv cell = cells[i];
            if (cell == null || cell.Detritus <= 0)
                continue;

            int distance = world.StepDistance(currentCell, cell);
            if (distance < 0 || distance > maxSteps)
                continue;

            float score = cell.Detritus * 2f - distance;
            if (score <= bestScore)
                continue;

            bestScore = score;
            best = cell;
        }

        return best;
    }
}
