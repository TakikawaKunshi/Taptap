using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 验证 PlantSettler 的三件事：固定顺序收集、扩张竞争裁决、与遍历顺序无关。
/// 对应策划案"同一资源点每晚只允许一份资源被预留，
/// 竞争由固定规则裁决，不按对象遍历顺序偷偷决定赢家"。
/// </summary>
public class PlantSettlerTests
{
    /// <summary>3x3 地图，全部同一种湿润状态。</summary>
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

    /// <summary>按坐标计算真实四邻接，顺序为下、右、上、左。</summary>
    private static void LinkAll(FakeMap map)
    {
        for (int y = -3; y <= 3; y++)
        {
            for (int x = -3; x <= 3; x++)
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

    // ============================================================
    // 放置规则
    //
    // CanOccupy 只看主体位。封锁一个格子的扩张要用种子植物占主体位；
    // 用苔藓占位只占地表覆盖，该格仍然可以扩张。
    // ============================================================

    [Test]
    public void 放置孢子植物进入地表覆盖()
    {
        FakeMap map = Build3x3();
        FakeCell cell = map.Get(Vector2Int.zero);
        Moss moss = new Moss();

        Assert.IsTrue(PlantSettler.Place(moss, cell));
        Assert.IsNull(cell.Subject, "苔藓是地表覆盖，不占主体位");
        Assert.AreEqual(1, cell.Cover.Count);
        Assert.AreSame(moss, cell.Cover[0]);
    }

    [Test]
    public void 放置种子植物进入主体位()
    {
        FakeMap map = Build3x3();
        FakeCell cell = map.Get(Vector2Int.zero);
        Gymnosperm pine = new Gymnosperm();

        Assert.IsTrue(PlantSettler.Place(pine, cell));
        Assert.AreSame(pine, cell.Subject, "种子植物占主体格");
        Assert.AreEqual(0, cell.Cover.Count);
    }

    [Test]
    public void 主体位只能有一株植物()
    {
        FakeMap map = Build3x3();
        FakeCell cell = map.Get(Vector2Int.zero);

        Gymnosperm first = new Gymnosperm();
        Assert.IsTrue(PlantSettler.Place(first, cell));
        Assert.IsFalse(PlantSettler.Place(new Angiosperm(), cell), "主体位已有植物时不能再放");
        Assert.AreSame(first, cell.Subject, "被拒绝的放置不应覆盖原主体");
    }

    [Test]
    public void 同一株植物不能重复放置()
    {
        FakeMap map = Build3x3();
        Moss moss = new Moss();
        PlantSettler.Place(moss, map.Get(Vector2Int.zero));

        Assert.Throws<System.InvalidOperationException>(
            () => moss.PlaceAt(new Vector2Int(1, 1)), "重复放置说明占位逻辑有问题");
    }

    // ============================================================
    // 扩张
    // ============================================================

    [Test]
    public void 雨天苔藓扩张到固定顺序的第一个空格()
    {
        FakeMap map = Build3x3();
        PlantSettler.Place(new Moss(), map.Get(Vector2Int.zero));

        var settler = new PlantSettler();
        PlantSettleReport report = settler.Settle(1, FakeWeather.Rain(1), map.Cells);

        Assert.AreEqual(1, report.NewGrowths.Count, "策划案：雨天向 1 个相邻空地扩张");
        Assert.AreEqual(0, report.ExpansionRejected);
        Assert.AreEqual(1, map.Get(new Vector2Int(0, -1)).Cover.Count, "邻接顺序为下右上左，应先占下方");
        Assert.AreEqual(0, map.Get(new Vector2Int(1, 0)).Cover.Count);
    }

    [Test]
    public void 晴天苔藓不扩张()
    {
        FakeMap map = Build3x3();
        PlantSettler.Place(new Moss(), map.Get(Vector2Int.zero));

        var settler = new PlantSettler();
        PlantSettleReport report = settler.Settle(1, FakeWeather.Sun(1), map.Cells);

        Assert.AreEqual(0, report.NewGrowths.Count, "策划案只在雨天扩张");
    }

    [Test]
    public void 扩张绕开被占住的邻格()
    {
        var map = new FakeMap();
        map.Add(new Vector2Int(0, 0));
        map.Add(new Vector2Int(1, 0));
        map.Add(new Vector2Int(0, -1));
        LinkAll(map);

        // 种子植物占住 (0,-1) 的主体位，该格不可再被占用。
        PlantSettler.Place(new Gymnosperm(), map.Get(new Vector2Int(0, -1)));
        PlantSettler.Place(new Moss(), map.Get(Vector2Int.zero));

        var settler = new PlantSettler();
        PlantSettleReport report = settler.Settle(1, FakeWeather.Rain(1), map.Cells);

        Assert.AreEqual(1, report.NewGrowths.Count, "绕开被占的下邻后仍应扩张成功");
        Assert.AreEqual(1, map.Get(new Vector2Int(1, 0)).Cover.Count, "应扩张到右邻格");
        Assert.AreEqual(0, report.ExpansionRejected);
    }

    [Test]
    public void 邻格全被占满时苔藓不会提出扩张申请()
    {
        var map = new FakeMap();
        map.Add(Vector2Int.zero);
        map.Add(new Vector2Int(0, -1));
        map.Add(new Vector2Int(1, 0));
        map.Add(new Vector2Int(0, 1));
        map.Add(new Vector2Int(-1, 0));
        LinkAll(map);

        // 四个邻格全部用种子植物占住主体位。
        PlantSettler.Place(new Gymnosperm(), map.Get(new Vector2Int(0, -1)));
        PlantSettler.Place(new Gymnosperm(), map.Get(new Vector2Int(1, 0)));
        PlantSettler.Place(new Gymnosperm(), map.Get(new Vector2Int(0, 1)));
        PlantSettler.Place(new Gymnosperm(), map.Get(new Vector2Int(-1, 0)));
        PlantSettler.Place(new Moss(), map.Get(Vector2Int.zero));

        var settler = new PlantSettler();
        PlantSettleReport report = settler.Settle(1, FakeWeather.Rain(1), map.Cells);

        Assert.AreEqual(0, report.NewGrowths.Count, "无可用邻格时不能扩张");
        // 苔藓在提案阶段就发现没有空格，因此不会提出申请，也就无需裁决拒绝。
        Assert.AreEqual(0, report.ExpansionRejected, "没有提出申请就不存在被拒绝的扩张");
        Assert.Greater(report.Logs.Count, 0, "本晚仍应留下可解释记录");
    }

    /// <summary>
    /// 构造一个"两株苔藓只能抢同一个空格"的地图：
    /// (0,0) 与 (2,0) 各自被主体植物封住除 (1,0) 之外的全部邻格，
    /// 因此两株都只能申请扩张到 (1,0)，必然在裁决阶段发生竞争。
    /// </summary>
    private static FakeMap BuildContendedLayout(out Moss atOrigin, out Moss atEast)
    {
        var map = new FakeMap();
        // 两株苔藓的位置与争夺的空格。
        map.Add(Vector2Int.zero);
        map.Add(new Vector2Int(1, 0));
        map.Add(new Vector2Int(2, 0));
        // 封锁用的格子。
        map.Add(new Vector2Int(0, -1));
        map.Add(new Vector2Int(0, 1));
        map.Add(new Vector2Int(-1, 0));
        map.Add(new Vector2Int(2, -1));
        map.Add(new Vector2Int(2, 1));
        map.Add(new Vector2Int(3, 0));
        LinkAll(map);

        // 用种子植物占住主体位，使这些格子不可被占用。
        PlantSettler.Place(new Gymnosperm(), map.Get(new Vector2Int(0, -1)));
        PlantSettler.Place(new Gymnosperm(), map.Get(new Vector2Int(0, 1)));
        PlantSettler.Place(new Gymnosperm(), map.Get(new Vector2Int(-1, 0)));
        PlantSettler.Place(new Gymnosperm(), map.Get(new Vector2Int(2, -1)));
        PlantSettler.Place(new Gymnosperm(), map.Get(new Vector2Int(2, 1)));
        PlantSettler.Place(new Gymnosperm(), map.Get(new Vector2Int(3, 0)));

        atOrigin = new Moss();
        atEast = new Moss();
        PlantSettler.Place(atOrigin, map.Get(Vector2Int.zero));
        PlantSettler.Place(atEast, map.Get(new Vector2Int(2, 0)));
        return map;
    }

    [Test]
    public void 两株苔藓争同一个空格时一株成功一株被拒()
    {
        Moss atOrigin;
        Moss atEast;
        FakeMap map = BuildContendedLayout(out atOrigin, out atEast);

        var settler = new PlantSettler();
        PlantSettleReport report = settler.Settle(1, FakeWeather.Rain(1), map.Cells);

        Assert.AreEqual(1, report.NewGrowths.Count, "唯一的空格只能容纳一株");
        Assert.AreEqual(1, report.ExpansionRejected, "另一株的扩张必须被拒绝并计数");
        Assert.IsTrue(HasLogContaining(report, "已被占满"),
            "策划案要求拒绝原因写进日志，供玩家回答为什么没有扩张");
    }

    /// <summary>
    /// 裁决必须按固定顺序，与传入的遍历顺序无关。
    /// 离地图中心更近的 (0,0) 应当稳定获胜。
    /// </summary>
    [Test]
    public void 争抢同一空格的赢家不随遍历顺序变化()
    {
        int expectedWinner = -1;
        for (int run = 0; run < 8; run++)
        {
            Moss atOrigin;
            Moss atEast;
            FakeMap map = BuildContendedLayout(out atOrigin, out atEast);

            var random = new System.Random(run * 31 + 5);
            List<FakeCell> shuffled = map.Shuffled(random);

            new PlantSettler().Settle(1, FakeWeather.Rain(1), shuffled);

            // Expansion 递增说明该株的扩张被批准。
            int winner = atOrigin.Expansion > 0 ? 0 : atEast.Expansion > 0 ? 1 : -1;
            if (expectedWinner == -1)
                expectedWinner = winner;
            Assert.AreEqual(expectedWinner, winner,
                "第 " + run + " 次运行赢家发生变化，违反策划案的可复现性要求");
        }

        Assert.AreEqual(0, expectedWinner, "离地图中心更近的 (0,0) 应稳定获胜");
    }

    [Test]
    public void 扩张结果不依赖传入顺序()
    {
        HashSet<Vector2Int> expected = null;

        for (int run = 0; run < 8; run++)
        {
            FakeMap map = Build3x3();
            // 三个苔藓互相竞争同一批空格。
            PlantSettler.Place(new Moss(), map.Get(new Vector2Int(-1, -1)));
            PlantSettler.Place(new Moss(), map.Get(new Vector2Int(1, 1)));
            PlantSettler.Place(new Moss(), map.Get(new Vector2Int(0, -1)));

            var random = new System.Random(run * 7919 + 13);
            List<FakeCell> shuffled = map.Shuffled(random);

            var settler = new PlantSettler();
            settler.Settle(1, FakeWeather.Rain(1), shuffled);

            var occupied = new HashSet<Vector2Int>();
            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    if (map.Get(new Vector2Int(x, y)).Cover.Count > 0)
                        occupied.Add(new Vector2Int(x, y));
                }
            }

            if (expected == null)
            {
                expected = occupied;
                continue;
            }

            CollectionAssert.AreEquivalent(expected, occupied,
                "第 " + run + " 次运行占据的格子集合与第一次不同，说明结果依赖遍历顺序");
        }
    }

