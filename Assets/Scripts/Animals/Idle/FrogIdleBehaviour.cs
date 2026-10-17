/// <summary>青蛙：间歇性巡游，偏好湿润格；饥饿时接近猎物，遇到奶蛙先躲避。</summary>
public sealed class FrogIdleBehaviour : AnimalIdleBehaviour
{
    public override float SpeedMultiplier => 1.25f;
    public override float RestMultiplier => 1.5f;

    public override ICellEnv ChooseStep(AnimalsBase animal, IAnimalWorld world, ICellEnv previous)
    {
        if (TryEscape(animal, world, out ICellEnv escape)) return escape;
        ICellEnv prey = PreyStep(animal, world, animal.PerceptionForTonight);
        return prey ?? Wander(animal, world, previous,
            cell => (cell.IsWet ? 3f : 0f) + (AnimalFoodWeb.HasShelter(cell) ? 1f : 0f));
    }
}
