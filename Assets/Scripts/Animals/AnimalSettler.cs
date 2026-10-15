using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 一晚动物结算的完整结果。
/// 与 <see cref="PlantSettleReport"/> 对称：除了推进状态，还要按策划案要求
/// 把"发生了几次捕食、几次取食、几次饥饿死亡"记录下来，供晨报回答"到底发生了什么"。
/// </summary>
public sealed class AnimalSettleReport
{
    /// <summary>本晚参与结算的动物数量。</summary>
    public int SettledCount { get; internal set; }

    /// <summary>本晚发生位移的动物数量。</summary>
    public int MovedCount { get; internal set; }

    /// <summary>本晚成功发生的捕食次数。</summary>
    public int HuntedCount { get; internal set; }

    /// <summary>本晚成功发生的植物取食次数。</summary>
    public int GrazedCount { get; internal set; }

    /// <summary>本晚成功消耗的余屑份数。</summary>
    public int ScavengedCount { get; internal set; }

    /// <summary>本晚因饥饿死亡的动物数量。</summary>
    public int StarvedCount { get; internal set; }

    /// <summary>本晚新播种出来的植物。</summary>
    public List<PlantsBase> NewPlants { get; } = new List<PlantsBase>();

    /// <summary>本晚死亡的动物，含被捕食与饿死。</summary>
    public List<AnimalsBase> Dead { get; } = new List<AnimalsBase>();

    /// <summary>资源竞争被拒绝的次数；被拒绝的一方本晚改为空手而归。</summary>
    public int Rejected { get; internal set; }

    /// <summary>全部可解释记录，按发生顺序。</summary>
    public List<PlantLogEntry> Logs { get; } = new List<PlantLogEntry>();

    public void AddLogs(AnimalSettlePlan plan)
    {
        if (plan == null)
            return;
        for (int i = 0; i < plan.Logs.Count; i++)
            Logs.Add(plan.Logs[i]);
    }

    /// <summary>把本晚的动物侧结果拼成可直接并入晨报的中文摘要。</summary>
    public string BuildSummary()
    {
        if (SettledCount == 0 && Logs.Count == 0)
            return "本晚没有任何动物活动。\n";

        var text = new System.Text.StringBuilder();
        text.Append("第 ").Append(SettledCount).Append(" 只动物活动");
        if (MovedCount > 0)
            text.Append("，").Append(MovedCount).Append(" 只移动了位置");
        if (HuntedCount > 0)
            text.Append("，发生 ").Append(HuntedCount).Append(" 次捕食");
        if (GrazedCount > 0)
            text.Append("，").Append(GrazedCount).Append(" 次取食植物");
        if (ScavengedCount > 0)
            text.Append("，消耗 ").Append(ScavengedCount).Append(" 份余屑");
        if (StarvedCount > 0)
            text.Append("，").Append(StarvedCount).Append(" 只因饥饿死亡");
        if (NewPlants.Count > 0)
            text.Append("，新播下 ").Append(NewPlants.Count).Append(" 株植物");
        text.Append("。\n");

        for (int i = 0; i < Logs.Count; i++)
            text.Append("· ").Append(Logs[i].Text).Append('\n');
        return text.ToString();
    }
}

/// <summary>
/// 动物结算编排器。对应策划案第七章固定过夜结算顺序的第 6 至 8 步：
/// 更新动物饱腹与感知、统一提交移动提案、处理捕食与各类取食行为、处理饥饿死亡与回收。
///
/// 结构与 <see cref="PlantSettler"/> 保持一致的三段式：
/// 先只读收集提案，再统一裁决资源竞争，最后按固定顺序写回。
/// 策划案明确要求"同一资源点每晚只允许一份资源被预留，竞争由固定规则裁决，
/// 不按对象遍历顺序偷偷决定赢家"，所以一株植物、一格余屑、一只猎物
/// 一旦被某一只动物预留，同一晚就不会再被第二只动物吃第二次。
/// </summary>
public sealed class AnimalSettler
{
    /// <summary>建立当晚的世界视图；外部可以先拿到它做"预览今晚会发生什么"的只读查询。</summary>
    public static AnimalWorldView BuildWorld(IWeatherQuery weather, IEnumerable<ICellEnv> cells,
        IEnumerable<AnimalsBase> animals)
    {
        return new AnimalWorldView(weather, cells, animals);
    }

