using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 逐条核对策划案第五章与第六章的动物规则：
/// 通用状态、四级决策优先级、食物网关系、饥饿与死亡闭环。
/// 全部走纯逻辑对象，不依赖场景与 MonoBehaviour，因此可以在 EditMode 下直接跑。
/// </summary>
public class AnimalRuleTests
{
    /// <summary>搭一张互相连通的小地图，动物寻路需要真实的四邻接。</summary>
    private static FakeMap BuildLinkedMap(int half = 2)
    {
        var map = new FakeMap();
        for (int y = -half; y <= half; y++)
        {
            for (int x = -half; x <= half; x++)
                map.Add(new Vector2Int(x, y), true);
        }

        for (int y = -half; y <= half; y++)
        {
            for (int x = -half; x <= half; x++)
            {
                FakeCell cell = map.Get(new Vector2Int(x, y));
                cell.LinkNeighbors(
                    map.Get(new Vector2Int(x, y - 1)),
                    map.Get(new Vector2Int(x + 1, y)),
                    map.Get(new Vector2Int(x, y + 1)),
                    map.Get(new Vector2Int(x - 1, y)));
            }
        }

        return map;
    }

    private static List<ICellEnv> AsCells(FakeMap map)
    {
        var cells = new List<ICellEnv>();
        foreach (FakeCell cell in map.Cells)
            cells.Add(cell);
        return cells;
    }

    private static T PlaceAnimal<T>(T animal, FakeCell cell) where T : AnimalsBase
    {
        AnimalSettler.Place(animal, cell);
        return animal;
    }

    // ---- 通用状态 ----

    [Test]
    public void 每晚饱腹自然下降一点()
    {
        FakeMap map = BuildLinkedMap();
        FakeCell cell = map.Get(Vector2Int.zero);
        Snail snail = PlaceAnimal(new Snail(3), cell);
        map.Get(new Vector2Int(0, 1)).Detritus = 0;

        var settler = new AnimalSettler();
        settler.Settle(1, FakeWeather.Sun(), AsCells(map), new AnimalsBase[] { snail });

        Assert.AreEqual(2, snail.Satiety, "策划案：每晚自然下降 1，且飽腹最高 3");
    }

    [Test]
    public void 成功进食后恢复两点饱腹并清零饥饿()
    {
        FakeMap map = BuildLinkedMap();
        FakeCell cell = map.Get(Vector2Int.zero);
        var moss = new Moss();
        moss.PlaceAt(cell.Coordinates);
        cell.TryAddCover(moss);

        Snail snail = PlaceAnimal(new Snail(0), cell);
        var settler = new AnimalSettler();
        settler.Settle(1, FakeWeather.Sun(), AsCells(map), new AnimalsBase[] { snail });

        // 饱腹 0 → 吃一次：先 -1 再 +2，结果 1～2 之间都会被钳到 [0,3]。
        Assert.AreEqual(1, snail.Satiety, "策划案：成功进食后恢复 2，最高 3");
        Assert.AreEqual(0, snail.Starvation, "吃到东西后饥饿计数清零");
    }

    [Test]
    public void 连续三晚饿不到粮食就死亡并转为余屑()
    {
        FakeMap map = BuildLinkedMap();
        FakeCell cell = map.Get(Vector2Int.zero);
        // 地图上没有苔藓也没有余屑，青蛙只能空手而归。
        Frog frog = PlaceAnimal(new Frog(0), cell);

        var settler = new AnimalSettler();
        int detritusBefore = cell.Detritus;

        // 第 1 晚：饱腹已是 0，饥饿升到 1。
        settler.Settle(1, FakeWeather.Sun(), AsCells(map), new AnimalsBase[] { frog });
        Assert.AreEqual(1, frog.Starvation, "第 1 晚没有食物，饥饿计数 1");
        Assert.IsFalse(frog.IsDead, "还不至于饿死");

        settler.Settle(2, FakeWeather.Sun(), AsCells(map), new AnimalsBase[] { frog });
        Assert.AreEqual(2, frog.Starvation, "第 2 晚饥饿计数 2");

        settler.Settle(3, FakeWeather.Sun(), AsCells(map), new AnimalsBase[] { frog });
        Assert.IsTrue(frog.IsDead, "第 3 晚饥饿计数达到 3，死亡");
        Assert.AreEqual(detritusBefore + frog.RecycleYield, cell.Detritus,
            "策划案：死亡个体转为余屑，闭环回到蘑菇与蚯蚓");
    }

