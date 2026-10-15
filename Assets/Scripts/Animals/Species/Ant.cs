using System.Collections.Generic;

/// <summary>
/// 蚂蚁，MVP 的搬运者与传播者。
/// 策划案：搬运种子、取食花蜜和菌类，形成小型资源网络；被蜘蛛和青蛙捕食。
/// 它承担的"播种"是动物反作用于植物的主要方式之一：
/// 从成熟株取走种子，再送到有织材的空地上放下，由编排器真正种出一株新的种子植物。
/// </summary>
public sealed class Ant : AnimalsBase
{
    public Ant(int startingSatiety = 2) : base(startingSatiety)
    {
    }

    protected override E_AnimalSpecies SpeciesValue => E_AnimalSpecies.Ant;

    public override string DisplayName => "蚂蚁";

    public override string RulePrefix => "CARRY.ANT";

    public override E_ActivityTag Tags => E_ActivityTag.Diurnal | E_ActivityTag.Climbing;

    public override E_BodySize BodySize => E_BodySize.Small;

    protected override int RecycleAmount => 1;

    protected override bool CanEatPlant(E_PlantSpecies species)
    {
        // 策划案：花蜜、种子碎片、蘑菇孢子。MVP 里落到蘑菇与两种成熟种子植物上。
        return species == E_PlantSpecies.Mushroom
               || species == E_PlantSpecies.Angiosperm
               || species == E_PlantSpecies.Gymnosperm;
    }

    protected override bool EatsDetritus => true;

    protected override bool TryEscape(AnimalSettlePlan plan, int day, IAnimalWorld world, ICellEnv cell)
    {
        if (!AnimalFoodWeb.AnyPredatorNear(world, this, 1))
            return false;

        ICellEnv hide = PickBestElsewhere(world, cell, MaxStepsForTonight,
            candidate => (CellHasShelter(candidate) ? 3f : 0f)
                         - world.StepDistance(cell, candidate) * 0.3f);

        if (hide == null || hide == cell)
        {
            FailToAdvance(plan, "察觉捕食者却找不到掩体。");
            return true;
        }

        plan.Goal = E_AnimalGoal.Escape;
        plan.MoveTarget = hide;
        plan.EntersShelter = CellHasShelter(hide);
        plan.RememberSafety = hide;
        plan.Log(RulePrefix + ".FLEE", DisplayName + " 逃向 " + hide.Coordinates + "。");
        return true;
    }

    /// <summary>
    /// 第二层优先级：携带种子时先把它送到合法的空地。
    /// 策划案明确"已经携带……时，优先送到合法位置，不能同时再去捕食"，
    /// 因此这里一旦决定送货就直接返回，不再落到取食分支。
    /// </summary>
    protected override bool TryDeliver(AnimalSettlePlan plan, int day, IAnimalWorld world, ICellEnv cell)
    {
        if (Carried != E_ResourceKind.Seed)
            return false;

        // 合法送达点：整格全空、非要在这晚已被别的资源预留的位置。
        ICellEnv drop = PickBestElsewhere(world, cell, MaxStepsForTonight,
            candidate => (candidate.CanOccupy ? 4f : 0f)
                         + (CellHasShelter(candidate) ? -2f : 0f)
                         - world.StepDistance(cell, candidate) * 0.5f);

        if (drop == null || !drop.CanOccupy)
        {
            if (FailedNights >= 1)
            {
                // 策划案：连续两晚无法推进才允许换目标；此时就地放下，种子回到这一格。
                plan.Goal = E_AnimalGoal.Delivery;
                plan.Action = E_AnimalAction.Carry;
                plan.PutDownCarried = true;
                plan.ActionCell = cell;
                plan.ResetFailedNights = true;
                plan.Log(RulePrefix + ".DROP",
                    DisplayName + " 连续两晚找不到空地，把种子放在 " + cell.Coordinates + "。");
                return true;
            }

            FailToAdvance(plan, "带着种子却没有找到空地，保留目标。");
            return true;
        }

        plan.Goal = E_AnimalGoal.Delivery;
        plan.MoveTarget = drop;
        plan.Action = E_AnimalAction.Carry;
        plan.PutDownCarried = true;
        plan.ActionCell = drop;
        plan.ResetFailedNights = true;
        plan.Log(RulePrefix + ".SOW",
            DisplayName + " 把种子搬到 " + drop.Coordinates + " 放下。");
        return true;
    }

