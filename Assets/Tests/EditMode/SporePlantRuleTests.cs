using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 逐条核对策划案里孢子植物的数值规则。
/// 参考《七日生态箱系统策划案》第四章：每晚最多增长 2 点、晴天最多减少 2 点。
/// </summary>
public class SporePlantRuleTests
{
    private static Moss PlaceMoss(FakeCell cell)
    {
        var moss = new Moss();
        moss.PlaceAt(cell.Coordinates);
        cell.TryAddCover(moss);
        return moss;
    }

    [Test]
    public void 雨天苔藓增长两点()
    {
        var cell = new FakeCell(Vector2Int.zero, true);
        Moss moss = PlaceMoss(cell);

        int before = moss.Vitality;
        moss.PlantSettle(1, FakeWeather.Rain(), cell);

        Assert.AreEqual(before + 2, moss.Vitality, "策划案：苔藓雨天活力+2");
    }

    [Test]
    public void 晴天苔藓只减一点()
    {
        var cell = new FakeCell(Vector2Int.zero, true);
        Moss moss = PlaceMoss(cell);

        int before = moss.Vitality;
        moss.PlantSettle(1, FakeWeather.Sun(1), cell);

        Assert.AreEqual(before - 1, moss.Vitality, "策划案：苔藓晴天活力-1");
    }

    [Test]
    public void 连续晴天加速衰退但不超过上限()
    {
        var cell = new FakeCell(Vector2Int.zero, true);
        Moss moss = PlaceMoss(cell);

        int before = moss.Vitality;
        moss.PlantSettle(1, FakeWeather.Sun(2), cell);

        Assert.AreEqual(before - 2, moss.Vitality, "连续晴天加速到 -2，仍不超过策划案上限");
    }

    [Test]
    public void 干燥格的苔藓雨天不增长()
    {
        var cell = new FakeCell(Vector2Int.zero, false);
        Moss moss = PlaceMoss(cell);

        int before = moss.Vitality;
        moss.PlantSettle(1, FakeWeather.Rain(), cell);

        Assert.AreEqual(before, moss.Vitality, "既不湿润也不是石面木面时，雨天不满足最低条件，不增长");
    }

    [Test]
    public void 干燥格的苔藓晴天仍会衰退()
    {
        var cell = new FakeCell(Vector2Int.zero, false);
        Moss moss = PlaceMoss(cell);

        // 最低生长条件只约束"能不能长"，不约束"会不会枯"。
        // 策划案要求晴天把地表晒干并让孢子植物衰减，因此不满足最低条件的格子也要扣，
        // 否则植物会被冻结在原地：既不长，也不死，永远占着格子。
        int before = moss.Vitality;
        moss.PlantSettle(1, FakeWeather.Sun(1), cell);

        Assert.AreEqual(before - 1, moss.Vitality, "不满足最低条件也不免于晴天衰减");
    }

    [Test]
    public void 石面上的苔藓仍按晴天规则衰减()
    {
        var cell = new FakeCell(Vector2Int.zero, false, 0, E_SurfaceKind.Stone);
        Moss moss = PlaceMoss(cell);

        int before = moss.Vitality;
        moss.PlantSettle(1, FakeWeather.Sun(1), cell);

        // 策划案把"石面或木面"列为最低生长条件，满足条件就可以在晴天存活，
        // 但最低条件不等于免于衰减：晴天该衰减的仍然衰减。
        Assert.AreEqual(before - 1, moss.Vitality, "满足最低条件后按晴天规则-1");
    }

    [Test]
    public void 藻类雨天有余屑时消耗余屑产光粒()
    {
        var cell = new FakeCell(Vector2Int.zero, true, 3);
        var algae = new Algae();
        algae.PlaceAt(cell.Coordinates);
        cell.TrySetSubject(algae);

        // 先养到成熟，光粒产出要求活力达标。初始活力 3，两个雨天各 +2。
        for (int day = 1; day <= 2; day++)
            algae.PlantSettle(day, FakeWeather.Rain(day), cell);

        Assert.AreEqual(7, algae.Vitality, "每晚严格 +2，不多不少");

        int detritusBefore = cell.Detritus;
        algae.PlantSettle(3, FakeWeather.Rain(3), cell);

        Assert.AreEqual(detritusBefore - 1, cell.Detritus, "策划案：有余屑时消耗 1 份");
    }

    [Test]
    public void 藻类没有余屑时不消耗()
    {
        var cell = new FakeCell(Vector2Int.zero, true, 0);
        var algae = new Algae();
        algae.PlaceAt(cell.Coordinates);
        cell.TrySetSubject(algae);

        for (int day = 1; day <= 3; day++)
            algae.PlantSettle(day, FakeWeather.Rain(day), cell);

        Assert.AreEqual(0, cell.Detritus, "没有余屑就不该被扣除");
    }

    [Test]
    public void 蘑菇晴天无余屑时额外衰减()
    {
        var cell = new FakeCell(Vector2Int.zero, true, 1, E_SurfaceKind.Ground, true);
        var mushroom = new Mushroom();
        mushroom.PlaceAt(cell.Coordinates);
        cell.TrySetSubject(mushroom);

        // 先靠余屑在雨天长大一点，否则活力会被钳在 0，"额外衰减"就观察不出来。
        mushroom.PlantSettle(1, FakeWeather.Rain(1), cell);
        int before = mushroom.Vitality;
        Assert.Greater(before, 3, "需要足够的活力才能看出 -3 的效果");

        cell.Detritus = 0;
        mushroom.PlantSettle(2, FakeWeather.Sun(1), cell);

        // 策划案：晴天-2，无余屑额外-1，共 -3。
        // 蘑菇放宽了自己的每晚上限到 3，这条规则才会真的被玩家看见；
        // 若沿用统一上限 2，-3 会被截成 -2，"饿了更容易死"就完全消失了。
        Assert.AreEqual(before - 3, mushroom.Vitality, "策划案：无余屑时加速衰退，且必须看得出差别");
    }

