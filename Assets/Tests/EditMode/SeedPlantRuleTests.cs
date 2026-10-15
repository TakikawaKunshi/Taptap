using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 核对策划案里种子植物的成长回合规则。
/// 参考《七日生态箱系统策划案》第四章：雨天给水合标记，晴天给光合标记，
/// 两个标记配对后消耗并产生 1 成长点；晴与雨交替比连续同天气更有效。
/// </summary>
public class SeedPlantRuleTests
{
    private static Gymnosperm PlacePine()
    {
        var pine = new Gymnosperm();
        pine.PlaceAt(new Vector2Int(0, 0));
        return pine;
    }

    [Test]
    public void 新植物处于种子阶段且没有成长点()
    {
        Gymnosperm pine = PlacePine();

        Assert.AreEqual(E_PlantStage.Seed, pine.Stage);
        Assert.AreEqual(0, pine.GrowthPoints);
        Assert.IsFalse(pine.HasHydration);
        Assert.IsFalse(pine.HasPhotosynthesis);
    }

    [Test]
    public void 单独一个雨天只得到水合标记()
    {
        Gymnosperm pine = PlacePine();
        pine.PlantSettle(1, FakeWeather.Rain(1), new FakeCell(Vector2Int.zero, true));

        Assert.IsTrue(pine.HasHydration, "雨天获得水合标记");
        Assert.IsFalse(pine.HasPhotosynthesis, "单独的雨天不完成生长");
        Assert.AreEqual(0, pine.GrowthPoints, "策划案：单独的雨天不完成生长");
    }

    [Test]
    public void 单独一个晴天只得到光合标记()
    {
        Gymnosperm pine = PlacePine();
        pine.PlantSettle(1, FakeWeather.Sun(1), new FakeCell(Vector2Int.zero, true));

        Assert.IsTrue(pine.HasPhotosynthesis, "晴天获得光合标记");
        Assert.AreEqual(0, pine.GrowthPoints);
    }

    [Test]
    public void 晴雨交替后配对产生成长点()
    {
        Gymnosperm pine = PlacePine();
        pine.PlantSettle(1, FakeWeather.Sun(1), new FakeCell(Vector2Int.zero, true));
        pine.PlantSettle(2, FakeWeather.Rain(1), new FakeCell(Vector2Int.zero, true));

        Assert.AreEqual(1, pine.GrowthPoints, "策划案：两个标记配对消耗并产生 1 成长点");
        Assert.AreEqual(E_PlantStage.Sprout, pine.Stage, "1 成长点对应幼芽");
    }

    [Test]
    public void 雨晴交替同样能配对()
    {
        Gymnosperm pine = PlacePine();
        pine.PlantSettle(1, FakeWeather.Rain(1), new FakeCell(Vector2Int.zero, true));
        pine.PlantSettle(2, FakeWeather.Sun(1), new FakeCell(Vector2Int.zero, true));

        Assert.AreEqual(1, pine.GrowthPoints, "交替顺序不影响配对");
    }

    [Test]
    public void 连续同天气不会配对()
    {
        Gymnosperm pine = PlacePine();
        pine.PlantSettle(1, FakeWeather.Sun(1), new FakeCell(Vector2Int.zero, true));
        pine.PlantSettle(2, FakeWeather.Sun(2), new FakeCell(Vector2Int.zero, true));
        pine.PlantSettle(3, FakeWeather.Sun(3), new FakeCell(Vector2Int.zero, true));

        Assert.AreEqual(0, pine.GrowthPoints, "策划案：交替比连续同天气更有效");
        Assert.IsTrue(pine.HasPhotosynthesis);
    }

    [Test]
    public void 交替三次可以到达成熟株并掉落种子()
    {
        Gymnosperm pine = PlacePine();
        var cell = new FakeCell(Vector2Int.zero, true);

        for (int day = 1; day <= 6; day++)
        {
            bool sunny = day % 2 == 1;
            pine.PlantSettle(day, sunny ? FakeWeather.Sun(1) : FakeWeather.Rain(1), cell);
        }

        Assert.AreEqual(3, pine.GrowthPoints, "三次交替产生 3 个成长点");
        Assert.AreEqual(E_PlantStage.Mature, pine.Stage, "策划案：3 成长点为成熟株");
        Assert.IsTrue(pine.HasCone, "成熟后长出硬壳球果");
        Assert.IsTrue(pine.HasDroppedSeed, "成熟后一次性掉落种子");
    }

