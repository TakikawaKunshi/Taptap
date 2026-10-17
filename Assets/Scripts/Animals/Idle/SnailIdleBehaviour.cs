/// <summary>蜗牛：慢速、较长停留；在可食植物附近活动，找不到植物时偏好湿润地表。</summary>
public sealed class SnailIdleBehaviour : AnimalIdleBehaviour
{
    public override float SpeedMultiplier => 0.4f;
    public override float RestMultiplier => 1.8f;

    public override ICellEnv ChooseStep(AnimalsBase animal, IAnimalWorld world, ICellEnv previous)
    {
        if (TryEscape(animal, world, out ICellEnv escape)) return escape;
        ICellEnv food = FindStep(animal, world, 2, cell => HasPlant(cell,
            plant => plant.FoodAmount > 0 && (plant.Species == E_PlantSpecies.Algae
                || plant.Species == E_PlantSpecies.Moss || plant.Species == E_PlantSpecies.Mushroom)), out _);
        return food ?? Wander(animal, world, previous,
            cell => (cell.IsWet ? 3f : 0f) + (cell.IsShaded ? 1f : 0f));
    }
}
