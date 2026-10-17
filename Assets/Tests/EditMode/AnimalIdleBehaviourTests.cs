using NUnit.Framework;
using UnityEngine;

public class AnimalIdleBehaviourTests
{
    private Random.State randomState;

    [SetUp]
    public void SetUp() { randomState = Random.state; Random.InitState(191); }
    [TearDown]
    public void TearDown() => Random.state = randomState;

    private static FakeMap Map()
    {
        var map = new FakeMap();
        for (int y = -2; y <= 2; y++)
            for (int x = -2; x <= 2; x++) map.Add(new Vector2Int(x, y), false);
        foreach (FakeCell cell in map.Cells)
            cell.LinkNeighbors(map.Get(cell.Coordinates + Vector2Int.down), map.Get(cell.Coordinates + Vector2Int.right),
                map.Get(cell.Coordinates + Vector2Int.up), map.Get(cell.Coordinates + Vector2Int.left));
        return map;
    }

    [Test]
    public void FrogPrefersWetGroundButEscapesMilkFrogBeforeFollowingMoisture()
    {
        FakeMap map = Map();
        var frog = new Frog();
        AnimalSettler.Place(frog, map.Get(Vector2Int.zero));
        map.Get(Vector2Int.right).IsWet = true;
        var idle = new FrogIdleBehaviour();
        var world = AnimalSettler.BuildWorld(FakeWeather.Rain(), map.Cells, new[] { frog });
        Assert.AreEqual(Vector2Int.right, idle.ChooseStep(frog, world, null).Coordinates);
        var milk = new MilkFrog(0);
        AnimalSettler.Place(milk, map.Get(Vector2Int.right));
        PlantSettler.Place(new Moss(), map.Get(Vector2Int.left));
        world = AnimalSettler.BuildWorld(FakeWeather.Rain(), map.Cells, new AnimalsBase[] { milk, frog });
        Assert.AreEqual(Vector2Int.left, idle.ChooseStep(frog, world, null).Coordinates);
        Assert.AreEqual(2, frog.Satiety);
        Assert.AreEqual(0, frog.LastSettledDay);
    }

    [Test]
    public void EarthwormApproachesDetritusAndWaitsWithoutConsumingIt()
    {
        FakeMap map = Map();
        var worm = new Earthworm(0);
        AnimalSettler.Place(worm, map.Get(Vector2Int.zero));
        FakeCell food = map.Get(Vector2Int.right * 2);
        food.Detritus = 2;
        var world = AnimalSettler.BuildWorld(FakeWeather.Sun(), map.Cells, new[] { worm });
        var idle = new EarthwormIdleBehaviour();
        Assert.AreEqual(Vector2Int.right, idle.ChooseStep(worm, world, null).Coordinates);
        worm.MoveTo(food);
        Assert.AreSame(food, idle.ChooseStep(worm, world, null));
        Assert.AreEqual(2, food.Detritus);
        Assert.AreEqual(0, worm.Satiety);
    }

    [Test]
    public void SnailApproachesEdibleCoverInsteadOfDetritusAndDoesNotGrazeDuringIdle()
    {
        FakeMap map = Map();
        var snail = new Snail(0);
        AnimalSettler.Place(snail, map.Get(Vector2Int.zero));
        map.Get(Vector2Int.right).Detritus = 5;
        var moss = new Moss();
        PlantSettler.Place(moss, map.Get(Vector2Int.left));
        var world = AnimalSettler.BuildWorld(FakeWeather.Sun(), map.Cells, new[] { snail });
        var idle = new SnailIdleBehaviour();
        Assert.AreEqual(Vector2Int.left, idle.ChooseStep(snail, world, null).Coordinates);
        snail.MoveTo(map.Get(Vector2Int.left));
        Assert.AreSame(snail.CurrentCell, idle.ChooseStep(snail, world, null));
        Assert.AreEqual(5, moss.Vitality);
        Assert.AreEqual(0, snail.Satiety);
    }

    [Test]
    public void AntSeeksSeedsWhileHungryAndKeepsItsOwnDeliveryDestination()
    {
        FakeMap map = Map();
        var ant = new Ant(0);
        AnimalSettler.Place(ant, map.Get(Vector2Int.zero));
        var seed = new Gymnosperm();
        PlantSettler.Place(seed, map.Get(Vector2Int.right));
        var world = AnimalSettler.BuildWorld(FakeWeather.Sun(), map.Cells, new[] { ant });
        var idle = new AntIdleBehaviour();
        Assert.AreEqual(Vector2Int.right, idle.ChooseStep(ant, world, null).Coordinates);
        Assert.IsTrue(seed.HasTransportableSeed);
        Assert.AreEqual(E_ResourceKind.None, ant.Carried);
        ant.SetCarried(E_ResourceKind.Seed);
        ICellEnv destination = idle.ChooseStep(ant, world, null);
        Assert.IsTrue(destination.CanOccupy);
        Assert.AreNotSame(ant.CurrentCell, destination);
        ant.MoveTo(destination);
        Assert.AreSame(destination, idle.ChooseStep(ant, world, null));
        Assert.AreEqual(E_ResourceKind.Seed, ant.Carried);
        Assert.IsNull(destination.Subject);
    }