    protected override bool TryForage(AnimalSettlePlan plan, int day, IAnimalWorld world, ICellEnv cell)
    {
        // 种子碎片是本物种最重要的资源：先找地上有种子的成熟株。
        PlantsBase seedSource = FindSeedSource(world, plan, PerceptionForTonight);
        if (seedSource != null)
        {
            ICellEnv plantCell = FindCellOf(world, seedSource);
            plan.Goal = E_AnimalGoal.Food;
            plan.MoveTarget = plantCell;
            plan.PlantTarget = seedSource;
            plan.SeedSource = seedSource;
            plan.PickUpCarried = true;
            plan.CarryKind = E_ResourceKind.Seed;
            Feed(plan, E_AnimalAction.Graze, seedSource.DisplayName + " 的种子碎片");
            plan.RememberFood = plantCell;
            return true;
        }

        ICellEnv detritus = FindDetritus(world, PerceptionForTonight);
        if (detritus != null)
        {
            plan.Goal = E_AnimalGoal.Food;
            plan.MoveTarget = detritus;
            plan.DetritusDelta = -1;
            Feed(plan, E_AnimalAction.Scavenge, "一份余屑");
            plan.RememberFood = detritus;
            return true;
        }

        PlantsBase plant = FindEdiblePlant(world, PerceptionForTonight);
        if (plant != null)
        {
            ICellEnv plantCell = FindCellOf(world, plant);
            plan.Goal = E_AnimalGoal.Food;
            plan.MoveTarget = plantCell;
            plan.PlantTarget = plant;
            plan.BiteAmount = 1;
            Feed(plan, E_AnimalAction.Graze, plant.DisplayName);
            plan.RememberFood = plantCell;
            return true;
        }

        Starve(plan);
        return true;
    }

    protected override void TryRoutine(AnimalSettlePlan plan, int day, IAnimalWorld world, ICellEnv cell)
    {
        ICellEnv target = RememberedFood ?? PickBest(world, cell, MaxStepsForTonight,
            candidate => (candidate.Detritus > 0 ? 2f : 0f)
                         - world.StepDistance(cell, candidate) * 0.5f);

        if (target == null || target == cell)
        {
            Rest(plan, "在巢穴附近巡游。");
            return;
        }

        plan.Goal = E_AnimalGoal.None;
        plan.MoveTarget = target;
        plan.Log(RulePrefix + ".PATROL", DisplayName + " 巡游到 " + target.Coordinates + "。");
    }

    /// <summary>
    /// 找一株地上还有种子的成熟株。
    /// 这里只读不写：真正把种子从植株上取走由编排器在写回阶段执行，
    /// 否则"预览今晚会发生什么"会把种子提前消耗掉。
    /// </summary>
    private PlantsBase FindSeedSource(IAnimalWorld world, AnimalSettlePlan plan, int maxSteps)
    {
        PlantsBase best = null;
        int bestDistance = int.MaxValue;

        IReadOnlyList<ICellEnv> cells = world.Cells;
        for (int i = 0; i < cells.Count; i++)
        {
            ICellEnv candidateCell = cells[i];
            if (candidateCell == null)
                continue;

            SeedPlantsBase seedPlant = candidateCell.Subject as SeedPlantsBase;
            if (seedPlant == null || seedPlant.IsDead || !seedPlant.HasSeedOnGround)
                continue;

            int distance = world.StepDistance(CurrentCell, candidateCell);
            if (distance < 0 || distance > maxSteps || distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = seedPlant;
        }

        return best;
    }
}
