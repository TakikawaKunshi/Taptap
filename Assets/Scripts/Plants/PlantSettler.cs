using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 一晚植物结算的完整结果。
/// 策划案要求玩家能回答"为什么它长了／死了／迁移了"，
/// 因此编排器除了推进状态，还要按分类收集可显示的日志。
/// </summary>
public sealed class PlantSettleReport
{
    /// <summary>本次结算属于第几晚。下游（例如动物结算）据此对齐同一晚，不必再推算天数。</summary>
    public int Day { get; internal set; }

    /// <summary>本晚处理过的植物数量。</summary>
    public int SettledCount { get; internal set; }

    /// <summary>提案阶段被判定为无需变化的植物数量。</summary>
    public int UnchangedCount { get; internal set; }

    /// <summary>本晚新扩张出来的植物。</summary>
    public List<PlantsBase> NewGrowths { get; } = new List<PlantsBase>();

    /// <summary>本晚回收成余屑的植物。</summary>
    public List<PlantsBase> Recycled { get; } = new List<PlantsBase>();

    /// <summary>全部可解释记录，按发生顺序。</summary>
    public List<PlantLogEntry> Logs { get; } = new List<PlantLogEntry>();

    /// <summary>本晚产出的资源汇总。</summary>
    public Dictionary<E_ResourceKind, int> Produced { get; } = new Dictionary<E_ResourceKind, int>();

    /// <summary>扩张竞争被拒绝的次数；策划案要求竞争结果与遍历顺序无关。</summary>
    public int ExpansionRejected { get; internal set; }

    public void AddLog(PlantSettlePlan plan)
    {
        if (plan == null)
            return;
        for (int i = 0; i < plan.Logs.Count; i++)
            Logs.Add(plan.Logs[i]);
    }

    public void AddProduced(PlantSettlePlan plan)
    {
        if (plan == null || plan.ProduceAmount <= 0 || plan.ProduceKind == E_ResourceKind.None)
            return;
        if (Produced.TryGetValue(plan.ProduceKind, out int existing))
            Produced[plan.ProduceKind] = existing + plan.ProduceAmount;
        else
            Produced[plan.ProduceKind] = plan.ProduceAmount;
    }

    /// <summary>按策划案第三到第九步的顺序，把本晚结果拼成可直接写入事件日志的中文摘要。</summary>
    public string BuildSummary()
    {
        if (SettledCount == 0 && Logs.Count == 0)
            return "本晚没有任何植物发生变化。\n";

        var text = new System.Text.StringBuilder();
        text.Append("第 ").Append(SettledCount).Append(" 株植物参与结算");
        if (NewGrowths.Count > 0)
            text.Append("，新增 ").Append(NewGrowths.Count).Append(" 株");
        if (Recycled.Count > 0)
            text.Append("，回收 ").Append(Recycled.Count).Append(" 株");
        if (ExpansionRejected > 0)
            text.Append("，扩张被占满 ").Append(ExpansionRejected).Append(" 次");
        text.Append("。\n");

        if (Produced.Count > 0)
        {
            text.Append("产出：");
            bool first = true;
            foreach (KeyValuePair<E_ResourceKind, int> pair in Produced)
            {
                if (!first)
                    text.Append('、');
                text.Append(DescribeResource(pair.Key)).Append('x').Append(pair.Value);
                first = false;
            }

            text.Append('\n');
        }

        for (int i = 0; i < Logs.Count; i++)
            text.Append("· ").Append(Logs[i].Text).Append('\n');
        return text.ToString();
    }

    private static string DescribeResource(E_ResourceKind kind)
    {
        switch (kind)
        {
            case E_ResourceKind.Detritus: return "余屑";
            case E_ResourceKind.Light: return "光粒";
            case E_ResourceKind.Spore: return "孢子";
            case E_ResourceKind.Seed: return "种子";
            case E_ResourceKind.Fibre: return "织材";
            default: return kind.ToString();
        }
    }
}

/// <summary>
/// 植物结算编排器。
/// 策划案要求"同一资源点每晚只允许一份资源被预留，竞争由固定规则裁决，不按对象遍历顺序偷偷决定赢家"，
/// 因此这里分成三步：按固定顺序收集植物、只读地收集全部提案、按顺序裁决并提交。
/// 固定顺序沿用 TileMapManager 的既有做法：先比到中心的距离，再比坐标。
/// 每次扩张随机选择可用邻格；固定随机种子时，结果仍不受传入格子的遍历顺序影响。
/// </summary>
public sealed class PlantSettler
{
    private readonly System.Random expansionRandom;

    public PlantSettler()
    {
        expansionRandom = new System.Random();
    }

    /// <summary>指定随机种子，便于重现同一条扩张路径。</summary>
    public PlantSettler(int randomSeed)
    {
        expansionRandom = new System.Random(randomSeed);
    }