    [Test]
    public void 种子只掉落一次()
    {
        Gymnosperm pine = PlacePine();
        var cell = new FakeCell(Vector2Int.zero, true);

        for (int day = 1; day <= 10; day++)
        {
            bool sunny = day % 2 == 1;
            pine.PlantSettle(day, sunny ? FakeWeather.Sun(1) : FakeWeather.Rain(1), cell);
        }

        Assert.IsTrue(pine.HasDroppedSeed);
        Assert.AreEqual(E_PlantStage.Mature, pine.Stage, "不再继续推进阶段");
    }

    [Test]
    public void 同一天重复结算不会重复累计成长点()
    {
        Gymnosperm pine = PlacePine();
        var cell = new FakeCell(Vector2Int.zero, true);

        pine.PlantSettle(1, FakeWeather.Sun(1), cell);
        Assert.IsFalse(pine.PlantSettle(1, FakeWeather.Rain(1), cell), "同一天必须被拒绝");
        Assert.AreEqual(0, pine.GrowthPoints, "被拒绝的结算不能产生成长点");
    }

    [Test]
    public void 疯长药水加两点成长点但不能超过成熟阶段()
    {
        Gymnosperm pine = PlacePine();
        var cell = new FakeCell(Vector2Int.zero, true);

        for (int day = 1; day <= 4; day++)
        {
            bool sunny = day % 2 == 1;
            pine.PlantSettle(day, sunny ? FakeWeather.Sun(1) : FakeWeather.Rain(1), cell);
        }

        int before = pine.GrowthPoints;
        Assert.IsTrue(pine.ApplyGrowthPotion(), "幼株可以使用疯长药水");
        Assert.AreEqual(before + 2, pine.GrowthPoints, "策划案：成长点+2");
        Assert.IsFalse(pine.ApplyGrowthPotion(), "策划案：不能对同一株重复使用");
    }

    [Test]
    public void 成熟株不能再使用疯长药水()
    {
        Gymnosperm pine = PlacePine();
        var cell = new FakeCell(Vector2Int.zero, true);

        for (int day = 1; day <= 6; day++)
        {
            bool sunny = day % 2 == 1;
            pine.PlantSettle(day, sunny ? FakeWeather.Sun(1) : FakeWeather.Rain(1), cell);
        }

        Assert.AreEqual(E_PlantStage.Mature, pine.Stage);
        Assert.IsFalse(pine.ApplyGrowthPotion(), "策划案：不能对成熟株使用疯长药水");
    }

    [Test]
    public void 用过药水后下一次交替进入巨型株()
    {
        Gymnosperm pine = PlacePine();
        var cell = new FakeCell(Vector2Int.zero, true);

        // 交替两次到幼株
        for (int day = 1; day <= 4; day++)
        {
            bool sunny = day % 2 == 1;
            pine.PlantSettle(day, sunny ? FakeWeather.Sun(1) : FakeWeather.Rain(1), cell);
        }
        Assert.AreEqual(E_PlantStage.Seedling, pine.Stage);

        Assert.IsTrue(pine.ApplyGrowthPotion());

        // 下一次有效交替
        pine.PlantSettle(5, FakeWeather.Sun(1), cell);
        pine.PlantSettle(6, FakeWeather.Rain(1), cell);

        Assert.AreEqual(E_PlantStage.Giant, pine.Stage, "策划案：药水后下一次有效交替进入巨型株");
    }

    [Test]
    public void 幼株及以上可以作为庇护()
    {
        Gymnosperm pine = PlacePine();

        Assert.IsFalse(pine.IsShelter, "种子阶段不构成庇护");

        pine.GrantGrowth(2);
        Assert.IsTrue(pine.IsShelter, "策划案：幼株开始形成小型庇护");
    }

    [Test]
    public void 向日铃花成熟后每两个雨天产生一份种子()
    {
        var flower = new Angiosperm();
        flower.PlaceAt(new Vector2Int(0, 0));
        var cell = new FakeCell(Vector2Int.zero, true);

        for (int day = 1; day <= 6; day++)
        {
            bool sunny = day % 2 == 1;
            flower.PlantSettle(day, sunny ? FakeWeather.Sun(1) : FakeWeather.Rain(1), cell);
        }

        Assert.AreEqual(E_PlantStage.Mature, flower.Stage);
        Assert.IsTrue(flower.IsFlowering, "策划案：交替完成后开花");
    }

    [Test]
    public void 种子植物不吃东西()
    {
        Gymnosperm pine = PlacePine();
        Assert.IsFalse(pine.TakeBite(1), "策划案：蜗牛取食的是苔藓，不是种子植物");
    }
}