    // ============================================================
    // 回收
    // ============================================================

    [Test]
    public void 枯萎回收后余屑落在原格()
    {
        FakeMap map = Build3x3();
        FakeCell cell = map.Get(Vector2Int.zero);
        Moss moss = new Moss();
        PlantSettler.Place(moss, cell);

        while (moss.Vitality > 0)
            moss.TakeBite(1);

        var settler = new PlantSettler();
        PlantSettleReport report = settler.Settle(1, FakeWeather.Rain(1), map.Cells);

        Assert.IsTrue(moss.IsDead);
        Assert.AreEqual(1, report.Recycled.Count);
        Assert.AreEqual(1, cell.Detritus, "策划案：枯萎回收产出 1 份余屑");
    }

    [Test]
    public void 回收后的植物不再参与后续结算()
    {
        FakeMap map = Build3x3();
        Moss moss = new Moss();
        PlantSettler.Place(moss, map.Get(Vector2Int.zero));
        while (moss.Vitality > 0)
            moss.TakeBite(1);

        var settler = new PlantSettler();
        settler.Settle(1, FakeWeather.Rain(1), map.Cells);
        PlantSettleReport second = settler.Settle(2, FakeWeather.Rain(1), map.Cells);

        Assert.AreEqual(0, second.SettledCount, "已回收的植物不该再被结算");
    }