    [Test]
    public void SpidersPatrolIndependentlyAroundTheirOwnHomes()
    {
        FakeMap map = Map();
        var first = new Spider();
        var second = new Spider();
        ICellEnv firstHome = map.Get(Vector2Int.left);
        ICellEnv secondHome = map.Get(Vector2Int.right);
        AnimalSettler.Place(first, firstHome);
        AnimalSettler.Place(second, secondHome);
        AnimalIdleBehaviour a = AnimalIdleBehaviour.Create(first.Species);
        AnimalIdleBehaviour b = AnimalIdleBehaviour.Create(second.Species);
        a.Reset(firstHome);
        b.Reset(secondHome);
        var world = AnimalSettler.BuildWorld(FakeWeather.Sun(), map.Cells, new[] { first, second });
        ICellEnv previousA = null, previousB = null;
        bool moved = false;
        for (int i = 0; i < 30; i++)
        {
            ICellEnv nextA = a.ChooseStep(first, world, previousA);
            ICellEnv nextB = b.ChooseStep(second, world, previousB);
            previousA = first.CurrentCell;
            previousB = second.CurrentCell;
            first.MoveTo(nextA);
            second.MoveTo(nextB);
            moved |= nextA != firstHome;
            Assert.LessOrEqual(world.StepDistance(firstHome, nextA), 1);
            Assert.LessOrEqual(world.StepDistance(secondHome, nextB), 1);
        }
        Assert.IsTrue(moved);
    }

    [Test]
    public void HungryFrogRoutesAroundAnObstacleWithoutEatingDuringIdle()
    {
        FakeMap map = Map();
        var frog = new Frog(0);
        var worm = new Earthworm();
        AnimalSettler.Place(frog, map.Get(Vector2Int.zero));
        AnimalSettler.Place(worm, map.Get(Vector2Int.right * 2));
        PlantSettler.Place(new Mushroom(), map.Get(Vector2Int.right));
        var world = AnimalSettler.BuildWorld(FakeWeather.Rain(), map.Cells, new AnimalsBase[] { frog, worm });
        ICellEnv next = new FrogIdleBehaviour().ChooseStep(frog, world, null);
        Assert.AreEqual(0, next.Coordinates.x);
        Assert.AreEqual(1, Mathf.Abs(next.Coordinates.y));
        Assert.IsFalse(worm.IsDead);
        Assert.AreEqual(0, frog.Satiety);
    }

    [TestCase(E_AnimalSpecies.Frog)]
    [TestCase(E_AnimalSpecies.Earthworm)]
    [TestCase(E_AnimalSpecies.Snail)]
    [TestCase(E_AnimalSpecies.Ant)]
    [TestCase(E_AnimalSpecies.Spider)]
    [TestCase(E_AnimalSpecies.MilkFrog)]
    public void SpeciesDecisionsStayOnTraversableNeighborCellsAndDoNotSettle(E_AnimalSpecies species)
    {
        FakeMap map = Map();
        AnimalsBase animal = AnimalSettler.Create(species);
        AnimalSettler.Place(animal, map.Get(new Vector2Int(2, 2)));
        PlantSettler.Place(new Mushroom(), map.Get(new Vector2Int(1, 2)));
        var world = AnimalSettler.BuildWorld(FakeWeather.Sun(), map.Cells, new[] { animal });
        AnimalIdleBehaviour idle = AnimalIdleBehaviour.Create(species);
        idle.Reset(animal.CurrentCell);
        for (int i = 0; i < 20; i++)
        {
            ICellEnv next = idle.ChooseStep(animal, world, null);
            Assert.IsNotNull(next);
            Assert.LessOrEqual(world.StepDistance(animal.CurrentCell, next), 1);
            Assert.IsTrue(next == animal.CurrentCell || world.CanEnter(animal, next));
        }
        Assert.AreEqual(2, animal.Satiety);
        Assert.AreEqual(0, animal.Starvation);
        Assert.AreEqual(0, animal.LastSettledDay);
        Assert.AreEqual(E_ResourceKind.None, animal.Carried);
    }
}
