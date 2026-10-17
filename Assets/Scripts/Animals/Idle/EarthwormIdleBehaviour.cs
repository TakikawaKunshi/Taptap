/// <summary>蚯蚓：缓慢贴地游走，接近余屑和菌类，偏好阴湿地表。</summary>
public sealed class EarthwormIdleBehaviour : AnimalIdleBehaviour
{
    public override float SpeedMultiplier => 0.6f;
    public override float RestMultiplier => 0.8f;

    public override ICellEnv ChooseStep(AnimalsBase animal, IAnimalWorld world, ICellEnv previous)
    {
        if (TryEscape(animal, world, out ICellEnv escape)) return escape;
        ICellEnv food = FindStep(animal, world, 2, cell => cell.Detritus > 0, out _);
        if (food == null && animal.Satiety == 0)
            food = FindStep(animal, world, 2, cell => HasPlant(cell,
                plant => plant.FoodAmount > 0 && (plant.Species == E_PlantSpecies.Mushroom
                    || plant.Species == E_PlantSpecies.Moss)), out _);
        return food ?? Wander(animal, world, previous,
            cell => (cell.IsWet ? 2f : 0f) + (cell.IsShaded ? 2f : 0f));
    }
}
