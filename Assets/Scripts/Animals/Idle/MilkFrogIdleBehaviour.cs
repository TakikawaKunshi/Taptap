/// <summary>奶蛙沿用自身追猎、搬种和寻找树下庇护的决策；无目标时巡游。</summary>
public sealed class MilkFrogIdleBehaviour : AnimalIdleBehaviour
{
    private AnimalsBase prey;
    public override float SpeedMultiplier => 1f;
    public override float RestMultiplier => 1.2f;
    public override void Reset(ICellEnv cell) => prey = null;

    public override ICellEnv ChooseStep(AnimalsBase animal, IAnimalWorld world, ICellEnv previous)
    {
        if (animal is MilkFrog milk && milk.TryGetIdleStep(world, prey, out ICellEnv step, out AnimalsBase selected))
        {
            prey = selected;
            return step;
        }
        prey = null;
        return Wander(animal, world, previous, cell => 0f);
    }
}
