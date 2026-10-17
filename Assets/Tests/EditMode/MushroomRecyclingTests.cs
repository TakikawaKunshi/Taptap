using NUnit.Framework;
using UnityEngine;

public class MushroomRecyclingTests
{
    [Test]
    public void LethalWeatherImmediatelyRecyclesAndReleasesOnlyTheMushroom()
    {
        var cell = new FakeCell(Vector2Int.zero, true, 1);
        var mushroom = new Mushroom();
        var moss = new Moss();
        Assert.IsTrue(PlantSettler.Place(mushroom, cell));
        Assert.IsTrue(PlantSettler.Place(moss, cell));

        PlantSettlePlan plan = mushroom.Propose(1, FakeWeather.Sun(), cell);
        Assert.AreEqual(2, mushroom.Vitality, "预览不得提前回收或改变活力。");
        Assert.AreEqual(1, cell.Detritus);
        Assert.IsTrue(mushroom.Commit(1, cell, plan));

        Assert.AreEqual(0, mushroom.Vitality);
        Assert.IsTrue(mushroom.IsDead);
        Assert.IsFalse(mushroom.IsWithering);
        Assert.AreEqual(4, cell.Detritus);
        Assert.IsNull(cell.Subject);
        Assert.Contains(moss, (System.Collections.ICollection)cell.Cover);
        Assert.IsTrue(plan.Logs.Exists(entry => entry.RuleId == "SPORE.MUSHROOM.RECYCLE"));
        Assert.IsFalse(plan.Logs.Exists(entry => entry.RuleId.EndsWith(".WITHER")));

        Assert.IsFalse(mushroom.Commit(1, cell, plan));
        Assert.IsFalse(mushroom.PlantSettle(2, FakeWeather.Rain(), cell));
        Assert.IsFalse(mushroom.TakeBite(1));
        Assert.AreEqual(4, cell.Detritus, "重复结算和取食不能再次发放余屑。");
        Assert.IsTrue(PlantSettler.Place(new Mushroom(), cell), "原主体位应立即可用。");
    }

    [Test]
    public void GrazingBeforeFirstNightRecyclesOnceAndDetritusCanBeEaten()
    {
        var cell = new FakeCell(Vector2Int.zero, true);
        var mushroom = new Mushroom();
        Assert.IsTrue(PlantSettler.Place(mushroom, cell));
        Assert.IsTrue(mushroom.TakeBite(1));
        Assert.AreEqual(0, cell.Detritus);
        Assert.IsFalse(mushroom.IsDead);

        var snail = new Snail(0);
        AnimalSettler.Place(snail, cell);
        var animals = new AnimalSettler();
        AnimalSettleReport report = animals.Settle(1, FakeWeather.Rain(),
            new[] { cell }, new AnimalsBase[] { snail });
        Assert.AreEqual(1, report.GrazedCount);
        Assert.IsTrue(mushroom.IsDead);
        Assert.IsNull(cell.Subject);
        Assert.AreEqual(3, cell.Detritus);
        Assert.IsTrue(report.Logs.Exists(entry => entry.RuleId == "SPORE.MUSHROOM.RECYCLE"));

        var worm = new Earthworm(0);
        AnimalSettler.Place(worm, cell);
        report = animals.Settle(2, FakeWeather.Rain(), new[] { cell }, new AnimalsBase[] { worm });
        Assert.AreEqual(1, report.ScavengedCount);
        Assert.AreEqual(2, cell.Detritus);
        Assert.Greater(worm.Satiety, 0);
    }

    [Test]
    public void SettlementReportIncludesImmediateMushroomRecycling()
    {
        var cell = new FakeCell(Vector2Int.zero, true);
        var mushroom = new Mushroom();
        PlantSettler.Place(mushroom, cell);
        var settler = new PlantSettler();
        PlantSettleReport report = settler.Settle(1, FakeWeather.Sun(), new[] { cell });
        CollectionAssert.Contains(report.Recycled, mushroom);
        Assert.AreEqual(3, cell.Detritus);
        Assert.AreEqual(0, settler.Settle(2, FakeWeather.Sun(), new[] { cell }).Recycled.Count);
        Assert.AreEqual(3, cell.Detritus);
    }

    [Test]
    public void MossRecyclingAddsOneDetritusAndPreservesTheSubjectPlant()
    {
        var cell = new FakeCell(Vector2Int.zero, true, 2);
        var mushroom = new Mushroom();
        var moss = new Moss();
        PlantSettler.Place(mushroom, cell);
        PlantSettler.Place(moss, cell);
        moss.TakeBite(moss.Vitality);
        Assert.IsTrue(moss.IsDead);
        Assert.AreEqual(3, cell.Detritus);
        Assert.AreSame(mushroom, cell.Subject);
        Assert.IsFalse(mushroom.IsDead);
        Assert.AreEqual(2, mushroom.Vitality);
        CollectionAssert.DoesNotContain(cell.Cover, moss);
        Assert.IsTrue(PlantSettler.Place(new Moss(), cell), "枯萎苔藓立即释放覆盖位置。");
    }
}
