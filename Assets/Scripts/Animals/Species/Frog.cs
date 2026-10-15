/// <summary>
/// 青蛙，MVP 的核心捕食者。
/// 策划案：两栖类，捕食小型无脊椎动物，雨天沿雨印移动；
/// 在 MVP 里麻雀与奶蛙都不实例化，因此它是首版的顶级捕食者——
/// 这正是策划案"顶级捕食者不强行添加一个不合理天敌"的选择。
/// </summary>
public sealed class Frog : AnimalsBase
{
    public Frog(int startingSatiety = 2) : base(startingSatiety)
    {
    }

    protected override E_AnimalSpecies SpeciesValue => E_AnimalSpecies.Frog;

    public override string DisplayName => "青蛙";

    public override string RulePrefix => "PREY.FROG";

    public override E_ActivityTag Tags => E_ActivityTag.RainActive | E_ActivityTag.Aquatic;

    public override E_BodySize BodySize => E_BodySize.Medium;

    protected override int RecycleAmount => 2;

    protected override bool CanEatPlant(E_PlantSpecies species) => false;

    protected override bool EatsDetritus => false;

    /// <summary>青蛙在 MVP 中没有天敌，因此不需要逃跑；返回 false 让它继续按优先级往下走。</summary>
    protected override bool TryEscape(AnimalSettlePlan plan, int day, IAnimalWorld world, ICellEnv cell)
    {
        return false;
    }

    protected override bool TryForage(AnimalSettlePlan plan, int day, IAnimalWorld world, ICellEnv cell)
    {
        // 策划案：只有捕食者饥饿、目标可达、目标没有有效庇护时才提交捕食。
        int steps = PerceptionForTonight;
        AnimalsBase prey = FindPrey(world, steps);
        if (prey == null)
        {
            Starve(plan);
            return true;
        }

        plan.Goal = E_AnimalGoal.Food;
        plan.MoveTarget = prey.CurrentCell;
        plan.AnimalTarget = prey;
        plan.BiteAmount = 1;
        Feed(plan, E_AnimalAction.Hunt, prey.DisplayName);
        plan.RememberFood = prey.CurrentCell;
        return true;
    }

    protected override void TryRoutine(AnimalSettlePlan plan, int day, IAnimalWorld world, ICellEnv cell)
    {
        // 雨行动物：雨天偏好待在湿润的地表；不下雨时回到记忆里的最后一次进食点。
        ICellEnv target = null;
        if (world.Meets(E_PlantNeed.Moisture))
        {
            target = PickBest(world, cell, MaxStepsForTonight,
                candidate => (candidate.IsWet ? 2f : 0f)
                             - world.StepDistance(cell, candidate) * 0.5f
                             + (CellHasShelter(candidate) ? 0.5f : 0f));
        }
        else if (RememberedFood != null && !CellHasShelter(cell))
        {
            target = RememberedFood;
            plan.RememberSafety = RememberedFood;
        }

        if (target == null || target == cell)
        {
            Rest(plan, "水边的苔藓丛里停一晚。");
            return;
        }

        plan.Goal = E_AnimalGoal.Shelter;
        plan.MoveTarget = target;
        plan.Log(RulePrefix + ".ROAM", DisplayName + " 沿雨印移动到 " + target.Coordinates + "。");
    }
}
