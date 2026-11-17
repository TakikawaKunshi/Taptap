using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class MushroomVitalityCapTests
{
    [Test]
    public void ReachingCapClampsGrowthAndOnlyCommitCreatesFourNeighborDeposits()
    {
        FakeCell center = Cross(out FakeCell[] neighbors);
        center.Detritus = 1;
        var mushroom = new Mushroom { MaxVitality = 3 };
        PlantSettler.Place(mushroom, center);
        PlantSettlePlan plan = mushroom.Propose(1, FakeWeather.Rain(), center);
        Assert.AreEqual(1, plan.VitalityDelta, "原本增长 2 点，此时只剩 1 点空间。");
        Assert.AreEqual(2, mushroom.Vitality);
        Assert.AreEqual(4, plan.NeighborDetritusTargets.Count);
        foreach (FakeCell neighbor in neighbors) Assert.AreEqual(0, neighbor.Detritus);

        Assert.IsTrue(mushroom.Commit(1, center, plan));
        Assert.AreEqual(3, mushroom.Vitality);
        Assert.AreEqual(1, center.Detritus, "不会在蘑菇所在格额外生成。");
        foreach (FakeCell neighbor in neighbors) Assert.AreEqual(1, neighbor.Detritus);
        Assert.IsFalse(mushroom.Commit(1, center, plan));
        foreach (FakeCell neighbor in neighbors) Assert.AreEqual(1, neighbor.Detritus);
    }

    [Test]
    public void FullMushroomProducesEachRainNightWithoutGrowingOrDuplicatingSameDay()
    {
        FakeCell center = Cross(out FakeCell[] neighbors);
        var mushroom = new Mushroom();
        PlantSettler.Place(mushroom, center);
        var settler = new PlantSettler { MushroomMaxVitality = 2 };
        var cells = new List<ICellEnv>(neighbors) { center };
        PlantSettleReport report = settler.Settle(1, FakeWeather.Rain(), cells);
        Assert.AreEqual(2, mushroom.Vitality);
        Assert.AreEqual(4, report.Produced[E_ResourceKind.Detritus]);
        Assert.AreEqual(0, report.UnchangedCount);
        StringAssert.Contains("余屑x4", report.BuildSummary());
        Assert.AreEqual(0, settler.Settle(1, FakeWeather.Rain(), cells).SettledCount);
        foreach (FakeCell neighbor in neighbors) Assert.AreEqual(1, neighbor.Detritus);

        settler.Settle(2, FakeWeather.Rain(), cells);
        Assert.AreEqual(2, mushroom.Vitality);
        foreach (FakeCell neighbor in neighbors) Assert.AreEqual(2, neighbor.Detritus);
    }

    [Test]
    public void BorderIgnoresMissingDiagonalAndDuplicateNeighborsButAllowsOccupiedCells()
    {
        var center = new FakeCell(Vector2Int.zero, true);
        var right = new FakeCell(Vector2Int.right, true);
        var diagonal = new FakeCell(Vector2Int.one, true);
        PlantSettler.Place(new Gymnosperm(), right);
        center.Neighbors = new ICellEnv[] { null, right, right, diagonal, center };
        var mushroom = new Mushroom { MaxVitality = 2 };
        PlantSettler.Place(mushroom, center);
        mushroom.PlantSettle(1, FakeWeather.Rain(), center);
        Assert.AreEqual(1, right.Detritus);
        Assert.AreEqual(0, diagonal.Detritus);
        Assert.AreEqual(0, center.Detritus);
    }

    [Test]
    public void SunnyWeatherAndUnmetGrowthConditionsDoNotProduceNeighborDetritus()
    {
        FakeCell center = Cross(out FakeCell[] neighbors);
        center.IsWet = false;
        var mushroom = new Mushroom { MaxVitality = 2 };
        PlantSettler.Place(mushroom, center);
        mushroom.PlantSettle(1, FakeWeather.Rain(), center);
        Assert.AreEqual(2, mushroom.Vitality);
        foreach (FakeCell neighbor in neighbors) Assert.AreEqual(0, neighbor.Detritus);
        mushroom.PlantSettle(2, FakeWeather.Sun(), center);
        Assert.IsTrue(mushroom.IsDead);
        Assert.AreEqual(3, center.Detritus, "仍然正常死亡回收。");
        foreach (FakeCell neighbor in neighbors) Assert.AreEqual(0, neighbor.Detritus);
    }

    [Test]
    public void MatureSporeProductionAndItsCooldownRemainIndependentOfDetritus()
    {
        FakeCell center = Cross(out _);
        center.Detritus = 1;
        var mushroom = new Mushroom { MaxVitality = 5 };
        PlantSettler.Place(mushroom, center);
        mushroom.PlantSettle(1, FakeWeather.Rain(), center);
        PlantSettlePlan mature = mushroom.Propose(2, FakeWeather.Rain(), center);
        Assert.AreEqual(1, mature.ProduceAmount);
        Assert.AreEqual(E_ResourceKind.Spore, mature.ProduceKind);
        Assert.AreEqual(4, mature.NeighborDetritusTargets.Count);
        mushroom.Commit(2, center, mature);
        PlantSettlePlan cooldown = mushroom.Propose(3, FakeWeather.Rain(), center);
        Assert.AreEqual(0, cooldown.VitalityDelta);
        Assert.AreEqual(0, cooldown.ProduceAmount);
        Assert.AreEqual(4, cooldown.NeighborDetritusTargets.Count);
    }

    [Test]
    public void BelowCapAfterEatingResumesGrowthUntilCapIsReachedAgain()
    {
        FakeCell center = Cross(out FakeCell[] neighbors);
        var mushroom = new Mushroom { MaxVitality = 4 };
        PlantSettler.Place(mushroom, center);
        mushroom.PlantSettle(1, FakeWeather.Rain(), center);
        Assert.AreEqual(3, mushroom.Vitality);
        foreach (FakeCell neighbor in neighbors) Assert.AreEqual(0, neighbor.Detritus);
        mushroom.PlantSettle(2, FakeWeather.Rain(), center);
        Assert.AreEqual(4, mushroom.Vitality);
        mushroom.TakeBite(2);
        mushroom.PlantSettle(3, FakeWeather.Rain(), center);
        Assert.AreEqual(3, mushroom.Vitality);
        foreach (FakeCell neighbor in neighbors) Assert.AreEqual(1, neighbor.Detritus);
    }

    [Test]
    public void LocalSettlementDatesNewDetritusCorrectlyWithoutRenewingOlderBatches()
    {
        FakeCell center = Cross(out FakeCell[] neighbors);
        var mushroom = new Mushroom { MaxVitality = 2 };
        PlantSettler.Place(mushroom, center);
        foreach (FakeCell neighbor in neighbors) neighbor.Detritus = 2;
        mushroom.PlantSettle(8, FakeWeather.Rain(), center);
        foreach (FakeCell neighbor in neighbors)
        {
            Assert.AreEqual(1, neighbor.Detritus, "旧批次到期，新批次记为第 8 天。");
            neighbor.AdvanceDetritusDay(9);
            Assert.AreEqual(1, neighbor.Detritus);
            neighbor.AdvanceDetritusDay(10);
            Assert.AreEqual(0, neighbor.Detritus);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NewDepositsCannotBoostNeighborGrowthDuringTheSameNight(bool reverse)
    {
        var source = new FakeCell(Vector2Int.zero, true);
        var neighbor = new FakeCell(Vector2Int.right, true);
        source.Neighbors = new[] { neighbor };
        neighbor.Neighbors = new[] { source };
        var full = new Mushroom();
        var young = new Mushroom();
        PlantSettler.Place(full, source);
        PlantSettler.Place(young, neighbor);
        for (int day = 1; day <= 3; day++) full.PlantSettle(day, FakeWeather.Rain(), source);
        Assert.AreEqual(5, full.Vitality);
        var cells = reverse ? new[] { neighbor, source } : new[] { source, neighbor };
        new PlantSettler { MushroomMaxVitality = 6 }.Settle(4, FakeWeather.Rain(), cells);
        Assert.AreEqual(6, full.Vitality);
        Assert.AreEqual(3, young.Vitality, "邻格必须按本晚开始时没有余屑的状态增长 1 点。");
        Assert.AreEqual(1, neighbor.Detritus);
    }

    [Test]
    public void InspectorConfigurationAppliesToExistingAndNewMushroomsOnNextSettlement()
    {
        var host = new GameObject("Mushroom settings test");
        try
        {
            PlantSettlementBridge bridge = host.AddComponent<PlantSettlementBridge>();
            Set(bridge, "verboseLog", false);
            Set(bridge, "mushroomMaxVitality", 3);
            FakeCell center = Cross(out FakeCell[] neighbors);
            var mushroom = new Mushroom();
            PlantSettler.Place(mushroom, center);
            var cells = new List<ICellEnv>(neighbors) { center };
            bridge.SettleCells(1, new PlantWeatherCondition(E_WeatherType.Rain), cells);
            Assert.AreEqual(3, mushroom.Vitality);
            foreach (FakeCell neighbor in neighbors) Assert.AreEqual(1, neighbor.Detritus);

            Set(bridge, "mushroomMaxVitality", 8);
            var added = new Mushroom();
            PlantSettler.Place(added, neighbors[0]);
            bridge.SettleCells(2, new PlantWeatherCondition(E_WeatherType.Rain), cells);
            Assert.AreEqual(8, mushroom.MaxVitality);
            Assert.AreEqual(8, added.MaxVitality);
            Assert.AreEqual(4, mushroom.Vitality);

            Set(bridge, "mushroomMaxVitality", -1);
            bridge.SettleCells(3, new PlantWeatherCondition(E_WeatherType.Rain), cells);
            Assert.AreEqual(2, bridge.MushroomMaxVitality);
            Assert.AreEqual(4, mushroom.Vitality, "降低上限只停止增长，不扣除已有活力。");
        }
        finally
        {
            Object.DestroyImmediate(host);
        }
    }

    private static FakeCell Cross(out FakeCell[] neighbors)
    {
        var center = new FakeCell(Vector2Int.zero, true);
        neighbors = new[]
        {
            new FakeCell(Vector2Int.down, true), new FakeCell(Vector2Int.right, true),
            new FakeCell(Vector2Int.up, true), new FakeCell(Vector2Int.left, true)
        };
        center.Neighbors = neighbors;
        return center;
    }

    private static void Set(object target, string field, object value)
    {
        target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    }
}
