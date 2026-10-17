/// <summary>蜘蛛：较长停留，平时只在栖点一格内巡游；饥饿时才离开栖点追猎。</summary>
public sealed class SpiderIdleBehaviour : AnimalIdleBehaviour
{
    private ICellEnv home;
    public override float SpeedMultiplier => 0.8f;
    public override float RestMultiplier => 3f;
    public override void Reset(ICellEnv cell) => home = cell;

    public override ICellEnv ChooseStep(AnimalsBase animal, IAnimalWorld world, ICellEnv previous)
    {
        if (home == null) home = animal.CurrentCell;
        if (TryEscape(animal, world, out ICellEnv escape)) return escape;
        ICellEnv prey = PreyStep(animal, world, 3);
        if (prey != null) return prey;
        if (world.StepDistance(home, animal.CurrentCell) > 1)
        {
            ICellEnv back = FindStep(animal, world, animal.PerceptionForTonight, cell => cell == home, out _);
            if (back != null) return back;
            home = animal.CurrentCell;
        }
        return Wander(animal, world, previous, cell => AnimalFoodWeb.HasShelter(cell) ? 1f : 0f,
            cell => world.StepDistance(home, cell) >= 0 && world.StepDistance(home, cell) <= 1);
    }
}
