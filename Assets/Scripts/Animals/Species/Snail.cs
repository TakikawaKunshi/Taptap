/// <summary>
/// 蜗牛，MVP 的植物消耗者。
/// 策划案：取食藻类、苔藓和蘑菇，留下黏液路径；被青蛙捕食。
/// 它是"植物数量被动物压制"这一侧的验证点，和蚯蚓一起把静态资源变成移动资源。
/// </summary>
public sealed class Snail : AnimalsBase
{
    public Snail(int startingSatiety = 2) : base(startingSatiety)
    {
    }

    protected override E_AnimalSpecies SpeciesValue => E_AnimalSpecies.Snail;

    public override string DisplayName => "蜗牛";

    public override string RulePrefix => "GRAZE.SNAIL";

    public override E_ActivityTag Tags => E_ActivityTag.Aquatic;

    public override E_BodySize BodySize => E_BodySize.Small;

    protected override int RecycleAmount => 1;

    protected override bool CanEatPlant(E_PlantSpecies species)
    {
        // 策划案：藻类、苔藓、蘑菇。
        return species == E_PlantSpecies.Algae
               || species == E_PlantSpecies.Moss
               || species == E_PlantSpecies.Mushroom;
    }

    protected override bool EatsDetritus => false;

    protected override bool TryEscape(AnimalSettlePlan plan, int day, IAnimalWorld world, ICellEnv cell)
    {
        if (!AnimalFoodWeb.AnyPredatorNear(world, this, 1))
            return false;

        ICellEnv hide = PickBestElsewhere(world, cell, MaxStepsForTonight,
            candidate => (CellHasShelter(candidate) ? 3f : 0f)
                         - world.StepDistance(cell, candidate) * 0.4f);

        if (hide == null || hide == cell)
        {
            FailToAdvance(plan, "察觉捕食者，却没有够近的藏身之处。");
            return true;
        }

        plan.Goal = E_AnimalGoal.Escape;
        plan.MoveTarget = hide;
        plan.EntersShelter = CellHasShelter(hide);
        plan.RememberSafety = hide;
        plan.Log(RulePrefix + ".FLEE", DisplayName + " 缩进 " + hide.Coordinates + " 躲藏。");
        return true;
    }

    protected override bool TryForage(AnimalSettlePlan plan, int day, IAnimalWorld world, ICellEnv cell)
    {
        PlantsBase plant = FindEdiblePlant(world, PerceptionForTonight);
        if (plant == null)
        {
            Starve(plan);
            return true;
        }

        ICellEnv plantCell = FindCellOf(world, plant);
        plan.Goal = E_AnimalGoal.Food;
        plan.MoveTarget = plantCell;
        plan.PlantTarget = plant;
        plan.BiteAmount = 1;
        Feed(plan, E_AnimalAction.Graze, plant.DisplayName);
        plan.RememberFood = plantCell;
        return true;
    }

    protected override void TryRoutine(AnimalSettlePlan plan, int day, IAnimalWorld world, ICellEnv cell)
    {
        // 蜗牛行动缓慢，没有饥饿目标时只会挪到最近的湿润植物旁等待。
        ICellEnv target = PickBest(world, cell, 1,
            candidate => (candidate.IsWet ? 2f : 0f)
                         - world.StepDistance(cell, candidate) * 0.5f);

        if (target == null || target == cell)
        {
            Rest(plan, "停留在湿润的地表上。");
            return;
        }

        plan.Goal = E_AnimalGoal.Shelter;
        plan.MoveTarget = target;
        plan.Log(RulePrefix + ".CRAWL", DisplayName + " 缓慢爬到 " + target.Coordinates + "。");
    }
}
