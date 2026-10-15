using NUnit.Framework;
using UnityEngine;

public class PlantsBaseTests
{
    private static Moss PlaceMoss(FakeCell cell, int vitality = 3)
    {
        var moss = new Moss();
        moss.PlaceAt(cell.Coordinates);
        cell.TryAddCover(moss);
        return moss;
    }

    [Test]
    public void 放置前不允许结算()
    {
        var moss = new Moss();
        var cell = new FakeCell(Vector2Int.zero, true);
        Assert.Throws<System.InvalidOperationException>(() => moss.PlantSettle(1, FakeWeather.Rain(), cell));
    }

    [Test]
    public void 重复放置同一株植物会抛异常()
    {
        var moss = new Moss();
        moss.PlaceAt(new Vector2Int(1, 2));
        Assert.Throws<System.InvalidOperationException>(() => moss.PlaceAt(new Vector2Int(3, 4)));
    }

    [Test]
    public void 同一天重复结算返回false且不改变数据()
    {
        var cell = new FakeCell(Vector2Int.zero, true);
        Moss moss = PlaceMoss(cell);

        Assert.IsTrue(moss.PlantSettle(1, FakeWeather.Rain(), cell), "第 1 天应结算成功");
        int afterFirst = moss.Vitality;

        Assert.IsFalse(moss.PlantSettle(1, FakeWeather.Sun(), cell), "同一天再次结算必须返回 false");
        Assert.AreEqual(afterFirst, moss.Vitality, "被拒绝的结算不能改变活力");
    }

    [Test]
    public void 过期天数不会回滚结算()
    {
        var cell = new FakeCell(Vector2Int.zero, true);
        Moss moss = PlaceMoss(cell);

        moss.PlantSettle(5, FakeWeather.Rain(), cell);
        Assert.IsFalse(moss.PlantSettle(3, FakeWeather.Rain(), cell), "更早的天数必须被拒绝");
        Assert.AreEqual(5, moss.LastSettledDay, "结算天数不能倒退");
    }

    [Test]
    public void 天数非法时抛异常()
    {
        var cell = new FakeCell(Vector2Int.zero, true);
        Moss moss = PlaceMoss(cell);
        Assert.Throws<System.ArgumentOutOfRangeException>(() => moss.PlantSettle(0, FakeWeather.Rain(), cell));
    }

    [Test]
    public void 天气或格子为空时抛异常()
    {
        var cell = new FakeCell(Vector2Int.zero, true);
        Moss moss = PlaceMoss(cell);

        Assert.Throws<System.ArgumentNullException>(() => moss.PlantSettle(1, null, cell));
        Assert.Throws<System.ArgumentNullException>(() => moss.PlantSettle(1, FakeWeather.Rain(), null));
    }

    [Test]
    public void 回收后的植物不再结算()
    {
        var cell = new FakeCell(Vector2Int.zero, true);
        Moss moss = PlaceMoss(cell);

        for (int day = 1; day <= 30 && !moss.IsDead; day++)
        {
            moss.PlantSettle(day, FakeWeather.Sun(day), cell);
            Assert.GreaterOrEqual(moss.Vitality, 0, "活力不应为负");
        }

        Assert.IsTrue(moss.IsDead, "连续晴天足够多时必须回收");
        Assert.IsFalse(moss.PlantSettle(40, FakeWeather.Sun(40), cell), "已回收的植物不能继续结算");
    }
}