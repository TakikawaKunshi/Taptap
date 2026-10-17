using System;
using NUnit.Framework;
using UnityEngine;

public class MilkFrogRuleTests
{
    private static FakeMap Line(int min, int max)
    {
        var map = new FakeMap();
        for (int x = min; x <= max; x++)
            map.Add(new Vector2Int(x, 0));
        foreach (FakeCell cell in map.Cells)
            cell.LinkNeighbors(null, map.Get(cell.Coordinates + Vector2Int.right), null,
                map.Get(cell.Coordinates + Vector2Int.left));
        return map;
    }

    private static T Place<T>(T animal, FakeMap map, int x) where T : AnimalsBase
    {
        AnimalSettler.Place(animal, map.Get(new Vector2Int(x, 0)));
        return animal;
    }

    [Test]
    public void EquallyDistantPreyPrefersSmallerBodyRegardlessOfRegistrationOrder()
    {
        FakeMap map = Line(-1, 1);
        var milk = Place(new MilkFrog(0), map, 0);
        var frog = Place(new Frog(2), map, -1);
        var worm = Place(new Earthworm(2), map, 1);
        foreach (AnimalsBase[] animals in new[] {
            new AnimalsBase[] { milk, frog, worm }, new AnimalsBase[] { worm, frog, milk } })
        {
            var world = AnimalSettler.BuildWorld(FakeWeather.Sun(), map.Cells, animals);
            Assert.AreSame(worm, milk.Propose(1, world, milk.CurrentCell).AnimalTarget);
            Assert.IsTrue(milk.TryGetIdleStep(world, null, out ICellEnv next, out AnimalsBase prey));
            Assert.AreSame(worm, prey);
            Assert.AreSame(worm.CurrentCell, next);
        }
        Assert.AreEqual(0, milk.LastSettledDay);
        Assert.AreEqual(0, milk.Starvation);
        Assert.IsFalse(worm.IsDead);
    }

    [Test]
    public void ChaseKeepsEqualDistanceTargetButSwitchesWhenItBecomesSheltered()
    {
        FakeMap map = Line(0, 6);
        var milk = Place(new MilkFrog(0), map, 0);
        var frog = Place(new Frog(2), map, 6);
        new AnimalSettler().Settle(1, FakeWeather.Sun(), map.Cells, new AnimalsBase[] { milk, frog });
        var worm = Place(new Earthworm(2), map, 2);
        var world = AnimalSettler.BuildWorld(FakeWeather.Sun(), map.Cells, new AnimalsBase[] { worm, milk, frog });
        Assert.AreSame(frog, milk.Propose(2, world, milk.CurrentCell).AnimalTarget);
        PlantSettler.Place(new Moss(), frog.CurrentCell);
        Assert.AreSame(worm, milk.Propose(2, world, milk.CurrentCell).AnimalTarget);
    }

    [Test]
    public void IdleHuntFollowsTraversableDetourOneCellAtATimeAndWaitsForSettlement()
    {
        var map = new FakeMap();
        for (int y = 0; y <= 1; y++)
            for (int x = 0; x <= 2; x++) map.Add(new Vector2Int(x, y));
        foreach (FakeCell cell in map.Cells)
            cell.LinkNeighbors(map.Get(cell.Coordinates + Vector2Int.down),
                map.Get(cell.Coordinates + Vector2Int.right), map.Get(cell.Coordinates + Vector2Int.up),
                map.Get(cell.Coordinates + Vector2Int.left));
        PlantSettler.Place(new Mushroom(), map.Get(Vector2Int.right));
        var milk = Place(new MilkFrog(0), map, 0);
        var frog = Place(new Frog(2), map, 2);
        var world = AnimalSettler.BuildWorld(FakeWeather.Sun(), map.Cells, new AnimalsBase[] { milk, frog });
        foreach (Vector2Int expected in new[] { new Vector2Int(0, 1), new Vector2Int(1, 1),
            new Vector2Int(2, 1), new Vector2Int(2, 0) })
        {
            Assert.IsTrue(milk.TryGetIdleStep(world, null, out ICellEnv next, out AnimalsBase prey));
            Assert.AreSame(frog, prey);
            Assert.AreEqual(expected, next.Coordinates);
            milk.MoveTo(next);
        }
        Assert.IsTrue(milk.TryGetIdleStep(world, frog, out ICellEnv hold, out _));
        Assert.AreSame(milk.CurrentCell, hold);
        Assert.AreEqual(0, milk.Satiety);
        Assert.AreEqual(0, milk.Starvation);
        Assert.AreEqual(0, milk.LastSettledDay);
        Assert.IsFalse(frog.IsDead);
    }