    /// <summary>
    /// 按固定顺序返回参与结算的动物。
    /// 顺序规则与植物结算一致：先按到中心的距离，再按 (y, x)，
    /// 同一格上的多只动物再按物种枚举值排序，保证结果可复现。
    /// </summary>
    public static List<AnimalsBase> CollectInFixedOrder(IEnumerable<AnimalsBase> animals)
    {
        var list = new List<AnimalsBase>();
        if (animals == null)
            return list;

        foreach (AnimalsBase animal in animals)
        {
            if (animal != null && !animal.IsDead)
                list.Add(animal);
        }

        list.Sort(CompareAnimals);
        return list;
    }

    private static int CompareAnimals(AnimalsBase left, AnimalsBase right)
    {
        float dl = left.Coordinates.sqrMagnitude;
        float dr = right.Coordinates.sqrMagnitude;
        if (!Mathf.Approximately(dl, dr))
            return dl.CompareTo(dr);
        if (left.Coordinates.y != right.Coordinates.y)
            return left.Coordinates.y.CompareTo(right.Coordinates.y);
        if (left.Coordinates.x != right.Coordinates.x)
            return left.Coordinates.x.CompareTo(right.Coordinates.x);
        return ((int)left.Species).CompareTo((int)right.Species);
    }

    /// <summary>按物种创建一只动物，不落到任何格子上。</summary>
    public static AnimalsBase Create(E_AnimalSpecies species, int startingSatiety = 2)
    {
        switch (species)
        {
            case E_AnimalSpecies.Frog: return new Frog(startingSatiety);
            case E_AnimalSpecies.Earthworm: return new Earthworm(startingSatiety);
            case E_AnimalSpecies.Snail: return new Snail(startingSatiety);
            case E_AnimalSpecies.Ant: return new Ant(startingSatiety);
            case E_AnimalSpecies.Spider: return new Spider(startingSatiety);
            default:
                throw new ArgumentOutOfRangeException(nameof(species),
                    "该物种属于扩展内容，首版还没有实现：" + species);
        }
    }

    /// <summary>把一只动物放到指定格子；动物不占用植物的主体位或覆盖位。</summary>
    public static void Place(AnimalsBase animal, ICellEnv cell)
    {
        if (animal == null)
            throw new ArgumentNullException(nameof(animal));
        if (cell == null)
            throw new ArgumentNullException(nameof(cell));
        animal.PlaceAt(cell);
    }

    /// <summary>
    /// 结算地图上的全部动物。
    /// 传入的天气与格子都不需要是 UnityEngine 对象，因此整个流程可以在 EditMode 下完整验证。
    /// </summary>
    public AnimalSettleReport Settle(int day, IWeatherQuery weather, IEnumerable<ICellEnv> cells,
        IEnumerable<AnimalsBase> animals)
    {
        var report = new AnimalSettleReport();
        if (day < 1)
            throw new ArgumentOutOfRangeException(nameof(day), "天数必须从 1 开始。");
        if (weather == null)
            throw new ArgumentNullException(nameof(weather));

        AnimalWorldView world = BuildWorld(weather, cells, animals);
        List<AnimalsBase> ordered = CollectInFixedOrder(animals);

        List<Proposal> proposals = CollectProposals(ordered, day, world, report);
        ResolveCompetition(proposals, world, report);
        CommitAll(proposals, day, world, report);
        return report;
    }

    private readonly struct Proposal
    {
        public Proposal(AnimalsBase animal, ICellEnv cell, AnimalSettlePlan plan)
        {
            Animal = animal;
            Cell = cell;
            Plan = plan;
        }

        /// <summary>参与结算的动物。</summary>
        public AnimalsBase Animal { get; }

        /// <summary>提出提案时所在的格子。</summary>
        public ICellEnv Cell { get; }

        public AnimalSettlePlan Plan { get; }
    }