    [Test]
    public void 同一晚重复结算不会重复扣饱腹()
    {
        FakeMap map = BuildLinkedMap();
        FakeCell cell = map.Get(Vector2Int.zero);
        Spider spider = PlaceAnimal(new Spider(3), cell);

        var settler = new AnimalSettler();
        settler.Settle(1, FakeWeather.Sun(), AsCells(map), new AnimalsBase[] { spider });
        int afterFirst = spider.Satiety;

        settler.Settle(1, FakeWeather.Sun(), AsCells(map), new AnimalsBase[] { spider });
        Assert.AreEqual(afterFirst, spider.Satiety, "同一天重复结算必须被拦截");
    }

    // ---- 食物网 ----

    [Test]
    public void 青蛙捕食蚯蚓而不会被反向捕食()
    {
        Assert.IsTrue(AnimalFoodWeb.CanPrey(E_AnimalSpecies.Frog, E_AnimalSpecies.Earthworm),
            "策划案：青蛙捕食蚯蚓");
        Assert.IsFalse(AnimalFoodWeb.CanPrey(E_AnimalSpecies.Earthworm, E_AnimalSpecies.Frog),
            "食物网不是可逆的单线链");
    }

    [Test]
    public void 青蛙在首版没有天敌()
    {
        // 策划案：麻雀与奶蛙属于扩展，MVP 里不添加不合理的顶级捕食者。
        Assert.IsFalse(AnimalFoodWeb.IsThreatenedBy(E_AnimalSpecies.Frog, E_AnimalSpecies.Spider));
        Assert.IsFalse(AnimalFoodWeb.IsThreatenedBy(E_AnimalSpecies.Frog, E_AnimalSpecies.Ant));
        Assert.IsFalse(AnimalFoodWeb.IsThreatenedBy(E_AnimalSpecies.Frog, E_AnimalSpecies.Earthworm));
    }

    [Test]
    public void 青蛙在苔藓的遮蔽下捕不到猎物()
    {
        FakeMap map = BuildLinkedMap();
        FakeCell center = map.Get(Vector2Int.zero);
        FakeCell target = map.Get(new Vector2Int(1, 0));

        var frog = PlaceAnimal(new Frog(0), center);
        Earthworm worm = PlaceAnimal(new Earthworm(2), target);

        // 一株成熟苔藓给目标格提供庇护，捕食应当失败。
        var moss = new Moss();
        moss.PlaceAt(target.Coordinates);
        target.TryAddCover(moss);
        Assert.IsTrue(AnimalFoodWeb.HasShelter(target), "苔藓茂密时应提供庇护");

        var settler = new AnimalSettler();
        AnimalSettleReport report = settler.Settle(1, FakeWeather.Sun(), AsCells(map),
            new AnimalsBase[] { frog, worm });

        Assert.AreEqual(0, report.HuntedCount, "猎物有有效庇护时不允许捕食成功");
        Assert.IsFalse(worm.IsDead, "策划案：猎物可借助苔藓逃离");
    }

    [Test]
    public void 没有遮蔽时青蛙能吃到蚯蚓()
    {
        FakeMap map = BuildLinkedMap();
        FakeCell center = map.Get(Vector2Int.zero);
        FakeCell target = map.Get(new Vector2Int(1, 0));

        var frog = PlaceAnimal(new Frog(0), center);
        Earthworm worm = PlaceAnimal(new Earthworm(2), target);

        var settler = new AnimalSettler();
        AnimalSettleReport report = settler.Settle(1, FakeWeather.Sun(), AsCells(map),
            new AnimalsBase[] { frog, worm });

        Assert.AreEqual(1, report.HuntedCount, "没有遮蔽且距离可达时应成功捕食一次");
        Assert.IsTrue(worm.IsDead, "被捕食者应当死亡");
        Assert.Greater(frog.Satiety, 0, "捕食者饱腹应当恢复");
        Assert.AreEqual(worm.RecycleYield, target.Detritus, "死亡个体当场转为余屑");
    }