    [Test]
    public void IdleHuntReplansForMovingDeadAndProtectedPrey()
    {
        FakeMap map = Line(-2, 2);
        var milk = Place(new MilkFrog(0), map, 0);
        var frog = Place(new Frog(2), map, 2);
        AnimalsBase[] animals = { milk, frog };
        var world = AnimalSettler.BuildWorld(FakeWeather.Sun(), map.Cells, animals);
        Assert.IsTrue(milk.TryGetIdleStep(world, null, out ICellEnv right, out _));
        Assert.AreEqual(Vector2Int.right, right.Coordinates);
        frog.MoveTo(map.Get(new Vector2Int(-2, 0)));
        Assert.IsFalse(milk.TryGetIdleStep(world, frog, out _, out _), "过期快照不应追向旧位置。");
        world = AnimalSettler.BuildWorld(FakeWeather.Sun(), map.Cells, animals);
        Assert.IsTrue(milk.TryGetIdleStep(world, frog, out ICellEnv left, out _));
        Assert.AreEqual(Vector2Int.left, left.Coordinates);
        var moss = new Moss();
        PlantSettler.Place(moss, frog.CurrentCell);
        Assert.IsFalse(milk.TryGetIdleStep(world, frog, out _, out _));
        frog.CurrentCell.Release(moss);
        frog.TakeBite(2);
        Assert.IsFalse(milk.TryGetIdleStep(world, frog, out _, out _));
    }

    [Test]
    public void IdleCarrierStopsOnEmptyDestinationAndSowsThereNextNight()
    {
        FakeMap map = Line(0, 2);
        var seed = new Gymnosperm();
        PlantSettler.Place(seed, map.Get(Vector2Int.zero));
        var milk = Place(new MilkFrog(2), map, 0);
        var settler = new AnimalSettler();
        var world = AnimalSettler.BuildWorld(FakeWeather.Sun(), map.Cells, new[] { milk });
        Assert.IsTrue(milk.TryGetIdleStep(world, null, out ICellEnv source, out _));
        Assert.AreSame(milk.CurrentCell, source);
        Assert.IsTrue(seed.HasTransportableSeed, "闲置靠近不提前拿走种子。");
        settler.Settle(1, FakeWeather.Sun(), map.Cells, new[] { milk });
        Assert.AreEqual(E_ResourceKind.Seed, milk.Carried);
        Assert.IsTrue(milk.TryGetIdleStep(world, null, out ICellEnv next, out _));
        Assert.AreEqual(Vector2Int.right, next.Coordinates, "不能把刚捡起的种子放回原地。");
        milk.MoveTo(next);
        for (int i = 0; i < 5; i++)
        {
            Assert.IsTrue(milk.TryGetIdleStep(world, null, out ICellEnv hold, out _));
            Assert.AreSame(next, hold, "到达可播种空地后不再来回换目标。");
        }
        Assert.AreEqual(1, milk.LastSettledDay);
        Assert.IsNull(next.Subject);
        var report = settler.Settle(2, FakeWeather.Sun(), map.Cells, new[] { milk });
        Assert.AreEqual(1, report.NewPlants.Count);
        Assert.AreSame(next.Subject, report.NewPlants[0]);
        Assert.AreEqual(E_ResourceKind.None, milk.Carried);
    }

