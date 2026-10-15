/// <summary>
/// 蜘蛛，MVP 的非天气型捕食者。
/// 策划案：结网捕食蚂蚁等小型动物，改变通路与风险区；被青蛙捕食。
/// 它不看天气脸色行动，因此即使连续晴天，食物网里依然会发生捕食事件——
/// 这正是策划案"天气只提供机会，不替动物做决定"的验证点。
/// </summary>
public sealed class Spider : AnimalsBase
{
    public Spider(int startingSatiety = 2) : base(startingSatiety)
    {
    }

    protected override E_AnimalSpecies SpeciesValue => E_AnimalSpecies.Spider;

    public override string DisplayName => "蜘蛛";

    public override string RulePrefix => "PRED.SPIDER";

    /// <summary>策划案把蜘蛛定义为非天气型捕食者，因此不带任何晴雨偏好标签。</summary>
    public override E_ActivityTag Tags => E_ActivityTag.None;

    public override E_BodySize BodySize => E_BodySize.Small;

    protected override int RecycleAmount => 1;

    protected override bool CanEatPlant(E_PlantSpecies species) => false;

    protected override bool EatsDetritus => false;

    protected override bool TryEscape(AnimalSettlePlan plan, int day, IAnimalWorld world, ICellEnv cell)
    {
        if (!AnimalFoodWeb.AnyPredatorNear(world, this, 1))
            return false;

        ICellEnv hide = PickBestElsewhere(world, cell, MaxStepsForTonight,
            candidate => (CellHasShelter(candidate) ? 3f : 0f)
                         - world.StepDistance(cell, candidate) * 0.3f);

        if (hide == null || hide == cell)
        {
            FailToAdvance(plan, "发现青蛙却无处可躲，只能留在网上。");
            return true;
        }

        plan.Goal = E_AnimalGoal.Escape;
        plan.MoveTarget = hide;
        plan.EntersShelter = CellHasShelter(hide);
        plan.RememberSafety = hide;
        plan.Log(RulePrefix + ".FLEE", DisplayName + " 逃离 " + hide.Coordinates + " 方向。");
        return true;
    }

    protected override bool TryForage(AnimalSettlePlan plan, int day, IAnimalWorld world, ICellEnv cell)
    {
        AnimalsBase prey = FindPrey(world, PerceptionForTonight);
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
        // 没有猎物时留在原地结网：结网不改变地图数据，只是让本晚自己处于"守网"状态。
        plan.Goal = E_AnimalGoal.None;
        plan.Action = E_AnimalAction.Weave;
        plan.EntersShelter = CellHasShelter(cell);
        plan.Log(RulePrefix + ".WEAVE", DisplayName + " 守在自己织好的网上。");
    }
}