    [Test]
    public void 蘑菇可以长在有苔藓的格子里()
    {
        var cell = new FakeCell(Vector2Int.zero, true, 2, E_SurfaceKind.Ground, true);
        var moss = new Moss();
        var mushroom = new Mushroom();
        PlantSettler.Place(moss, cell);

        // 策划案：苔藓是地表覆盖，可与主体植物共存；蘑菇是子实体，占主体位。
        Assert.IsTrue(PlantSettler.Place(mushroom, cell));
        Assert.AreSame(mushroom, cell.Subject);
        Assert.AreSame(moss, cell.Cover[0]);
    }

    [Test]
    public void 初始活力物种可覆写以表达成熟与幼体()
    {
        // 策划案第八章：雨眠苔投放即为成熟，小蘑菇为幼体。
        Assert.Greater(new Moss().Vitality, new Mushroom().Vitality,
            "成熟的苔藓初始活力应高于幼体蘑菇");
    }

    [Test]
    public void 蘑菇晴天有余屑时只减两点()
    {
        var cell = new FakeCell(Vector2Int.zero, true, 5, E_SurfaceKind.Ground, true);
        var mushroom = new Mushroom();
        mushroom.PlaceAt(cell.Coordinates);
        cell.TrySetSubject(mushroom);

        // 先用雨天养大，避免活力被钳在 0 让断言失去意义。
        mushroom.PlantSettle(1, FakeWeather.Rain(1), cell);
        int before = mushroom.Vitality;
        mushroom.PlantSettle(2, FakeWeather.Sun(1), cell);

        Assert.AreEqual(before - 2, mushroom.Vitality, "策划案：蘑菇晴天-2");
    }

    [Test]
    public void 蘑菇雨天无余屑时只衰减不增长()
    {
        var cell = new FakeCell(Vector2Int.zero, true, 0, E_SurfaceKind.Ground, true);
        var mushroom = new Mushroom();
        mushroom.PlaceAt(cell.Coordinates);
        cell.TrySetSubject(mushroom);

        int before = mushroom.Vitality;
        mushroom.PlantSettle(1, FakeWeather.Rain(1), cell);

        Assert.AreEqual(before, mushroom.Vitality, "策划案把余屑列为蘑菇的最低条件");
    }

    [Test]
    public void 被取食会扣活力但不推进结算天数()
    {
        var cell = new FakeCell(Vector2Int.zero, true);
        Moss moss = PlaceMoss(cell);
        moss.PlantSettle(1, FakeWeather.Rain(), cell);

        int before = moss.Vitality;
        Assert.IsTrue(moss.TakeBite(1));

        Assert.AreEqual(before - 1, moss.Vitality, "被取食应扣一点活力");
        Assert.AreEqual(1, moss.LastSettledDay, "取食不属于过夜结算，不推进天数");
    }

    [Test]
    public void 被取食到零时进入枯萎()
    {
        var cell = new FakeCell(Vector2Int.zero, true);
        Moss moss = PlaceMoss(cell);

        while (moss.Vitality > 0)
            moss.TakeBite(1);

        Assert.IsTrue(moss.IsWithering, "活力归零应进入枯萎而不是立即消失");
        Assert.IsFalse(moss.IsDead, "枯萎当晚不回收");
    }

    [Test]
    public void 枯萎的植物下一晚回收成余屑()
    {
        var cell = new FakeCell(Vector2Int.zero, true);
        Moss moss = PlaceMoss(cell);

        while (moss.Vitality > 0)
            moss.TakeBite(1);
        Assert.IsTrue(moss.IsWithering);

        int detritusBefore = cell.Detritus;
        moss.PlantSettle(1, FakeWeather.Rain(), cell);

        Assert.IsTrue(moss.IsDead, "下一晚应该被回收");
        Assert.AreEqual(detritusBefore + 1, cell.Detritus, "策划案：苔藓回收产出 1 份余屑");
    }

    [Test]
    public void 蘑菇回收产出的余屑多于其他孢子植物()
    {
        var mossCell = new FakeCell(Vector2Int.zero, true);
        Moss moss = PlaceMoss(mossCell);
        while (moss.Vitality > 0)
            moss.TakeBite(1);
        moss.PlantSettle(1, FakeWeather.Rain(), mossCell);

        var mushroomCell = new FakeCell(Vector2Int.right, true, 0, E_SurfaceKind.Ground, true);
        var mushroom = new Mushroom();
        mushroom.PlaceAt(mushroomCell.Coordinates);
        while (mushroom.Vitality > 0)
            mushroom.TakeBite(1);
        mushroom.PlantSettle(1, FakeWeather.Rain(), mushroomCell);

        Assert.Greater(mushroomCell.Detritus, mossCell.Detritus,
            "策划案：蘑菇是死亡回收的主要起点，余屑应多于苔藓");
    }
}