    /// <summary>第一步：只读地收集全部提案。</summary>
    private static List<Proposal> CollectProposals(List<AnimalsBase> ordered, int day,
        AnimalWorldView world, AnimalSettleReport report)
    {
        var proposals = new List<Proposal>();
        for (int i = 0; i < ordered.Count; i++)
        {
            AnimalsBase animal = ordered[i];
            AnimalSettlePlan plan = animal.Propose(day, world, animal.CurrentCell);
            if (plan == null)
                continue;

            // 目标不可进入时就地保留，并累计失败夜数；策划案要求连续两晚才换行目标。
            if (plan.MoveTarget != null && !world.CanEnter(animal, plan.MoveTarget))
            {
                plan.MoveTarget = null;
                plan.AdvanceFailedNights = true;
                plan.Log(animal.RulePrefix + ".BLOCKED", animal.DisplayName + " 想去的位置无法通过。");
            }

            // 超过今晚的可用步数时同样保留目标，只走最近的合法位置。
            TrimTooFarMove(animal, plan, world);

            proposals.Add(new Proposal(animal, animal.CurrentCell, plan));
        }

        return proposals;
    }

    /// <summary>
    /// 把超出今晚步数上限的目标降级为"只走到范围内最近的一步"，
    /// 这样动物会朝目标持续推进，而不是因为一次走不到就放弃。
    /// </summary>
    private static void TrimTooFarMove(AnimalsBase animal, AnimalSettlePlan plan, AnimalWorldView world)
    {
        if (plan.MoveTarget == null)
            return;

        int distance = world.StepDistance(animal.CurrentCell, plan.MoveTarget);
        int limit = animal.MaxStepsForTonight;
        if (distance >= 0 && distance <= limit)
            return;

        // 朝着目标方向走到能走的最远处；找不到更好的就留在原地。
        ICellEnv best = animal.CurrentCell;
        int bestDistance = int.MaxValue;
        IReadOnlyList<ICellEnv> cells = world.Cells;
        for (int i = 0; i < cells.Count; i++)
        {
            ICellEnv candidate = cells[i];
            if (candidate == null || !world.CanEnter(animal, candidate))
                continue;

            int toMe = world.StepDistance(animal.CurrentCell, candidate);
            if (toMe < 0 || toMe > limit)
                continue;

            int remain = world.StepDistance(candidate, plan.MoveTarget);
            if (remain < 0 || remain >= bestDistance)
                continue;

            bestDistance = remain;
            best = candidate;
        }

        plan.MoveTarget = best == animal.CurrentCell ? null : best;
    }

    /// <summary>
    /// 第二步：裁决资源竞争。
    /// 同一份资源（一株植物、一格余屑、一只猎物）每晚只允许被一只动物预留，
    /// 未抢到的动物本晚只能记一行日志并退化为空手而归。
    /// </summary>
    private void ResolveCompetition(List<Proposal> proposals, AnimalWorldView world, AnimalSettleReport report)
    {
        var eatenPlants = new HashSet<PlantsBase>();
        var scavengedCells = new HashSet<ICellEnv>();
        var huntedAnimals = new HashSet<AnimalsBase>();

        for (int i = 0; i < proposals.Count; i++)
        {
            Proposal proposal = proposals[i];
            AnimalSettlePlan plan = proposal.Plan;
            AnimalsBase animal = proposal.Animal;

            if (plan.Action == E_AnimalAction.Graze && plan.PlantTarget != null)
            {
                if (!eatenPlants.Add(plan.PlantTarget))
                {
                    Reject(plan, animal, plan.PlantTarget.DisplayName + " 本晚已经被别的动物吃掉了。", report);
                    continue;
                }
            }
            else if (plan.Action == E_AnimalAction.Scavenge)
            {
                ICellEnv target = plan.MoveTarget ?? proposal.Cell;
                if (target != null && !scavengedCells.Add(target))
                {
                    Reject(plan, animal, "那处余屑本晚已经被别的动物先占了。", report);
                    continue;
                }
            }
            else if (plan.Action == E_AnimalAction.Hunt && plan.AnimalTarget != null)
            {
                if (!huntedAnimals.Add(plan.AnimalTarget))
                {
                    Reject(plan, animal, plan.AnimalTarget.DisplayName + " 本晚已经被别的捕食者盯上了。", report);
                    continue;
                }
            }
        }
    }

    private void Reject(AnimalSettlePlan plan, AnimalsBase animal, string reason, AnimalSettleReport report)
    {
        // 抢不到资源不代表吃饱了：清空进食收益，让后面的写回阶段照常扣饱腹。
        plan.SatietyGain = 0;
        plan.ResetStarvation = false;
        plan.DetritusDelta = 0;
        plan.BiteAmount = 0;
        plan.Log(animal.RulePrefix + ".CONTEST", animal.DisplayName + reason);
        report.Rejected++;
    }

