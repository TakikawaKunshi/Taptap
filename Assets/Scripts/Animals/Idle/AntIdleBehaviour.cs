/// <summary>蚂蚁：快速连续巡游；携种时靠近空地，饥饿时接近种子和余屑。</summary>
public sealed class AntIdleBehaviour : AnimalIdleBehaviour
{
    private ICellEnv delivery;
    public override float SpeedMultiplier => 1.45f;
    public override float RestMultiplier => 0.45f;
    public override void Reset(ICellEnv cell) => delivery = null;

    public override ICellEnv ChooseStep(AnimalsBase animal, IAnimalWorld world, ICellEnv previous)
    {
        if (TryEscape(animal, world, out ICellEnv escape)) return escape;
        if (animal.Carried == E_ResourceKind.Seed)
        {
            ICellEnv next = delivery != null && delivery.CanOccupy
                ? FindStep(animal, world, animal.PerceptionForTonight, cell => cell == delivery, out _) : null;
            if (next != null) return next;
            next = FindStep(animal, world, animal.PerceptionForTonight,
                cell => cell != animal.CurrentCell && cell.CanOccupy, out delivery);
            return next ?? animal.CurrentCell;
        }
        delivery = null;
        if (animal.Satiety == 0)
        {
            ICellEnv seed = FindStep(animal, world, animal.PerceptionForTonight,
                cell => cell.Subject is SeedPlantsBase tree && !tree.IsDead && tree.HasTransportableSeed, out _);
            if (seed != null) return seed;
            ICellEnv food = FindStep(animal, world, animal.PerceptionForTonight, cell => cell.Detritus > 0, out _);
            if (food != null) return food;
        }
        return Wander(animal, world, previous, cell => cell.Detritus > 0 ? 0.5f : 0f);
    }
}
