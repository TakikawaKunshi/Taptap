using System.Collections.Generic;

/// <summary>奶蛙：大型捕食者与种子搬运者。饥饿时捕食，饱腹时搬种子或利用树下庇护。</summary>
public sealed class MilkFrog : AnimalsBase
{
    private AnimalsBase previousPrey;
    private ICellEnv seedOrigin;

    public MilkFrog(int startingSatiety = 2) : base(startingSatiety) { }

    protected override E_AnimalSpecies SpeciesValue => E_AnimalSpecies.MilkFrog;
    public override string DisplayName => "奶蛙";
    public override string RulePrefix => "PREY.MILKFROG";
    public override E_ActivityTag Tags => E_ActivityTag.None;
    public override E_BodySize BodySize => E_BodySize.Large;
    // 策划案只规定回收量较高；这里取青蛙的两倍。
    protected override int RecycleAmount => 4;
    protected override bool CanEatPlant(E_PlantSpecies species) => false;
    protected override bool EatsDetritus => false;

    protected override bool TryForage(AnimalSettlePlan plan, int day, IAnimalWorld world, ICellEnv cell)
    {
        PlanForage(plan, world, FindRoutes(world), previousPrey);
        return true;
    }

    private void PlanForage(AnimalSettlePlan plan, IAnimalWorld world,
        Dictionary<ICellEnv, Route> routes, AnimalsBase preferredPrey)
    {
        AnimalsBase prey = null;
        int bestDistance = int.MaxValue;
        foreach (var pair in routes)
        {
            ICellEnv candidate = pair.Key;
            Route route = pair.Value;
            foreach (AnimalsBase animal in world.AnimalsAt(candidate))
            {
                // 世界快照可能已过期，不能把走开的猎物当成仍在原格。
                if (animal.CurrentCell != candidate || !CanHunt(animal, world)
                    || !IsBetterPrey(animal, route.Distance, prey, bestDistance, preferredPrey))
                    continue;
                prey = animal;
                bestDistance = route.Distance;
            }
        }

        if (prey == null)
        {
            Starve(plan);
            return;
        }

        plan.Goal = E_AnimalGoal.Food;
        plan.AnimalTarget = prey;
        plan.RememberFood = prey.CurrentCell;
        if (Travel(plan, prey.CurrentCell, routes))
        {
            plan.Action = E_AnimalAction.Hunt;
            plan.BiteAmount = 2;
            // 捕食收益在编排器确认实际捕获后才发放。
        }
        else
        {
            plan.Log(RulePrefix + ".CHASE", DisplayName + " 正在接近 " + prey.DisplayName + "，本晚尚未到达。");
        }
    }

    protected override bool TryDeliver(AnimalSettlePlan plan, int day, IAnimalWorld world, ICellEnv cell)
    {
        if (Carried != E_ResourceKind.Seed)
            return false;

        PlanDelivery(plan, world, FindRoutes(world));
        return true;
    }

    private void PlanDelivery(AnimalSettlePlan plan, IAnimalWorld world, Dictionary<ICellEnv, Route> routes)
    {
        plan.Goal = E_AnimalGoal.Delivery;
        ICellEnv drop = null;
        int bestDistance = int.MaxValue;
        foreach (ICellEnv candidate in world.Cells)
        {
            // 到达空地后可以就地播种，但不把刚捡起的种子放回原格。
            if (candidate == seedOrigin || !candidate.CanOccupy || !routes.TryGetValue(candidate, out Route route))
                continue;
            if (!IsCloser(candidate, route.Distance, drop, bestDistance))
                continue;
            drop = candidate;
            bestDistance = route.Distance;
        }

        if (drop == null)
        {
            FailToAdvance(plan, " 带着种子却找不到可达空地，继续保留种子。");
            return;
        }

        plan.ActionCell = drop;
        if (Travel(plan, drop, routes))
        {
            plan.Action = E_AnimalAction.Carry;
            plan.PutDownCarried = true;
            plan.ResetFailedNights = true;
        }
    }

    protected override void TryRoutine(AnimalSettlePlan plan, int day, IAnimalWorld world, ICellEnv cell)
    {
        PlanRoutine(plan, world, FindRoutes(world));
    }

    private void PlanRoutine(AnimalSettlePlan plan, IAnimalWorld world, Dictionary<ICellEnv, Route> routes)
    {
        ICellEnv source = null;
        int bestDistance = int.MaxValue;
        foreach (ICellEnv candidate in world.Cells)
        {
            if (!(candidate.Subject is SeedPlantsBase seed) || !seed.HasTransportableSeed
                || !routes.TryGetValue(candidate, out Route route))
                continue;
            if (!IsCloser(candidate, route.Distance, source, bestDistance))
                continue;
            source = candidate;
            bestDistance = route.Distance;
        }

        if (source != null)
        {
            plan.Goal = E_AnimalGoal.Delivery;
            if (Travel(plan, source, routes))
            {
                plan.Action = E_AnimalAction.Carry;
                plan.PickUpCarried = true;
                plan.CarryKind = E_ResourceKind.Seed;
                plan.SeedSource = source.Subject;
            }
            return;
        }

        // 大体型奶蛙使用幼株及成熟树下的庇护，不把小片苔藓当作自己的巢穴。
        ICellEnv shelter = null;
        bestDistance = int.MaxValue;
        foreach (ICellEnv candidate in world.Cells)
        {
            if (!(candidate.Subject is SeedPlantsBase tree) || tree.IsDead || !tree.IsShelter
                || !routes.TryGetValue(candidate, out Route route))
                continue;
            if (!IsCloser(candidate, route.Distance, shelter, bestDistance))
                continue;
            shelter = candidate;
            bestDistance = route.Distance;
        }

        if (shelter == null)
        {
            Rest(plan, " 没有搬运目标或可达的树下庇护，原地停留。");
            return;
        }

        plan.Goal = E_AnimalGoal.Shelter;
        if (Travel(plan, shelter, routes))
        {
            plan.Action = E_AnimalAction.Hide;
            plan.EntersShelter = true;
            plan.RememberSafety = shelter;
            plan.Log(RulePrefix + ".SHELTER", DisplayName + " 在植物下休息。");
        }
    }