    [Test]
    public void FactoryAndFoodWebEnableMilkFrogWithoutAddingAHigherPredator()
    {
        Assert.IsTrue(AnimalSettler.IsSupported(E_AnimalSpecies.MilkFrog));
        var animal = AnimalSettler.Create(E_AnimalSpecies.MilkFrog, 1);
        Assert.IsInstanceOf<MilkFrog>(animal);
        Assert.AreEqual(1, animal.Satiety);
        Assert.AreEqual(E_BodySize.Large, animal.BodySize);
        foreach (E_AnimalSpecies species in Enum.GetValues(typeof(E_AnimalSpecies)))
            Assert.IsFalse(AnimalFoodWeb.CanPrey(species, animal.Species));
        foreach (E_AnimalSpecies prey in new[] { E_AnimalSpecies.Frog, E_AnimalSpecies.Fish,
            E_AnimalSpecies.Sandworm, E_AnimalSpecies.Sparrow, E_AnimalSpecies.Earthworm,
            E_AnimalSpecies.Snail, E_AnimalSpecies.Spider })
            Assert.IsTrue(AnimalFoodWeb.CanPrey(animal.Species, prey));
        Assert.IsFalse(AnimalFoodWeb.CanPrey(animal.Species, E_AnimalSpecies.Ant));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void HungryMilkFrogHuntsInEitherWeatherAndDeadPreyCannotAct(bool rain)
    {
        FakeMap map = Line(0, 3);
        var milk = Place(new MilkFrog(0), map, 0);
        var frog = Place(new Frog(0), map, 2);
        var worm = Place(new Earthworm(2), map, 3);
        AnimalsBase[] animals = { worm, frog, milk };
        AnimalSettleReport report = new AnimalSettler().Settle(1,
            rain ? FakeWeather.Rain() : FakeWeather.Sun(), map.Cells, animals);
        Assert.IsTrue(frog.IsDead);
        Assert.IsFalse(worm.IsDead, "被奶蛙吃掉的青蛙不能继续执行捕食蚯蚓的旧计划。");
        Assert.AreEqual(1, report.HuntedCount);
        Assert.AreEqual(1, milk.Satiety);
        Assert.AreEqual(0, milk.Starvation);
        Assert.AreEqual(frog.RecycleYield, map.Get(new Vector2Int(2, 0)).Detritus);
    }

    [Test]
    public void FedMilkFrogDoesNotHunt()
    {
        FakeMap map = Line(0, 2);
        var milk = Place(new MilkFrog(3), map, 0);
        var frog = Place(new Frog(2), map, 2);
        var report = new AnimalSettler().Settle(1, FakeWeather.Sun(), map.Cells, new AnimalsBase[] { milk, frog });
        Assert.IsFalse(frog.IsDead);
        Assert.AreEqual(0, report.HuntedCount);
        Assert.AreEqual(2, milk.Satiety);
    }

    [Test]
    public void DistantPreyRequiresTravelBeforeItCanBeEaten()
    {
        FakeMap map = Line(0, 6);
        var milk = Place(new MilkFrog(0), map, 0);
        var frog = Place(new Frog(2), map, 6);
        var settler = new AnimalSettler();
        AnimalsBase[] animals = { milk, frog };
        int limit = milk.MaxStepsForTonight;
        var first = settler.Settle(1, FakeWeather.Sun(), map.Cells, animals);
        Assert.AreEqual(limit, milk.Coordinates.x);
        Assert.IsFalse(frog.IsDead);
        Assert.AreEqual(0, first.HuntedCount);
        Assert.AreEqual(1, milk.Starvation);
        Assert.AreEqual(0, milk.Satiety);
        Assert.AreEqual(1, settler.Settle(2, FakeWeather.Sun(), map.Cells, animals).HuntedCount);
    }

    [Test]
    public void ImpassablePlantBlocksHuntingPath()
    {
        FakeMap map = Line(0, 3);
        PlantSettler.Place(new Mushroom(), map.Get(Vector2Int.right));
        var milk = Place(new MilkFrog(0), map, 0);
        var frog = Place(new Frog(2), map, 3);
        var report = new AnimalSettler().Settle(1, FakeWeather.Sun(), map.Cells, new AnimalsBase[] { milk, frog });
        Assert.AreEqual(Vector2Int.zero, milk.Coordinates);
        Assert.IsFalse(frog.IsDead);
        Assert.AreEqual(0, report.HuntedCount);
    }

    [Test]
    public void FrogEscapesIntoMossAndMilkFrogCannotEatItRemotely()
    {
        FakeMap map = Line(0, 2);
        PlantSettler.Place(new Moss(), map.Get(Vector2Int.zero));
        var frog = Place(new Frog(2), map, 1);
        var milk = Place(new MilkFrog(0), map, 2);
        var report = new AnimalSettler().Settle(1, FakeWeather.Sun(), map.Cells, new AnimalsBase[] { milk, frog });
        Assert.AreEqual(Vector2Int.zero, frog.Coordinates);
        Assert.IsTrue(frog.IsSheltered);
        Assert.IsFalse(frog.IsDead);
        Assert.AreEqual(0, report.HuntedCount);
        Assert.AreEqual(0, milk.Satiety);
        Assert.AreEqual(1, milk.Starvation);
    }

    [Test]
    public void ThreeHungryNightsProduceFourDetritusOnlyOnce()
    {
        FakeMap map = Line(0, 0);
        var milk = Place(new MilkFrog(0), map, 0);
        var settler = new AnimalSettler();
        AnimalSettleReport report = null;
        for (int day = 1; day <= 3; day++)
            report = settler.Settle(day, FakeWeather.Sun(), map.Cells, new[] { milk });
        Assert.IsTrue(milk.IsDead);
        Assert.AreEqual(3, milk.Starvation);
        Assert.AreEqual(1, report.StarvedCount);
        Assert.AreEqual(4, milk.CurrentCell.Detritus);
        settler.Settle(3, FakeWeather.Sun(), map.Cells, new[] { milk });
        settler.Settle(4, FakeWeather.Sun(), map.Cells, new[] { milk });
        Assert.AreEqual(4, milk.CurrentCell.Detritus);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CarriesARealSeedAndPlantsSameSpeciesOnAFollowingNight(bool matureSource)
    {
        FakeMap map = Line(0, 2);
        var source = new Gymnosperm();
        PlantSettler.Place(source, map.Get(Vector2Int.right));
        if (matureSource) source.GrantGrowth(3);
        var milk = Place(new MilkFrog(3), map, 0);
        var settler = new AnimalSettler();
        var world = AnimalSettler.BuildWorld(FakeWeather.Rain(), map.Cells, new[] { milk });
        milk.Propose(1, world, milk.CurrentCell);
        Assert.IsTrue(source.HasTransportableSeed, "预览不能提前消耗种子。");
        var first = settler.Settle(1, FakeWeather.Rain(), map.Cells, new[] { milk });
        Assert.AreEqual(E_ResourceKind.Seed, milk.Carried);
        Assert.AreEqual(E_PlantSpecies.Gymnosperm, milk.CarriedSeedSpecies);
        Assert.AreEqual(0, first.NewPlants.Count);
        Assert.IsFalse(source.HasTransportableSeed);
        Assert.AreEqual(!matureSource, source.IsDead);
        Assert.AreEqual(0, milk.CurrentCell.Detritus);
        var second = settler.Settle(2, FakeWeather.Rain(), map.Cells, new[] { milk });
        Assert.AreEqual(1, second.NewPlants.Count);
        Assert.IsInstanceOf<Gymnosperm>(second.NewPlants[0]);
        Assert.AreEqual(E_PlantStage.Seed, ((SeedPlantsBase)second.NewPlants[0]).Stage);
        Assert.AreEqual(0, second.NewPlants[0].LastSettledDay);
        Assert.AreEqual(E_ResourceKind.None, milk.Carried);
        Assert.AreEqual(0, settler.Settle(2, FakeWeather.Rain(), map.Cells, new[] { milk }).NewPlants.Count);
    }

    [Test]
    public void AntAndMilkFrogCannotDuplicateTheSameSeed()
    {
        FakeMap map = Line(0, 1);
        var source = new Gymnosperm();
        PlantSettler.Place(source, map.Get(Vector2Int.right));
        source.GrantGrowth(3);
        var ant = Place(new Ant(0), map, 0);
        var milk = Place(new MilkFrog(3), map, 0);
        var report = new AnimalSettler().Settle(1, FakeWeather.Sun(), map.Cells, new AnimalsBase[] { milk, ant });
        Assert.AreEqual(1, report.Rejected);
        Assert.AreEqual(E_ResourceKind.Seed, ant.Carried);
        Assert.AreEqual(E_ResourceKind.None, milk.Carried);
        Assert.IsFalse(source.HasSeedOnGround);
    }

    [Test]
    public void FailedSowingKeepsSeedWhenTwoCarriersChooseSameEmptyCell()
    {
        FakeMap map = Line(-1, 1);
        foreach (int x in new[] { -1, 1 })
        {
            var source = new Gymnosperm();
            PlantSettler.Place(source, map.Get(new Vector2Int(x, 0)));
            source.GrantGrowth(3);
        }
        var left = Place(new MilkFrog(3), map, -1);
        var right = Place(new MilkFrog(3), map, 1);
        var settler = new AnimalSettler();
        AnimalsBase[] animals = { right, left };
        settler.Settle(1, FakeWeather.Sun(), map.Cells, animals);
        Assert.AreEqual(E_ResourceKind.Seed, left.Carried);
        Assert.AreEqual(E_ResourceKind.Seed, right.Carried);
        var report = settler.Settle(2, FakeWeather.Sun(), map.Cells, animals);
        Assert.AreEqual(1, report.NewPlants.Count);
        Assert.AreEqual(E_ResourceKind.None, left.Carried);
        Assert.AreEqual(E_ResourceKind.Seed, right.Carried);
        Assert.AreEqual(E_PlantSpecies.Gymnosperm, right.CarriedSeedSpecies);
    }

    [Test]
    public void BlockedCarrierStillStarvesInsteadOfLivingForever()
    {
        FakeMap map = Line(0, 1);
        PlantSettler.Place(new Mushroom(), map.Get(Vector2Int.zero));
        var source = new Gymnosperm();
        PlantSettler.Place(source, map.Get(Vector2Int.right));
        source.GrantGrowth(3);
        var milk = Place(new MilkFrog(1), map, 0);
        var settler = new AnimalSettler();
        for (int day = 1; day <= 3; day++)
            settler.Settle(day, FakeWeather.Sun(), map.Cells, new[] { milk });
        Assert.IsTrue(milk.IsDead);
        Assert.AreEqual(3, milk.Starvation);
        Assert.AreEqual(4, milk.CurrentCell.Detritus);
    }

    [Test]
    public void ContestedHuntCanCauseStarvationWithoutGrantingImaginaryFood()
    {
        FakeMap map = Line(0, 2);
        var first = Place(new MilkFrog(0), map, 0);
        var second = Place(new MilkFrog(0), map, 1);
        var settler = new AnimalSettler();
        settler.Settle(1, FakeWeather.Sun(), map.Cells, new[] { first, second });
        settler.Settle(2, FakeWeather.Sun(), map.Cells, new[] { first, second });
        var frog = Place(new Frog(2), map, 2);
        var report = settler.Settle(3, FakeWeather.Sun(), map.Cells, new AnimalsBase[] { first, second, frog });
        Assert.AreEqual(1, report.HuntedCount);
        Assert.AreEqual(1, report.StarvedCount);
        Assert.IsFalse(first.IsDead);
        Assert.IsTrue(second.IsDead);
        Assert.AreEqual(0, second.Satiety);
    }

    [Test]
    public void UsesOrdinaryTreeShelterButCannotPassGiantPlants()
    {
        FakeMap map = Line(0, 1);
        var tree = new Gymnosperm();
        PlantSettler.Place(tree, map.Get(Vector2Int.right));
        tree.GrantGrowth(3);
        tree.OnSeedTaken();
        var milk = Place(new MilkFrog(3), map, 0);
        new AnimalSettler().Settle(1, FakeWeather.Rain(), map.Cells, new[] { milk });
        Assert.AreEqual(Vector2Int.right, milk.Coordinates);
        Assert.IsTrue(milk.IsSheltered);

        var giant = new Gymnosperm();
        PlantSettler.Place(giant, map.Get(Vector2Int.zero));
        giant.ApplyGrowthPotion();
        giant.GrantGrowth(1);
        Assert.AreEqual(E_PlantStage.Giant, giant.Stage);
        var world = AnimalSettler.BuildWorld(FakeWeather.Sun(), map.Cells, new[] { milk });
        Assert.IsFalse(world.CanEnter(milk, map.Get(Vector2Int.zero)));
    }
}