    /// <summary>收集全部格子，按固定顺序返回，便于结算结果可复现。</summary>
    public static List<ICellEnv> CollectInFixedOrder(IEnumerable<ICellEnv> cells)
    {
        var list = new List<ICellEnv>();
        if (cells == null)
            return list;
        foreach (ICellEnv cell in cells)
        {
            if (cell != null)
                list.Add(cell);
        }

        list.Sort(CompareByDistanceThenCoordinate);
        return list;
    }

    /// <summary>
    /// 固定排序规则：到地图中心的距离升序，距离相同时按 (y, x) 升序。
    /// 与 TileMapManager 中"距离相同时选左下方格子"的既有规则保持一致。
    /// </summary>
    private static int CompareByDistanceThenCoordinate(ICellEnv left, ICellEnv right)
    {
        float dl = left.Coordinates.sqrMagnitude;
        float dr = right.Coordinates.sqrMagnitude;
        if (!Mathf.Approximately(dl, dr))
            return dl.CompareTo(dr);
        if (left.Coordinates.y != right.Coordinates.y)
            return left.Coordinates.y.CompareTo(right.Coordinates.y);
        return left.Coordinates.x.CompareTo(right.Coordinates.x);
    }

    /// <summary>
    /// 结算地图上的全部植物。
    /// 传入的天气与格子都不需要是 UnityEngine 对象，因此整个流程可以在 EditMode 下完整验证。
    /// </summary>
    public PlantSettleReport Settle(int day, IWeatherQuery weather, IEnumerable<ICellEnv> cells)
    {
        var report = new PlantSettleReport();
        if (day < 1)
            throw new ArgumentOutOfRangeException(nameof(day), "天数必须从 1 开始。");
        if (weather == null)
            throw new ArgumentNullException(nameof(weather));
        report.Day = day;

        List<ICellEnv> ordered = CollectInFixedOrder(cells);
        DetritusLifecycle.SettleCells(day, ordered, report.Logs);
        var proposals = CollectProposals(ordered, day, weather);

        ResolveExpansion(proposals, ordered, day, report);
        CommitAll(proposals, day, report);
        return report;
    }

    /// <summary>结算单个格子上的植物，便于测试与局部刷新。</summary>
    public PlantSettleReport SettleCell(int day, IWeatherQuery weather, ICellEnv cell)
    {
        var report = new PlantSettleReport();
        if (day < 1)
            throw new ArgumentOutOfRangeException(nameof(day), "天数必须从 1 开始。");
        if (weather == null)
            throw new ArgumentNullException(nameof(weather));
        report.Day = day;
        if (cell == null)
            throw new ArgumentNullException(nameof(cell));

        var single = new List<ICellEnv> { cell };
        DetritusLifecycle.SettleCells(day, single, report.Logs);
        List<Proposal> proposals = CollectProposals(single, day, weather);
        ResolveExpansion(proposals, single, day, report);
        CommitAll(proposals, day, report);
        return report;
    }

    /// <summary>
    /// 按物种声明的占位槽判断能不能放进这一格。
    /// 只依赖 <see cref="ICellEnv"/> 已有的成员，因此格子层不需要为此改动。
    /// 注意它与 <see cref="ICellEnv.CanOccupy"/> 不同：后者代表"整格全空"，
    /// 而主体植物所在格仍然可以再铺一层苔藓。
    /// </summary>
    public static bool CanOccupy(ICellEnv cell, E_OccupancySlot slot)
    {
        if (cell == null || slot == E_OccupancySlot.Subject)
            return cell != null && cell.Subject == null;

        IReadOnlyList<PlantsBase> cover = cell.Cover;
        for (int i = 0; i < cover.Count; i++)
        {
            if (cover[i] != null && !cover[i].IsDead)
                return false;
        }

        return true;
    }

    /// <summary>把一棵植物放到指定格子；地表覆盖与主体位按物种自动选择。</summary>
    public static bool Place(PlantsBase plant, ICellEnv cell)
    {
        if (plant == null)
            throw new ArgumentNullException(nameof(plant));
        if (cell == null)
            throw new ArgumentNullException(nameof(cell));
        if (!CanOccupy(cell, plant.OccupancySlot))
            return false;

        plant.PlaceAt(cell);
        // 策划案：占位方式由物种自己声明，不能按"孢子／种子"的基类二分。
        // 苔藓与藻类是地表覆盖，可与主体共存；蘑菇和成熟种子植物占主体格。
        return plant.OccupancySlot == E_OccupancySlot.Subject
            ? cell.TrySetSubject(plant)
            : cell.TryAddCover(plant);
    }

    /// <summary>按物种创建一株植物，不放到任何格子上。</summary>
    public static PlantsBase Create(E_PlantSpecies species)
    {
        switch (species)
        {
            case E_PlantSpecies.Algae: return new Algae();
            case E_PlantSpecies.Moss: return new Moss();
            case E_PlantSpecies.Mushroom: return new Mushroom();
            case E_PlantSpecies.Gymnosperm: return new Gymnosperm();
            case E_PlantSpecies.Angiosperm: return new Angiosperm();
            default: throw new ArgumentOutOfRangeException(nameof(species), "没有该物种的实现。");
        }
    }