    // ============================================================
    // 天气规则
    // ============================================================

    [Test]
    public void 晴天孢子植物按策划案规则衰减()
    {
        // 晴天要观察到衰减，格子必须仍满足最低生长条件，否则按设计整晚不变化。
        // 这里用石面：策划案允许苔藓长在石面上，石面在晴天依然成立。
        FakeMap map = Build3x3(isWet: false);
        FakeCell cell = map.Get(Vector2Int.zero);
        cell.Surface = E_SurfaceKind.Stone;
        Moss moss = new Moss();
        PlantSettler.Place(moss, cell);

        int before = moss.Vitality;
        new PlantSettler().Settle(1, FakeWeather.Sun(1), map.Cells);

        Assert.AreEqual(before - 1, moss.Vitality, "策划案：苔藓晴天-1");
    }

    [Test]
    public void 干燥的普通地面在晴天仍然衰减()
    {
        // 策划案："晴 → 晴 土地更干，孢子植物衰减"。
        // 最低生长条件只决定能不能长，不决定会不会枯，否则植物会被冻结在原地。
        FakeMap map = Build3x3(isWet: false);
        Moss moss = new Moss();
        PlantSettler.Place(moss, map.Get(Vector2Int.zero));

        int before = moss.Vitality;
        new PlantSettler().Settle(1, FakeWeather.Sun(1), map.Cells);

        Assert.AreEqual(before - 1, moss.Vitality, "晴天一律衰减，与是否满足生长条件无关");
    }

