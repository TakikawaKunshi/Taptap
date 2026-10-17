using NUnit.Framework;
using UnityEngine;

public class DetritusLifecycleTests
{
    [Test]
    public void InitialDetritusExpiresOnSecondNightEvenInAnEmptyCell()
    {
        var cell = new FakeCell(Vector2Int.zero, true, 2);
        var plants = new PlantSettler();
        plants.Settle(1, FakeWeather.Sun(), new[] { cell });
        Assert.AreEqual(2, cell.Detritus);
        var report = plants.Settle(2, FakeWeather.Sun(), new[] { cell });
        Assert.AreEqual(0, cell.Detritus);
        Assert.IsTrue(report.Logs.Exists(log => log.RuleId == "RESOURCE.DETRITUS.EXPIRE"));
        Assert.AreEqual(0, plants.Settle(2, FakeWeather.Sun(), new[] { cell }).Logs.Count);
    }

    [Test]
    public void NewDetritusDoesNotRenewOldBatchesAndConsumptionUsesOldestFirst()
    {
        var cell = new FakeCell(Vector2Int.zero, true, 3);
        cell.AdvanceDetritusDay(1);
        cell.Detritus += 2;
        cell.Detritus -= 1;
        Assert.AreEqual(2, cell.AdvanceDetritusDay(2), "只剩余的旧余屑到期。");
        Assert.AreEqual(2, cell.Detritus, "新增批次应完整保留。");
        cell.Detritus = cell.Detritus;
        Assert.AreEqual(0, cell.AdvanceDetritusDay(2));
        Assert.AreEqual(0, cell.AdvanceDetritusDay(1));
        Assert.AreEqual(2, cell.AdvanceDetritusDay(3));
        Assert.AreEqual(0, cell.Detritus);
    }

    [Test]
    public void SameNightPlantAndAnimalPassesDoNotAgeNewlyRecycledDetritus()
    {
        var cell = new FakeCell(Vector2Int.zero, true);
        PlantSettler.Place(new Mushroom(), cell);
        var plants = new PlantSettler();
        var animals = new AnimalSettler();
        plants.Settle(1, FakeWeather.Sun(), new[] { cell });
        Assert.AreEqual(3, cell.Detritus);
        animals.Settle(1, FakeWeather.Sun(), new[] { cell }, new AnimalsBase[0]);
        plants.Settle(1, FakeWeather.Sun(), new[] { cell });
        Assert.AreEqual(3, cell.Detritus);
        plants.Settle(2, FakeWeather.Sun(), new[] { cell });
        Assert.AreEqual(3, cell.Detritus);
        plants.Settle(3, FakeWeather.Sun(), new[] { cell });
        Assert.AreEqual(0, cell.Detritus);
    }

    [Test]
    public void AnimalDeathDetritusAlsoExpiresWhenOnlyAnimalSettlementRuns()
    {
        var cell = new FakeCell(Vector2Int.zero, true);
        var milk = new MilkFrog(0);
        var worm = new Earthworm(2);
        AnimalSettler.Place(milk, cell);
        AnimalSettler.Place(worm, cell);
        var settler = new AnimalSettler();
        var report = settler.Settle(1, FakeWeather.Sun(), new[] { cell }, new AnimalsBase[] { milk, worm });
        Assert.AreEqual(1, report.HuntedCount);
        Assert.AreEqual(worm.RecycleYield, cell.Detritus);
        settler.Settle(2, FakeWeather.Sun(), new[] { cell }, new AnimalsBase[0]);
        Assert.AreEqual(worm.RecycleYield, cell.Detritus);
        settler.Settle(3, FakeWeather.Sun(), new[] { cell }, new AnimalsBase[0]);
        Assert.AreEqual(0, cell.Detritus);
    }

    [Test]
    public void ExpiredDetritusCannotSupportGrowthOrFeedAnimals()
    {
        var cell = new FakeCell(Vector2Int.zero, true, 1);
        var mushroom = new Mushroom();
        PlantSettler.Place(mushroom, cell);
        var plants = new PlantSettler();
        plants.SettleCell(1, FakeWeather.Rain(), cell);
        Assert.AreEqual(4, mushroom.Vitality);
        plants.SettleCell(2, FakeWeather.Rain(), cell);
        Assert.AreEqual(5, mushroom.Vitality, "到期余屑不能提供额外增长。");
        var empty = new FakeCell(Vector2Int.right, true, 1);
        var worm = new Earthworm(0);
        AnimalSettler.Place(worm, empty);
        var report = new AnimalSettler().Settle(2, FakeWeather.Sun(), new[] { empty }, new[] { worm });
        Assert.AreEqual(0, report.ScavengedCount);
        Assert.AreEqual(0, worm.Satiety);
        Assert.AreEqual(1, worm.Starvation);
    }

    [Test]
    public void ClearingAndRefillingCannotResurrectOldBatches()
    {
        var stock = new DetritusLifecycle();
        stock.SetAmount(3);
        stock.AdvanceDay(1);
        stock.SetAmount(0);
        stock.SetAmount(1);
        Assert.AreEqual(0, stock.AdvanceDay(2));
        Assert.AreEqual(1, stock.Amount);
        Assert.AreEqual(1, stock.AdvanceDay(10), "跳过日期也应正确清理到期资源。");
        stock.SetAmount(-5);
        Assert.AreEqual(0, stock.Amount);
    }

    [Test]
    public void BatchAgesSurviveSerialization()
    {
        var stock = new DetritusLifecycle(4);
        stock.SetAmount(2);
        stock.AdvanceDay(5);
        stock.SetAmount(3);
        var restored = JsonUtility.FromJson<DetritusLifecycle>(JsonUtility.ToJson(stock));
        Assert.AreEqual(2, restored.AdvanceDay(6));
        Assert.AreEqual(1, restored.Amount);
        Assert.AreEqual(1, restored.AdvanceDay(7));
    }
}