    /// <summary>第三步：按固定顺序写回，位移、行为与自身状态在同一个循环里落地。</summary>
    private void CommitAll(List<Proposal> proposals, int day, AnimalWorldView world, AnimalSettleReport report)
    {
        for (int i = 0; i < proposals.Count; i++)
        {
            Proposal proposal = proposals[i];
            AnimalsBase animal = proposal.Animal;
            AnimalSettlePlan plan = proposal.Plan;

            ICellEnv from = proposal.Cell;
            ICellEnv destination = from;

            if (plan.MoveTarget != null)
            {
                destination = plan.MoveTarget;
                animal.MoveTo(destination);
                world.MoveAnimal(animal, from, destination);
                plan.MoveGranted = true;
                report.MovedCount++;

                // 把位移写进晨报：否则玩家只能看到"几只移动了"，
                // 无法在日志里核对它到底从哪一格走到了哪一格。
                plan.Log(animal.RulePrefix + ".MOVE",
                    animal.DisplayName + " 从 " + from.Coordinates + " 移动到 " + destination.Coordinates + "。");
            }

            ApplyAction(animal, plan, destination, world, report);

            if (animal.Commit(day, world, destination, plan))
            {
                report.SettledCount++;
                report.AddLogs(plan);
            }

            // 被捕食或饿死的动物要从快照里摘掉，后面的同晚查询才不会再看到它们。
            if (animal.IsDead)
            {
                world.RemoveAnimal(animal, destination);
                if (!report.Dead.Contains(animal))
                    report.Dead.Add(animal);
            }
        }
    }

    /// <summary>执行那个主要动作；所有对植物和格子的写入都集中在这里。</summary>
    private void ApplyAction(AnimalsBase animal, AnimalSettlePlan plan, ICellEnv cell,
        AnimalWorldView world, AnimalSettleReport report)
    {
        // 拾起种子：真正把它从成熟株上取走发生在这里。
        // BuildPlan 只登记意图，否则只读地预览一晚就会把种子提前消耗掉。
        if (plan.PickUpCarried && plan.SeedSource != null)
            plan.SeedSource.OnSeedTaken();

        switch (plan.Action)
        {
            case E_AnimalAction.Hunt:
                TryHunt(animal, plan, world, report);
                break;

            case E_AnimalAction.Graze:
                if (plan.PlantTarget == null || plan.PlantTarget.IsDead)
                    break;
                if (!plan.PlantTarget.TakeBite(plan.BiteAmount))
                {
                    plan.Log(animal.RulePrefix + ".GRAZE_FAIL",
                        animal.DisplayName + " 没能吃到 " + plan.PlantTarget.DisplayName + "。");
                    plan.SatietyGain = 0;
                    plan.ResetStarvation = false;
                    break;
                }

                report.GrazedCount++;
                break;

            case E_AnimalAction.Scavenge:
                if (plan.DetritusDelta >= 0 || cell.Detritus <= 0)
                    break;
                cell.Detritus += plan.DetritusDelta;
                report.ScavengedCount++;
                break;

            case E_AnimalAction.Till:
                ApplyTill(animal, plan, cell, world);
                break;

            case E_AnimalAction.Carry:
                ApplyCarry(animal, plan, cell, report);
                break;

            case E_AnimalAction.Weave:
            case E_AnimalAction.Hide:
                break;
        }

        // 饿死的个体当场转为余屑，回收真的落到实处而不是只写日志。
        if (plan.StarvedToDeath)
        {
            if (cell != null && plan.RecycleToDetritus > 0)
                cell.Detritus += plan.RecycleToDetritus;
            report.StarvedCount++;
        }
    }