    [Test]
    public void 同一份资源每晚只允许一只动物预留()
    {
        FakeMap map = BuildLinkedMap();
        FakeCell center = map.Get(Vector2Int.zero);
        FakeCell target = map.Get(new Vector2Int(1, 0));
        target.Detritus = 1;

        // 两只蚯蚓抢同一份余屑：只能有一只吃到，另一只当晚空手而归。
        Earthworm first = PlaceAnimal(new Earthworm(0), center);
        Earthworm second = PlaceAnimal(new Earthworm(0), map.Get(new Vector2Int(-1, 0)));

        var settler = new AnimalSettler();
        AnimalSettleReport report = settler.Settle(1, FakeWeather.Sun(), AsCells(map),
            new AnimalsBase[] { first, second });

        Assert.AreEqual(1, report.ScavengedCount, "同一份余屑每晚只允许被预留一次");
        Assert.AreEqual(0, target.Detritus, "那份余屑确实被消耗掉了");
        Assert.AreEqual(1, report.Rejected, "没抢到的那只应当被记为竞争失败");
    }

    // ---- 决策优先级 ----

    [Test]
    public void 携带种子时蚂蚁优先送货而不同时捕食()
    {
        FakeMap map = BuildLinkedMap();
        FakeCell cell = map.Get(Vector2Int.zero);
        Ant ant = PlaceAnimal(new Ant(2), cell);
        ant.SetCarried(E_ResourceKind.Seed);

        AnimalSettlePlan plan = ant.Propose(1,
            AnimalSettler.BuildWorld(FakeWeather.Sun(), AsCells(map), new AnimalsBase[] { ant }), cell);

        Assert.AreEqual(E_AnimalGoal.Delivery, plan.Goal, "第二层优先级：先把携带物送到位");
        Assert.IsTrue(plan.PutDownCarried, "到达后应当放下种子");
    }

    [Test]
    public void 感知到捕食者时优先逃离而不是去觅食()
    {
        FakeMap map = BuildLinkedMap();
        FakeCell center = map.Get(Vector2Int.zero);
        var frog = PlaceAnimal(new Frog(3), center);
        Earthworm worm = PlaceAnimal(new Earthworm(3), map.Get(new Vector2Int(1, 0)));

        AnimalSettlePlan plan = worm.Propose(1,
            AnimalSettler.BuildWorld(FakeWeather.Sun(), AsCells(map),
                new AnimalsBase[] { frog, worm }), worm.CurrentCell);

        Assert.AreEqual(E_AnimalGoal.Escape, plan.Goal, "第一层优先级：先处理危险");
    }

    [Test]
    public void 没有合法目标时本晚休眠不凭空行动()
    {
        FakeMap map = BuildLinkedMap(0);
        FakeCell cell = map.Get(Vector2Int.zero);
        Spider spider = PlaceAnimal(new Spider(3), cell);

        AnimalSettlePlan plan = spider.Propose(1,
            AnimalSettler.BuildWorld(FakeWeather.Sun(), AsCells(map), new AnimalsBase[] { spider }), cell);

        // 蜘蛛的常规行为是结网，这在只有一格且没有猎物的地图上仍然成立，
        // 但它不该在这晚凭空移动或进食。
        Assert.IsNull(plan.MoveTarget, "没有目标时不应移动");
        Assert.AreEqual(0, plan.SatietyGain, "没有食物时不应获得饱腹");
    }

    [Test]
    public void 蜘蛛不受天气影响仍然可以捕食()
    {
        FakeMap map = BuildLinkedMap();
        FakeCell center = map.Get(Vector2Int.zero);
        var spider = PlaceAnimal(new Spider(0), center);
        Ant ant = PlaceAnimal(new Ant(2), map.Get(new Vector2Int(1, 0)));

        // 策划案把蜘蛛定义为非天气型捕食者：连续晴天也要能发生捕食。
        var settler = new AnimalSettler();
        AnimalSettleReport sunny = settler.Settle(1, FakeWeather.Sun(3), AsCells(map),
            new AnimalsBase[] { spider, ant });

        Assert.AreEqual(1, sunny.HuntedCount, "非天气型捕食者在晴天同样成立");
    }