    private readonly struct Proposal
    {
        public Proposal(PlantsBase plant, ICellEnv cell, PlantSettlePlan plan)
        {
            Plant = plant;
            Cell = cell;
            Plan = plan;
        }

        public PlantsBase Plant { get; }

        public ICellEnv Cell { get; }

        public PlantSettlePlan Plan { get; }
    }

    /// <summary>第一步：只读地收集全部提案。提案顺序与格子顺序一致。</summary>
    private static List<Proposal> CollectProposals(List<ICellEnv> cells, int day, IWeatherQuery weather)
    {
        var proposals = new List<Proposal>();
        for (int c = 0; c < cells.Count; c++)
        {
            ICellEnv cell = cells[c];
            foreach (PlantsBase plant in EnumeratePlants(cell))
            {
                PlantSettlePlan plan = plant.Propose(day, weather, cell);
                if (plan == null)
                    continue;
                proposals.Add(new Proposal(plant, cell, plan));
            }
        }

        return proposals;
    }

    /// <summary>按主体优先、再覆盖的顺序枚举本格植物，保证同一格的结算顺序固定。</summary>
    private static IEnumerable<PlantsBase> EnumeratePlants(ICellEnv cell)
    {
        if (cell.Subject != null && !cell.Subject.IsDead)
            yield return cell.Subject;
        IReadOnlyList<PlantsBase> cover = cell.Cover;
        for (int i = 0; i < cover.Count; i++)
        {
            if (cover[i] != null && !cover[i].IsDead)
                yield return cover[i];
        }
    }

    /// <summary>
    /// 第二步：裁决扩张竞争。
    /// 苔藓只提出"我想扩张"，这里按固定提案顺序处理，每次从仍可占用的邻格中等概率随机选一格。
    /// 被占满时拒绝该提案，并记录原因；同一随机种子下不受传入格子的遍历顺序影响。
    /// </summary>
    private void ResolveExpansion(List<Proposal> proposals, List<ICellEnv> cells, int day, PlantSettleReport report)
    {
        var reserved = new HashSet<ICellEnv>();
        for (int i = 0; i < proposals.Count; i++)
        {
            Proposal proposal = proposals[i];
            if (!proposal.Plan.RequestsExpansion)
                continue;

            ICellEnv target = FindExpansionTarget(proposal.Cell, reserved);
            if (target == null)
            {
                proposal.Plan.RequestsExpansion = false;
                report.ExpansionRejected++;
                proposal.Plan.Log(proposal.Plant.RulePrefix + ".EXPAND_FULL",
                    proposal.Plant.DisplayName + " 申请扩张，但相邻格子本晚已被占满。");
                continue;
            }

            reserved.Add(target);
            PlantsBase growth = Create(proposal.Plant.Species);
            if (!Place(growth, target))
            {
                reserved.Remove(target);
                proposal.Plan.RequestsExpansion = false;
                report.ExpansionRejected++;
                continue;
            }

            proposal.Plan.RequestsExpansion = false;
            proposal.Plan.ExpansionGranted = true;
            proposal.Plan.Log(proposal.Plant.RulePrefix + ".EXPAND",
                proposal.Plant.DisplayName + " 向 " + target.Coordinates + " 扩张。");
            report.NewGrowths.Add(growth);
        }
    }

    /// <summary>每次从可占用且未被本晚预留的邻格中等概率随机选择，避免偏向固定方向。</summary>
    private ICellEnv FindExpansionTarget(ICellEnv cell, HashSet<ICellEnv> reserved)
    {
        var candidates = new List<ICellEnv>(4);
        IReadOnlyList<ICellEnv> neighbors = cell.Neighbors;
        for (int i = 0; i < neighbors.Count; i++)
        {
            ICellEnv neighbor = neighbors[i];
            if (neighbor == null || !neighbor.CanOccupy || reserved.Contains(neighbor))
                continue;
            candidates.Add(neighbor);
        }

        return candidates.Count > 0 ? candidates[expansionRandom.Next(candidates.Count)] : null;
    }

    /// <summary>第三步：按固定顺序提交，保证同一晚的累计结果不受遍历顺序影响。</summary>
    private static void CommitAll(List<Proposal> proposals, int day, PlantSettleReport report)
    {
        for (int i = 0; i < proposals.Count; i++)
        {
            Proposal proposal = proposals[i];
            if (!proposal.Plant.Commit(day, proposal.Cell, proposal.Plan))
                continue;

            report.SettledCount++;
            report.AddLog(proposal.Plan);
            report.AddProduced(proposal.Plan);
            if (proposal.Plant.IsDead)
            {
                // 回收成余屑后必须把位置还回去，否则格子层的 subject / cover 会一直挂着死株，
                // 后续既不能放新植物，也会拿到过期的"本格已有植物"判断。
                proposal.Cell.Release(proposal.Plant);
                report.Recycled.Add(proposal.Plant);
            }
            else if (proposal.Plan.IsEmpty)
                report.UnchangedCount++;
        }
    }
}
