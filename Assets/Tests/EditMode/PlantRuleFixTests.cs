using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 覆盖这一轮修改引入的行为：
/// 只读提案不污染状态、占位槽、药水必须等下一次有效交替、
/// 种子只结一次、回收后释放格子、以及 WeatherBase 到植物的天气映射。
/// </summary>
public class PlantRuleFixTests
{
    private static FakeMap Build3x3(bool isWet = true)
    {
        var map = new FakeMap();
        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
                map.Add(new Vector2Int(x, y), isWet);
        }

        LinkAll(map);
        return map;
    }

    private static void LinkAll(FakeMap map)
    {
        for (int y = -2; y <= 2; y++)
        {
            for (int x = -2; x <= 2; x++)
            {
                FakeCell cell = map.Get(new Vector2Int(x, y));
                if (cell == null)
                    continue;
                cell.LinkNeighbors(
                    map.Get(new Vector2Int(x, y - 1)),
                    map.Get(new Vector2Int(x + 1, y)),
                    map.Get(new Vector2Int(x, y + 1)),
                    map.Get(new Vector2Int(x - 1, y)));
            }
        }
    }

    private static Gymnosperm PlacePine(FakeCell cell)
    {
        var pine = new Gymnosperm();
        PlantSettler.Place(pine, cell);
        return pine;
    }

    private static void Alternate(Gymnosperm pine, FakeCell cell, int days)
    {
        for (int day = 1; day <= days; day++)
            pine.PlantSettle(day, day % 2 == 1 ? FakeWeather.Sun(1) : FakeWeather.Rain(1), cell);
    }

    // ============================================================
    // 只读提案必须是纯读的
    // ============================================================

    [Test]
    public void 提案是只读的只有提交后才改变植物状态()
    {
        FakeMap map = Build3x3();
        FakeCell cell = map.Get(Vector2Int.zero);
        Gymnosperm pine = PlacePine(cell);

        PlantSettlePlan plan = pine.Propose(1, FakeWeather.Rain(1), cell);
        Assert.IsTrue(plan.GainHydration, "提案应写上本晚要获得水合标记");
        Assert.IsFalse(pine.HasHydration, "只读提案不能把标记写进实例，否则预览会污染状态");

        Assert.IsTrue(pine.Commit(1, cell, plan));
        Assert.IsTrue(pine.HasHydration, "只有提交之后标记才真正生效");
    }

    [Test]
    public void 反复提案不会累积标记()
    {
        FakeMap map = Build3x3();
        FakeCell cell = map.Get(Vector2Int.zero);
        Gymnosperm pine = PlacePine(cell);

        for (int i = 0; i < 5; i++)
            pine.Propose(1, FakeWeather.Rain(1), cell);

        Assert.IsFalse(pine.HasHydration, "预览无论调用多少次都不应改变状态");
        Assert.AreEqual(0, pine.GrowthPoints);
    }

    [Test]
    public void 提案不为空准则把获得标记也算作变化()
    {
        var plan = new PlantSettlePlan();
        Assert.IsTrue(plan.IsEmpty);

        plan.GainHydration = true;
        Assert.IsFalse(plan.IsEmpty, "拿到标记也是本晚发生了事，不能算成无变化");
    }

    // ============================================================
    // 占位槽
    // ============================================================

    [Test]
    public void 蘑菇占主体位且不被视为地表覆盖()
    {
        FakeMap map = Build3x3();
        FakeCell cell = map.Get(Vector2Int.zero);

        var mushroom = new Mushroom();
        Assert.IsTrue(PlantSettler.Place(mushroom, cell));
        Assert.AreSame(mushroom, cell.Subject);
        Assert.AreEqual(0, cell.Cover.Count);
    }

    [Test]
    public void 同一格不能铺两层地表覆盖()
    {
        FakeMap map = Build3x3();
        FakeCell cell = map.Get(Vector2Int.zero);

        Assert.IsTrue(PlantSettler.Place(new Moss(), cell));
        Assert.IsFalse(PlantSettler.Place(new Algae(), cell), "同一格只容一层覆盖");
    }

    // ============================================================
    // 疯长药水
    // ============================================================

    [Test]
    public void 药水之后连续同天气不会提前巨大化()
    {
        FakeMap map = Build3x3();
        FakeCell cell = map.Get(Vector2Int.zero);
        Gymnosperm pine = PlacePine(cell);

        Alternate(pine, cell, 4);
        Assert.AreEqual(E_PlantStage.Seedling, pine.Stage);
        Assert.IsTrue(pine.ApplyGrowthPotion());

        // 连续晴天没有完成任何一次有效交替，不该凭药水当天就巨大化。
        pine.PlantSettle(5, FakeWeather.Sun(2), cell);
        pine.PlantSettle(6, FakeWeather.Sun(3), cell);

        Assert.AreNotEqual(E_PlantStage.Giant, pine.Stage,
            "策划案：药水后要等下一次有效晴雨交替才进入巨型株");
    }

    [Test]
    public void 药水之后的下一次有效交替进入巨型株()
    {
        FakeMap map = Build3x3();
        FakeCell cell = map.Get(Vector2Int.zero);
        Gymnosperm pine = PlacePine(cell);

        Alternate(pine, cell, 4);
        Assert.IsTrue(pine.ApplyGrowthPotion());

        pine.PlantSettle(5, FakeWeather.Sun(1), cell);
        pine.PlantSettle(6, FakeWeather.Rain(1), cell);

        Assert.AreEqual(E_PlantStage.Giant, pine.Stage, "策划案：下一次有效交替后变巨型株");
        Assert.IsTrue(pine.HasCone, "巨型株同样成熟，应该长球果");
    }

    // ============================================================
    // 种子只结一次
    // ============================================================

    [Test]
    public void 种子被搬走后不会每晚再生一颗()
    {
        FakeMap map = Build3x3();
        FakeCell cell = map.Get(Vector2Int.zero);
        Gymnosperm pine = PlacePine(cell);

        Alternate(pine, cell, 6);
        Assert.AreEqual(E_PlantStage.Mature, pine.Stage);
        Assert.IsTrue(pine.HasSeedOnGround);

        pine.OnSeedTaken();
        Assert.IsFalse(pine.HasSeedOnGround, "地上那颗被搬走了");
        Assert.IsTrue(pine.HasDroppedSeed, "但成熟株只结一次这件事本身不能被抹掉");

        for (int day = 7; day <= 12; day++)
        {
            PlantSettlePlan later = pine.Propose(day, FakeWeather.Rain(1), cell);
            pine.Commit(day, cell, later);
            Assert.IsFalse(pine.HasSeedOnGround, "第 " + day + " 天不应再长出新的种子");
        }
    }

    [Test]
    public void 被蚯蚓催熟也会补上种子球果和花()
    {
        var pine = new Gymnosperm();
        pine.PlaceAt(Vector2Int.zero);
        pine.GrantGrowth(3);

        Assert.AreEqual(E_PlantStage.Mature, pine.Stage);
        Assert.IsTrue(pine.HasDroppedSeed, "GrantGrowth 不走结算，也要补上一次性掉落");
        Assert.IsTrue(pine.HasCone, "被催熟的裸子植物也要长球果");

        var flower = new Angiosperm();
        flower.PlaceAt(Vector2Int.zero);
        flower.GrantGrowth(3);

        Assert.IsTrue(flower.IsFlowering, "被催熟的被子植物也要开花，否则蜘蛛永远没地方结网");
    }

    // ============================================================
    // 回收与占位释放
    // ============================================================

    [Test]
    public void 回收之后格子被释放可以重新种植()
    {
        FakeMap map = Build3x3();
        FakeCell cell = map.Get(Vector2Int.zero);
        var mushroom = new Mushroom();
        PlantSettler.Place(mushroom, cell);

        while (mushroom.Vitality > 0)
            mushroom.TakeBite(1);

        new PlantSettler().Settle(1, FakeWeather.Rain(1), map.Cells);

        Assert.IsTrue(mushroom.IsDead);
        Assert.IsNull(cell.Subject, "回收后必须把位置还回去，否则后续永远种不进去");

        var fresh = new Mushroom();
        Assert.IsTrue(PlantSettler.Place(fresh, cell));
        Assert.AreSame(fresh, cell.Subject);
    }

    // ============================================================
    // 天气映射
    // ============================================================

    [Test]
    public void 晴天提供光照与热雨天提供潮湿与繁菌()
    {
        Assert.IsTrue(PlantWeatherCondition.MeetsNeed(E_WeatherType.Sun, E_PlantNeed.Light));
        Assert.IsTrue(PlantWeatherCondition.MeetsNeed(E_WeatherType.Sun, E_PlantNeed.Heat));
        Assert.IsFalse(PlantWeatherCondition.MeetsNeed(E_WeatherType.Sun, E_PlantNeed.Moisture));

        Assert.IsTrue(PlantWeatherCondition.MeetsNeed(E_WeatherType.Rain, E_PlantNeed.Moisture));
        Assert.IsTrue(PlantWeatherCondition.MeetsNeed(E_WeatherType.Rain, E_PlantNeed.Fungi));
        Assert.IsFalse(PlantWeatherCondition.MeetsNeed(E_WeatherType.Rain, E_PlantNeed.Light));
    }

    [Test]
    public void 天气条件可以直接驱动植物结算()
    {
        FakeMap map = Build3x3(isWet: true);
        FakeCell cell = map.Get(Vector2Int.zero);
        var moss = new Moss();
        PlantSettler.Place(moss, cell);

        int before = moss.Vitality;
        var settler = new PlantSettler();
        PlantSettleReport report = settler.Settle(1,
            new PlantWeatherCondition(E_WeatherType.Rain, 1, E_WeatherChange.None), map.Cells);

        Assert.AreEqual(before + 2, moss.Vitality, "雨天条件应让苔藓增长 2 点");
        Assert.AreEqual(0, report.Recycled.Count);
        Assert.Greater(report.Logs.Count, 0);
    }

    [Test]
    public void 连续天数会被截断到至少一天()
    {
        var condition = new PlantWeatherCondition(E_WeatherType.Sun, 0);
        Assert.AreEqual(1, condition.ConsecutiveSameDays, "第 1 天就是连续 1 天");
    }
}