    [Test]
    public void 干燥的普通地面在雨天不增长()
    {
        FakeMap map = Build3x3(isWet: false);
        Moss moss = new Moss();
        PlantSettler.Place(moss, map.Get(Vector2Int.zero));

        // 雨天但格子不湿润，理论上格子状态与天气矛盾；这里显式构造以验证最低条件的守卫。
        int before = moss.Vitality;
        new PlantSettler().Settle(1, FakeWeather.Rain(1), map.Cells);

        Assert.AreEqual(before, moss.Vitality, "雨天不满足最低条件时只记录原因，不增长");
    }

    [Test]
    public void 七天晴雨交替可以让种子植物完成多个阶段()
    {
        FakeMap map = Build3x3();
        Gymnosperm pine = new Gymnosperm();
        PlantSettler.Place(pine, map.Get(Vector2Int.zero));

        var settler = new PlantSettler();
        bool[] sun = { true, false, true, false, true, false, true };
        for (int day = 1; day <= 7; day++)
            settler.Settle(day, sun[day - 1] ? FakeWeather.Sun(1) : FakeWeather.Rain(1), map.Cells);

        Assert.AreEqual(3, pine.GrowthPoints, "4 次交替产生 3 个成长点");
        Assert.AreEqual(E_PlantStage.Mature, pine.Stage, "策划案验收：7 天内达到成熟株");
    }

    [Test]
    public void 相同输入得到相同结果()
    {
        int[] first = RunSevenDays(12345);
        int[] second = RunSevenDays(12345);
        CollectionAssert.AreEqual(first, second,
            "策划案验收：相同初始布局、天气序列和操作顺序应得到同样结果");
    }

    /// <summary>跑满 7 天，每天结束后的全图活力与余屑快照。</summary>
    private static int[] RunSevenDays(int seed)
    {
        FakeMap map = Build3x3();
        PlantSettler.Place(new Moss(), map.Get(new Vector2Int(-1, -1)));
        PlantSettler.Place(new Algae(), map.Get(new Vector2Int(1, 1)));
        PlantSettler.Place(new Gymnosperm(), map.Get(Vector2Int.zero));
        map.Get(new Vector2Int(1, 1)).Detritus = 2;

        var settler = new PlantSettler();
        bool[] sun = { true, false, true, true, false, false, true };
        var random = new System.Random(seed);
        var snapshot = new List<int>();

        for (int day = 1; day <= 7; day++)
        {
            // 故意打乱传入顺序，验证内部排序确实生效。
            settler.Settle(day, sun[day - 1] ? FakeWeather.Sun(1) : FakeWeather.Rain(1), map.Shuffled(random));
            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    FakeCell cell = map.Get(new Vector2Int(x, y));
                    snapshot.Add(cell.Detritus);
                    Moss moss = FindMoss(cell);
                    snapshot.Add(moss != null ? moss.Vitality : -1);
                }
            }
        }

        return snapshot.ToArray();
    }

    private static Moss FindMoss(FakeCell cell)
    {
        for (int i = 0; i < cell.Cover.Count; i++)
        {
            Moss moss = cell.Cover[i] as Moss;
            if (moss != null && !moss.IsDead)
                return moss;
        }

        return null;
    }

    private static bool HasLogContaining(PlantSettleReport report, string keyword)
    {
        for (int i = 0; i < report.Logs.Count; i++)
        {
            if (report.Logs[i].Text != null && report.Logs[i].Text.Contains(keyword))
                return true;
        }

        return false;
    }
}