    /// <summary>
    /// 捕食是否能真正成立。策划案要求"目标没有有效庇护"才允许捕食，
    /// 而目标可能在移动后躲进了庇护里，因此这里必须在写回阶段再确认一次。
    /// 不成立时双方只改变目标和饥饿状态，不作强制死亡。
    /// </summary>
    private void TryHunt(AnimalsBase hunter, AnimalSettlePlan plan, AnimalWorldView world,
        AnimalSettleReport report)
    {
        AnimalsBase prey = plan.AnimalTarget;
        if (prey == null || prey.IsDead)
            return;

        // 猎物可以借助苔藓、菌伞、巢穴逃离：此时捕食失败。
        if (AnimalFoodWeb.HasShelter(prey.CurrentCell))
        {
            plan.SatietyGain = 0;
            plan.ResetStarvation = false;
            plan.Log(hunter.RulePrefix + ".HUNT_ESCAPED",
                hunter.DisplayName + " 扑了个空：" + prey.DisplayName + " 躲进了遮蔽里。");
            return;
        }

        if (!prey.TakeBite(plan.BiteAmount))
            return;

        // 死亡当场转成余屑，让"死亡 → 余屑 → 蘑菇与蚯蚓"的闭环在同一晚就开始。
        if (prey.CurrentCell != null)
            prey.CurrentCell.Detritus += prey.RecycleYield;

        world.RemoveAnimal(prey, prey.CurrentCell);
        report.HuntedCount++;
        if (!report.Dead.Contains(prey))
            report.Dead.Add(prey);
    }

    /// <summary>蚯蚓松土：给本格与邻格里的一株种子植物追加成长点，选择了就固定 (y,x) 顺序。</summary>
    private void ApplyTill(AnimalsBase animal, AnimalSettlePlan plan, ICellEnv cell, AnimalWorldView world)
    {
        PlantsBase target = null;

        PlantsBase candidate = PickSeedling(cell);
        if (candidate != null)
            target = candidate;

        IReadOnlyList<ICellEnv> neighbors = cell.Neighbors;
        for (int i = 0; i < neighbors.Count && target == null; i++)
            target = PickSeedling(neighbors[i]);

        if (target == null)
        {
            plan.Log(animal.RulePrefix + ".TILL_IDLE", animal.DisplayName + " 松了土，但附近没有种子植物。");
            return;
        }

        target.GrantGrowth(plan.GrowthPointsToGrant);
        plan.Log(animal.RulePrefix + ".TILL_GROW",
            animal.DisplayName + " 松土，" + target.DisplayName + " 获得 " + plan.GrowthPointsToGrant + " 点成长。");
    }

    private static PlantsBase PickSeedling(ICellEnv cell)
    {
        if (cell == null)
            return null;

        PlantsBase subject = cell.Subject;
        SeedPlantsBase seed = subject as SeedPlantsBase;
        if (subject != null && !subject.IsDead && seed != null && seed.Stage < E_PlantStage.Mature)
            return subject;

        return null;
    }

    /// <summary>蚂蚁放下携带的种子：在空格上真的种出一株同种植物。</summary>
    private void ApplyCarry(AnimalsBase animal, AnimalSettlePlan plan, ICellEnv cell, AnimalSettleReport report)
    {
        if (!plan.PutDownCarried || animal.CarriedSeedSpecies == null)
            return;

        ICellEnv drop = plan.ActionCell ?? cell;
        if (drop == null || !drop.CanOccupy)
        {
            plan.Log(animal.RulePrefix + ".DROP_FAIL",
                animal.DisplayName + " 想在 " + (drop != null ? drop.Coordinates.ToString() : "目标格")
                + " 放下种子，但那里已经被占了。");
            return;
        }

        PlantsBase sapling = PlantSettler.Create(animal.CarriedSeedSpecies.Value);
        if (!PlantSettler.Place(sapling, drop))
        {
            plan.Log(animal.RulePrefix + ".DROP_FAIL", animal.DisplayName + " 没有能把种子种下去。");
            return;
        }

        plan.PlantTarget = sapling;
        plan.CarryKind = E_ResourceKind.Seed;
        report.NewPlants.Add(sapling);
        plan.Log(animal.RulePrefix + ".SEEDED",
            animal.DisplayName + " 把种子种在 " + drop.Coordinates + "，长出了 " + sapling.DisplayName + "。");
    }

    /// <summary>记录死亡并当场转成余屑，保证"死亡→余屑→蘑菇与蚯蚓"的闭环在同一晚就开始。</summary>
    private void Kill(AnimalsBase victim, int detritus)
    {
        if (victim == null || detritus <= 0)
            return;
        if (victim.CurrentCell != null)
            victim.CurrentCell.Detritus += detritus;
    }
}