    [Test]
    public void 蚂蚁搬运成熟株的种子后可以种出新植物()
    {
        FakeMap map = BuildLinkedMap();
        FakeCell home = map.Get(Vector2Int.zero);
        FakeCell source = map.Get(new Vector2Int(1, 0));

        // 先用松土把它催到成熟并结出种子（成熟门槛为 3 点成长点）。
        var angiosperm = new Angiosperm();
        angiosperm.PlaceAt(source.Coordinates);
        source.TrySetSubject(angiosperm);
        SeedPlantsBase seedPlant = angiosperm;
        seedPlant.GrantGrowth(3);
        Assert.IsTrue(seedPlant.HasSeedOnGround, "成熟株应当在地上结出种子");

        Ant ant = PlaceAnimal(new Ant(0), home);
        var settler = new AnimalSettler();

        // 第 1 晚：过去吃一口并顺手带走种子。
        settler.Settle(1, FakeWeather.Sun(), AsCells(map), new AnimalsBase[] { ant });
        Assert.AreEqual(E_ResourceKind.Seed, ant.Carried, "蚂蚁应当拿起了种子");

        // 第 2 晚：带着种子优先送货，种下一株新的被子植物。
        AnimalSettleReport report = settler.Settle(2, FakeWeather.Sun(), AsCells(map), new AnimalsBase[] { ant });
        Assert.AreEqual(E_ResourceKind.None, ant.Carried, "送达后不再携带");
        Assert.AreEqual(1, report.NewPlants.Count, "应当在空地上真的种出一株植物");
    }

    [Test]
    public void 蚯蚓松土给种子植物追加成长点()
    {
        FakeMap map = BuildLinkedMap();
        FakeCell center = map.Get(Vector2Int.zero);
        Earthworm worm = PlaceAnimal(new Earthworm(3), center);

        var gymnosperm = new Gymnosperm();
        gymnosperm.PlaceAt(center.Coordinates);
        center.TrySetSubject(gymnosperm);

        int before = gymnosperm.GrowthPoints;
        var settler = new AnimalSettler();
        settler.Settle(1, FakeWeather.Sun(), AsCells(map), new AnimalsBase[] { worm });

        Assert.AreEqual(before + 1, gymnosperm.GrowthPoints,
            "策划案：蚯蚓松土让种子植物获得额外成长点");
    }

    [Test]
    public void 蜗牛取食苔藓会扣减苔藓的活力()
    {
        FakeMap map = BuildLinkedMap();
        FakeCell cell = map.Get(Vector2Int.zero);
        var moss = new Moss();
        moss.PlaceAt(cell.Coordinates);
        cell.TryAddCover(moss);

        int vitalityBefore = moss.Vitality;
        Snail snail = PlaceAnimal(new Snail(0), cell);

        var settler = new AnimalSettler();
        settler.Settle(1, FakeWeather.Sun(), AsCells(map), new AnimalsBase[] { snail });

        Assert.Less(moss.Vitality, vitalityBefore, "取食应当真的扣掉苔藓的活力");
    }

    [Test]
    public void 动物移动不会超过每晚三格()
    {
        FakeMap map = BuildLinkedMap(4);
        FakeCell start = map.Get(new Vector2Int(-4, 0));
        // 余屑放在感知范围内、却超出一晚能走到的距离：动物应当朝它推进一部分。
        map.Get(new Vector2Int(2, 0)).Detritus = 3;

        Earthworm worm = PlaceAnimal(new Earthworm(0), start);
        int limit = worm.MaxStepsForTonight;
        int before = worm.Coordinates.x;

        var settler = new AnimalSettler();
        settler.Settle(1, FakeWeather.Sun(), AsCells(map), new AnimalsBase[] { worm });

        int moved = worm.Coordinates.x - before;
        Assert.Greater(moved, 0, "应当朝着感知到的余屑推进而不是原地不动");
        Assert.LessOrEqual(moved, limit, "策划案：每只动物每晚最多移动 3 格（饥饿时 +1）");
        Assert.AreNotEqual(new Vector2Int(2, 0), worm.Coordinates, "一次走不到的目标只能逐步靠近");
    }
}
