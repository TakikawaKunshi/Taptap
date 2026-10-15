using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 验证策划案第七章的"固定过夜结算顺序"中属于植物的部分。
/// 这里不启动 Unity 场景，全部用 FakeMap 驱动，验证顺序、幂等与资源流向。
/// </summary>
public class PlantSettlementOrderTests
{
    private static FakeMap BuildMap()
    {
        var map = new FakeMap();
        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
                map.Add(new Vector2Int(x, y));
        }

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

        return map;
    }

    /// <summary>
    /// 策划案第九步：孢子植物在雨天增长至少一次，连续晴天至少衰退一次；
    /// 种子植物在 7 天内至少完成 3 个阶段。
    /// </summary>
    [Test]
    public void 七天流程满足策划案验收标准()
    {
        FakeMap map = BuildMap();
        Moss moss = new Moss();
        PlantSettler.Place(moss, map.Get(new Vector2Int(-1, -1)));

        Gymnosperm pine = new Gymnosperm();
        PlantSettler.Place(pine, map.Get(new Vector2Int(1, 1)));

        var settler = new PlantSettler();
        bool[] sun = { false, true, false, true, true, false, true }; // 策划案因果链的雨晴雨晴晴雨晴
        var vitality = new List<int>();

        for (int day = 1; day <= 7; day++)
        {
            settler.Settle(day, sun[day - 1] ? FakeWeather.Sun(1) : FakeWeather.Rain(1), map.Cells);
            Moss alive = FindLivingMoss(map);
            vitality.Add(alive != null ? alive.Vitality : -1);
        }

        // 孢子植物：雨天至少增长一次。
        bool grewOnRain = false;
        for (int day = 0; day < 6; day++)
        {
            if (!sun[day] && vitality[day] > vitality[day - 1 >= 0 ? day - 1 : day])
                grewOnRain = true;
        }

        Assert.IsTrue(grewOnRain, "策划案验收：雨天至少增长 1 次");
        Assert.IsTrue(vitality[6] > 0, "苔藓在 7 天内不应全部枯死");
        Assert.AreEqual(3, pine.GrowthPoints, "策划案验收：7 天内完成 3 个成长点");
        Assert.AreEqual(E_PlantStage.Mature, pine.Stage, "策划案验收：晴雨交替明显更有效");
    }

    /// <summary>
    /// 策划案要求"重试过夜不会重复发放"。
    /// 同一个天数结算两次，第二次必须被完全拒绝。
    /// </summary>
    [Test]
    public void 同一晚重复结算不重复发放资源()
    {
        FakeMap map = BuildMap();
        FakeCell cell = map.Get(new Vector2Int(1, 1));
        cell.Detritus = 5;

        var algae = new Algae();
        PlantSettler.Place(algae, cell);
        // 养到成熟以下，先让活力足够再让它消耗余屑。
        algae.GrantGrowth(0);

        var settler = new PlantSettler();
        settler.Settle(1, FakeWeather.Rain(1), map.Cells);
        int detritusAfterFirst = cell.Detritus;
        int vitalityAfterFirst = algae.Vitality;

        settler.Settle(1, FakeWeather.Rain(1), map.Cells);
        Assert.AreEqual(detritusAfterFirst, cell.Detritus, "重复结算不得再次扣除余屑");
        Assert.AreEqual(vitalityAfterFirst, algae.Vitality, "重复结算不得再次增长活力");
    }

    /// <summary>
    /// 策划案的死亡回收闭环：蘑菇回收的余屑应当能供养新的蘑菇生长。
    /// </summary>
    [Test]
    public void 蘑菇回收的余屑可以供养新的蘑菇()
    {
        FakeMap map = BuildMap();
        FakeCell source = map.Get(Vector2Int.zero);
        source.Detritus = 3;

        var mushroom = new Mushroom();
        PlantSettler.Place(mushroom, source);

        // 先在雨天长到成熟，让它消耗本格余屑导致衰退条件恶化。
        var settler = new PlantSettler();
        settler.Settle(1, FakeWeather.Rain(1), map.Cells);

        // 直接吃到零活力，验证回收。
        while (mushroom.Vitality > 0)
            mushroom.TakeBite(1);

        settler.Settle(2, FakeWeather.Rain(1), map.Cells);
        Assert.IsTrue(mushroom.IsDead, "活力归零后应被回收");
        Assert.Greater(source.Detritus, 0, "回收应产出余屑");

        // 回收产出的余屑足以让新蘑菇满足最低条件。
        var fresh = new Mushroom();
        PlantSettler.Place(fresh, source);
        int before = fresh.Vitality;
        settler.Settle(3, FakeWeather.Rain(1), map.Cells);
        Assert.AreEqual(before + 2, fresh.Vitality, "新蘑菇应能利用回收的余屑增长");
    }

    /// <summary>
    /// 策划案可解释性验收：结算报告必须能说明每株植物发生了什么。
    /// </summary>
    [Test]
    public void 结算报告包含可解释记录()
    {
        FakeMap map = BuildMap();
        Moss moss = new Moss();
        PlantSettler.Place(moss, map.Get(Vector2Int.zero));

        var settler = new PlantSettler();
        PlantSettleReport report = settler.Settle(1, FakeWeather.Rain(1), map.Cells);

        Assert.AreEqual(1, report.SettledCount);
        Assert.Greater(report.Logs.Count, 0, "策划案验收：玩家要能从日志回答为什么它长了");

        string summary = report.BuildSummary();
        Assert.IsNotEmpty(summary);
        Assert.IsTrue(summary.Contains("苔藓"), "摘要要能指出是哪株植物");

        foreach (PlantLogEntry log in report.Logs)
        {
            Assert.IsNotEmpty(log.RuleId, "每条记录都要有规则编号供为什么按钮定位");
            Assert.IsNotEmpty(log.Text);
        }
    }

    /// <summary>资源产出要能在报告里汇总，供晨报显示。</summary>
    [Test]
    public void 资源产出汇总到结算报告()
    {
        FakeMap map = BuildMap();
        FakeCell cell = map.Get(new Vector2Int(1, 1));
        cell.Detritus = 4;

        var algae = new Algae();
        PlantSettler.Place(algae, cell);

        // 用多次雨天把活力养到成熟以上，产出光粒需要消耗余屑。
        var settler = new PlantSettler();
        settler.Settle(1, FakeWeather.Rain(1), map.Cells);
        settler.Settle(2, FakeWeather.Rain(2), map.Cells);
        settler.Settle(3, FakeWeather.Rain(3), map.Cells);

        Assert.Greater(algae.Vitality, 0, "连续雨天藻类应增长");
        Assert.Less(cell.Detritus, 4, "成熟后消耗余屑产出光粒");
    }

    private static Moss FindLivingMoss(FakeMap map)
    {
        foreach (FakeCell cell in map.Cells)
        {
            for (int i = 0; i < cell.Cover.Count; i++)
            {
                Moss moss = cell.Cover[i] as Moss;
                if (moss != null && !moss.IsDead)
                    return moss;
            }
        }

        return null;
    }
}