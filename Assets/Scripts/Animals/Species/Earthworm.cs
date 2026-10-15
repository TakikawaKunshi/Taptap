/// <summary>
/// 蚯蚓，MVP 的分解者。
/// 策划案：取食余屑和菌类，松土并提供炼化材料；
/// 被青蛙捕食，因此每晚先判断附近有没有捕食者。
/// 它同时是炼化疯长药水的必需材料，和食物网形成真正的取舍。
/// </summary>
public sealed class Earthworm : AnimalsBase
{
    public Earthworm(int startingSatiety = 2) : base(startingSatiety)
    {
    }

    protected override E_AnimalSpecies SpeciesValue => E_AnimalSpecies.Earthworm;

    public override string DisplayName => "蚯蚓";

    public override string RulePrefix => "DECOMP.WORM";

    public override E_ActivityTag Tags => E_ActivityTag.Burrowing;

    public override E_BodySize BodySize => E_BodySize.Small;

    protected override int RecycleAmount => 1;

    protected override bool CanEatPlant(E_PlantSpecies species)
    {
        // 策划案：余屑、蘑菇残体、枯萎苔藓。
        return species == E_PlantSpecies.Mushroom || species == E_PlantSpecies.Moss;
    }

    protected override bool EatsDetritus => true;

    protected override bool TryEscape(AnimalSettlePlan plan, int day, IAnimalWorld world, ICellEnv cell)
    {
        if (!AnimalFoodWeb.AnyPredatorNear(world, this, 1))
            return false;

        // 穴居动物逃进有遮蔽或有余屑的格子：地表覆盖能挡住捕食者的视线。
        ICellEnv hide = PickBestElsewhere(world, cell, MaxStepsForTonight,
            candidate => (CellHasShelter(candidate) ? 3f : 0f)
                         + (candidate.IsWet ? 1f : 0f)
                         - world.StepDistance(cell, candidate) * 0.5f);

        if (hide == null || hide == cell)
        {
            FailToAdvance(plan, "察觉捕食者，但附近没有可躲的地方。");
            return true;
        }

        plan.Goal = E_AnimalGoal.Escape;
        plan.MoveTarget = hide;
        plan.EntersShelter = CellHasShelter(hide);
        plan.RememberSafety = hide;
        plan.Log(RulePrefix + ".FLEE", DisplayName + " 察觉捕食者，钻向 " + hide.Coordinates + "。");
        return true;
    }

    protected override bool TryForage(AnimalSettlePlan plan, int day, IAnimalWorld world, ICellEnv cell)
    {
        int steps = PerceptionForTonight;

        // 余屑是最省力的来源，先找它；找不到再吃掉枯萎的植物。
        ICellEnv detritus = FindDetritus(world, steps);
        if (detritus != null)
        {
            plan.Goal = E_AnimalGoal.Food;
            plan.MoveTarget = detritus;
            plan.DetritusDelta = -1;
            Feed(plan, E_AnimalAction.Scavenge, "一份余屑");
            plan.RememberFood = detritus;
            return true;
        }

        PlantsBase plant = FindEdiblePlant(world, steps);
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
        plan.Goal = E_AnimalGoal.None;
        plan.Action = E_AnimalAction.Till;

        // 策划案：蚯蚓松土让种子植物获得额外成长点。
        // 松土作用在本格与其邻格里的种子植物上，具体选取由编排器按固定顺序裁决。
        plan.GrowthPointsToGrant = 1;
        plan.Log(RulePrefix + ".TILL", DisplayName + " 在本格松土，附近的种子植物获得额外成长点。");
    }

}
