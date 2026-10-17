/// <summary>
/// 青蛙，MVP 的核心捕食者。
/// 策划案：两栖类，捕食小型无脊椎动物，雨天沿雨印移动；
/// 奶蛙加入后，青蛙也会感知高阶捕食者并寻找庇护。
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

    /// <summary>奶蛙接近时优先躲藏；有庇护时原地藏好，否则逃向更安全的位置。</summary>
    protected override bool TryEscape(AnimalSettlePlan plan, int day, IAnimalWorld world, ICellEnv cell)
    {
        if (!AnimalFoodWeb.AnyPredatorNear(world, this, 1))
            return false;
        ICellEnv hide = PickBest(world, cell, MaxStepsForTonight, candidate =>
        {
            if (candidate != cell && !world.CanEnter(this, candidate))
                return float.NegativeInfinity;
            float safety = CellHasShelter(candidate) ? 100f : 0f;
            int nearestThreat = int.MaxValue;
            foreach (ICellEnv otherCell in world.Cells)
                foreach (AnimalsBase other in world.AnimalsAt(otherCell))
                    if (!other.IsDead && AnimalFoodWeb.IsThreatenedBy(Species, other.Species))
                    {
                        int distance = world.StepDistance(candidate, otherCell);
                        if (distance >= 0)
                            nearestThreat = System.Math.Min(nearestThreat, distance);
                    }
            return safety + (nearestThreat == int.MaxValue ? 0 : nearestThreat)
                - world.StepDistance(cell, candidate) * 0.1f;
        });
        plan.Goal = E_AnimalGoal.Escape;
        plan.MoveTarget = hide != cell ? hide : null;
        plan.EntersShelter = CellHasShelter(hide);
        plan.RememberSafety = hide;
        plan.Action = E_AnimalAction.Hide;
        plan.Log(RulePrefix + ".FLEE", DisplayName + " 察觉奶蛙等捕食者，优先寻找安全位置。");
        return true;
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