    protected override void ApplyPlan(AnimalSettlePlan plan, IAnimalWorld world, ICellEnv cell)
    {
        base.ApplyPlan(plan, world, cell);
        if (plan.PickUpCarried) seedOrigin = cell;
        if (Carried != E_ResourceKind.Seed) seedOrigin = null;
        previousPrey = !IsDead && Satiety == 0 && plan.AnimalTarget != null && !plan.AnimalTarget.IsDead
            ? plan.AnimalTarget : null;
    }

    /// <summary>
    /// 闲置移动只读取同一套觅食、搬运和庇护决策，返回路径的下一格。
    /// 返回当前格表示已到达，等待过夜执行动作；false 表示没有目标，可以随机巡游。
    /// 不调用 Propose/Commit，不改变饱腹、庇护、携带物或结算天数。
    /// </summary>
    public bool TryGetIdleStep(IAnimalWorld world, AnimalsBase preferredPrey,
        out ICellEnv next, out AnimalsBase prey)
    {
        next = null;
        prey = null;
        if (world == null || CurrentCell == null || IsDead) return false;
        Dictionary<ICellEnv, Route> routes = FindRoutes(world);
        var plan = new AnimalSettlePlan();
        if (Carried == E_ResourceKind.Seed)
            PlanDelivery(plan, world, routes);
        else if (Satiety == 0)
            PlanForage(plan, world, routes, preferredPrey ?? previousPrey);
        else
            PlanRoutine(plan, world, routes);

        prey = plan.AnimalTarget;
        ICellEnv destination = plan.MoveTarget;
        if (destination == null)
        {
            if (plan.Action == E_AnimalAction.None) return false;
            next = CurrentCell;
            return true;
        }
        while (routes[destination].Distance > 1)
            destination = routes[destination].Previous;
        next = destination;
        return true;
    }

    private static bool IsBetterPrey(AnimalsBase candidate, int distance, AnimalsBase best,
        int bestDistance, AnimalsBase preferred)
    {
        if (best == null || distance != bestDistance) return distance < bestDistance;
        // 相同距离继续跟随原猎物，避免在两只猎物之间来回转向。
        if (candidate == preferred || best == preferred) return candidate == preferred;
        // 距离相同时优先小体型，再用坐标和物种稳定排序，不依赖投放顺序。
        if (candidate.BodySize != best.BodySize) return candidate.BodySize < best.BodySize;
        if (candidate.Coordinates != best.Coordinates)
            return IsCloser(candidate.CurrentCell, distance, best.CurrentCell, bestDistance);
        return (int)candidate.Species < (int)best.Species;
    }

    private readonly struct Route
    {
        public Route(ICellEnv previous, int distance) { Previous = previous; Distance = distance; }
        public ICellEnv Previous { get; }
        public int Distance { get; }
    }

    /// <summary>按实际可通行格寻路，防止隔着主体植物或地图缺口捕食、搬运。</summary>
    private Dictionary<ICellEnv, Route> FindRoutes(IAnimalWorld world)
    {
        var routes = new Dictionary<ICellEnv, Route> { { CurrentCell, new Route(null, 0) } };
        var queue = new Queue<ICellEnv>();
        queue.Enqueue(CurrentCell);
        while (queue.Count > 0)
        {
            ICellEnv current = queue.Dequeue();
            int distance = routes[current].Distance + 1;
            if (distance > PerceptionForTonight)
                continue;
            foreach (ICellEnv neighbor in current.Neighbors)
            {
                if (neighbor == null || routes.ContainsKey(neighbor) || !world.CanEnter(this, neighbor))
                    continue;
                routes.Add(neighbor, new Route(current, distance));
                queue.Enqueue(neighbor);
            }
        }
        return routes;
    }

    private bool Travel(AnimalSettlePlan plan, ICellEnv target, Dictionary<ICellEnv, Route> routes)
    {
        ICellEnv destination = target;
        while (routes[destination].Distance > MaxStepsForTonight)
            destination = routes[destination].Previous;
        plan.MoveTarget = destination == CurrentCell ? null : destination;
        return destination == target;
    }

    private static bool IsCloser(ICellEnv candidate, int distance, ICellEnv best, int bestDistance)
    {
        if (best == null || distance != bestDistance)
            return distance < bestDistance;
        if (candidate.Coordinates.y != best.Coordinates.y)
            return candidate.Coordinates.y < best.Coordinates.y;
        return candidate.Coordinates.x < best.Coordinates.x;
    }
}
