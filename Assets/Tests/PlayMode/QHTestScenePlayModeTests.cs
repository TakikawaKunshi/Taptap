using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>
/// 在真实的 QH Test 场景里验证植物链路。
/// 与 EditMode 测试的区别：这里会加载场景、让 TileMapManager 真的生成格子，
/// 因此能验证格子的序列化接线、占据逻辑与七日演化。
/// </summary>
public class QHTestScenePlayModeTests
{
    private const string ScenePath = "Assets/Scenes/TestScene/QH Test.unity";

    [UnitySetUp]
    public IEnumerator LoadScene()
    {
        yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
        // 等待 TileMapManager.Start 完成格子生成。
        yield return null;
        yield return null;
        // 让当前测试的天气状态生效：TileMapManager 在 LateUpdate 里按 weatherType 同步格子，
        // 所以改完天气后至少再走两帧，才能读到 Wet/Dry。
        ApplyWeather(E_WeatherType.Rain);
        yield return null;
        yield return null;
    }

    /// <summary>设置当天天气并等格子状态同步完成。</summary>
    private static void ApplyWeather(E_WeatherType type)
    {
        WeatherBase weather = Object.FindObjectOfType<WeatherBase>();
        if (weather != null)
            weather.weatherType = type;
    }

    [Test]
    public void 场景生成了有效格子()
    {
        TileMapManager mapManager = Object.FindObjectOfType<TileMapManager>();
        Assert.IsNotNull(mapManager, "场景里必须有 TileMapManager");
        Assert.Greater(mapManager.CellCount, 0, "TileMapManager 应该在 Start 里生成格子");
    }

    [Test]
    public void 格子的序列化接线正确()
    {
        TileMapManager mapManager = Object.FindObjectOfType<TileMapManager>();
        WeatherBase weather = Object.FindObjectOfType<WeatherBase>();
        NightSettlement settlement = Object.FindObjectOfType<NightSettlement>();
        PlantSpawner spawner = Object.FindObjectOfType<PlantSpawner>();

        Assert.IsNotNull(weather, "场景里必须有 WeatherBase");
        Assert.IsNotNull(mapManager.Tilemap, "TileMapManager 必须绑定 Tilemap");
        Assert.IsNotNull(mapManager.Weather, "TileMapManager 必须绑定 WeatherBase");
        Assert.IsNotNull(settlement.Weather, "NightSettlement 必须绑定 WeatherBase，否则过夜结算会被拒绝");
        Assert.IsNotNull(spawner, "场景里必须有 PlantSpawner");
        Assert.IsNotNull(spawner.Map, "PlantSpawner 必须绑定 TileMapManager");
    }

    [Test]
    public void 场景启动后自动铺设初始植物()
    {
        TileMapManager mapManager = Object.FindObjectOfType<TileMapManager>();
        PlantSpawner spawner = Object.FindObjectOfType<PlantSpawner>();

        Assert.AreEqual(4, spawner.LastPlacedCount, "无需手动铺场，开局应自动生成 4 株植物");
        Assert.IsTrue(mapManager.TryGetCell(new Vector2Int(1, 0), out TileCell detritusCell));
        Assert.AreEqual(1, detritusCell.Detritus, "开局应只发放一份初始余屑");

        int mossCount = 0;
        int seedCount = 0;
        foreach (TileCell cell in mapManager.Cells)
        {
            for (int i = 0; i < cell.Cover.Count; i++)
            {
                if (cell.Cover[i] is Moss)
                    mossCount++;
            }

            if (cell.Subject is SeedPlantsBase)
                seedCount++;
        }

        Assert.AreEqual(1, mossCount, "初始配置里有一株雨眠苔");
        Assert.AreEqual(2, seedCount, "初始配置里有一粒向日铃花种子与一粒星壳针木种子");
    }

    [UnityTest]
    public IEnumerator 七天结算后孢子植物有增有减()
    {
        TileMapManager mapManager = Object.FindObjectOfType<TileMapManager>();
        PlantSpawner spawner = Object.FindObjectOfType<PlantSpawner>();
        spawner.SpawnInitialLayout();

        WeatherBase weather = Object.FindObjectOfType<WeatherBase>();
        var query = new PlayModeWeatherQuery(weather);
        var settler = new PlantSettler();

        // 与策划案第七章的示例天气序列一致。
        E_WeatherType[] sequence =
        {
            E_WeatherType.Rain, E_WeatherType.Sun, E_WeatherType.Rain, E_WeatherType.Sun,
            E_WeatherType.Sun, E_WeatherType.Rain, E_WeatherType.Sun
        };

        // 初始配置把雨眠苔放在 (-2,-1)，但那只在地图范围内成立；
        // 天气变化通过 TileMapManager 同步到格子，因此这里比较"所有苔藓活力之和"。
        var mossVitals = new System.Collections.Generic.List<int>();
        var reports = new System.Collections.Generic.List<PlantSettleReport>();
        for (int day = 1; day <= 7; day++)
        {
            weather.weatherType = sequence[day - 1];
            weather.RecordDayWeather(day, sequence[day - 1]);
            query.Refresh(sequence, day);

            // 格子状态由 TileMapManager 在 LateUpdate 里同步，必须让出一帧再结算，
            // 否则植物读到的仍是上一天的 Wet/Dry。
            yield return null;
            mapManager.RefreshWeather();

            reports.Add(settler.Settle(day, query, CollectCells(mapManager)));
            mossVitals.Add(TotalMossVitality(mapManager));
        }

        Assert.Greater(mossVitals[0], 0, "第 1 天是雨天，苔藓应当有活力");

        // 逐日断言用结算报告里的日志，而不是活力总和：
        // 扩张会新增苔藓，总和只升不降，会掩盖单株的衰退。
        var detail = new System.Text.StringBuilder();
        int rainGrowths = 0;
        int sunDecays = 0;
        for (int d = 0; d < reports.Count; d++)
        {
            for (int i = 0; i < reports[d].Logs.Count; i++)
            {
                PlantLogEntry log = reports[d].Logs[i];
                detail.Append('[').Append(log.RuleId).Append(']').Append(log.Text).Append('\n');
                if (log.RuleId.EndsWith("MOIST", StringComparison.Ordinal))
                    rainGrowths++;
                if (log.RuleId.EndsWith("HEAT", StringComparison.Ordinal))
                    sunDecays++;
            }
        }

        Assert.GreaterOrEqual(rainGrowths, 1,
            "策划案验收：雨天至少增长 1 次，逐日总活力 " + string.Join(",", mossVitals)
            + "\n明细：\n" + detail);
        Assert.GreaterOrEqual(sunDecays, 1,
            "策划案验收：晴天至少衰退 1 次，逐日总活力 " + string.Join(",", mossVitals)
            + "\n明细：\n" + detail);

        // 扩张必须真的发生：新株是雨天增长的重要来源之一。
        int newGrowths = 0;
        foreach (PlantSettleReport report in reports)
            newGrowths += report.NewGrowths.Count;

        var lines = new System.Text.StringBuilder();
        for (int day = 0; day < reports.Count; day++)
        {
            lines.Append("第").Append(day + 1).Append("天(")
                .Append(sequence[day] == E_WeatherType.Sun ? "晴" : "雨").Append(") ")
                .Append("结算").Append(reports[day].SettledCount)
                .Append(" 新增").Append(reports[day].NewGrowths.Count)
                .Append(" 回收").Append(reports[day].Recycled.Count).Append(" | ");
            foreach (PlantLogEntry log in reports[day].Logs)
                lines.Append('[').Append(log.RuleId).Append(']').Append(log.Text).Append(' ');
        }

        Assert.GreaterOrEqual(newGrowths, 1,
            "策划案验收：雨天苔藓应向相邻空地扩张。逐日总活力 " + string.Join(",", mossVitals)
            + "\n明细：" + lines);
    }

    [Test]
    public void 七天结算后种子植物完成多个阶段()
    {
        TileMapManager mapManager = Object.FindObjectOfType<TileMapManager>();
        PlantSpawner spawner = Object.FindObjectOfType<PlantSpawner>();
        spawner.SpawnInitialLayout();

        WeatherBase weather = Object.FindObjectOfType<WeatherBase>();
        var query = new PlayModeWeatherQuery(weather);
        var settler = new PlantSettler();

        // 策划案示例序列：雨晴雨晴晴雨晴。
        E_WeatherType[] sequence =
        {
            E_WeatherType.Rain, E_WeatherType.Sun, E_WeatherType.Rain, E_WeatherType.Sun,
            E_WeatherType.Sun, E_WeatherType.Rain, E_WeatherType.Sun
        };

        for (int day = 1; day <= 7; day++)
        {
            weather.weatherType = sequence[day - 1];
            weather.RecordDayWeather(day, sequence[day - 1]);
            query.Refresh(sequence, day);
            settler.Settle(day, query, CollectCells(mapManager));
        }

        SeedPlantsBase best = null;
        foreach (TileCell cell in mapManager.Cells)
        {
            SeedPlantsBase seed = cell.Subject as SeedPlantsBase;
            if (seed == null)
                continue;
            if (best == null || seed.GrowthPoints > best.GrowthPoints)
                best = seed;
        }

        Assert.IsNotNull(best, "初始配置里应有种子植物");
        Assert.GreaterOrEqual(best.GrowthPoints, 2, "策划案验收：晴雨交替应明显推进成长");
        Assert.GreaterOrEqual((int)best.Stage, (int)E_PlantStage.Sprout, "至少进入幼芽");
    }

    [Test]
    public void 重复铺场不会覆盖已有植物()
    {
        PlantSpawner spawner = Object.FindObjectOfType<PlantSpawner>();
        int first = spawner.LastPlacedCount;
        Assert.IsTrue(spawner.Map.TryGetCell(new Vector2Int(1, 0), out TileCell cell));
        int initialDetritus = cell.Detritus;

        spawner.SpawnInitialLayout();
        Assert.AreEqual(0, spawner.LastPlacedCount, "第二次铺场应全部跳过，不覆盖已有植物");
        Assert.Greater(first, 0);
        Assert.AreEqual(initialDetritus, cell.Detritus, "重复铺场不应额外增加余屑");

        cell.Detritus = 0;
        spawner.SpawnInitialLayout();
        Assert.AreEqual(0, cell.Detritus, "已消耗的初始余屑也不应被再次发放");
    }

    [Test]
    public void 清空植物后地图回到开局()
    {
        TileMapManager mapManager = Object.FindObjectOfType<TileMapManager>();
        PlantSpawner spawner = Object.FindObjectOfType<PlantSpawner>();
        spawner.SpawnInitialLayout();
        spawner.ClearAllPlants();

        foreach (TileCell cell in mapManager.Cells)
        {
            Assert.IsNull(cell.Subject, "清空后不应残留主体植物");
            Assert.AreEqual(0, cell.Cover.Count, "清空后不应残留地表覆盖");
            Assert.AreEqual(0, cell.Detritus, "清空后不应残留余屑");
        }

        spawner.SpawnInitialLayout();
        Assert.AreEqual(4, spawner.LastPlacedCount, "清空后仍可手动重新铺场");
        Assert.IsTrue(mapManager.TryGetCell(new Vector2Int(1, 0), out TileCell seededCell));
        Assert.AreEqual(1, seededCell.Detritus, "重新铺场应恢复初始余屑");
    }

    [UnityTest]
    public IEnumerator 重新启用组件不会再次自动铺场()
    {
        PlantSpawner plants = Object.FindObjectOfType<PlantSpawner>();
        AnimalSpawner animals = Object.FindObjectOfType<AnimalSpawner>();
        plants.ClearAllPlants();
        animals.ClearAllAnimals();
        plants.enabled = false;
        animals.enabled = false;
        yield return null;
        plants.enabled = true;
        animals.enabled = true;
        yield return null;

        AssertEmptyEcology(plants.Map, animals);
    }

    [UnityTest]
    public IEnumerator 关闭开局投放后仍可手动铺场()
    {
        Object.FindObjectOfType<PlantSpawner>().ClearAllPlants();
        Object.FindObjectOfType<AnimalSpawner>().ClearAllAnimals();
        var host = new GameObject("Manual Spawner Test");
        try
        {
            PlantSpawner plants = host.AddComponent<PlantSpawner>();
            AnimalSpawner animals = host.AddComponent<AnimalSpawner>();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic;
            typeof(PlantSpawner).GetField("spawnOnStart", flags).SetValue(plants, false);
            typeof(AnimalSpawner).GetField("spawnOnStart", flags).SetValue(animals, false);
            yield return null;

            AssertEmptyEcology(plants.Map, animals);
            plants.SpawnInitialLayout();
            animals.SpawnInitialLayout();
            Assert.AreEqual(4, plants.LastPlacedCount);
            Assert.AreEqual(5, animals.LiveCount);
        }
        finally
        {
            Object.Destroy(host);
        }
    }

    [UnityTest]
    public IEnumerator 启动前手动铺场不会再自动投放()
    {
        Object.FindObjectOfType<PlantSpawner>().ClearAllPlants();
        Object.FindObjectOfType<AnimalSpawner>().ClearAllAnimals();
        var host = new GameObject("Early Spawner Test");
        try
        {
            PlantSpawner plants = host.AddComponent<PlantSpawner>();
            AnimalSpawner animals = host.AddComponent<AnimalSpawner>();
            plants.SpawnInitialLayout();
            animals.SpawnInitialLayout();
            yield return null;

            Assert.AreEqual(4, plants.LastPlacedCount, "Start 不应再次铺场并覆盖上次投放结果");
            Assert.AreEqual(5, animals.LastPlacedCount);
            Assert.AreEqual(5, animals.LiveCount);
            Assert.IsTrue(plants.Map.TryGetCell(new Vector2Int(1, 0), out TileCell cell));
            Assert.AreEqual(1, cell.Detritus);
        }
        finally
        {
            Object.Destroy(host);
        }
    }

    private static void AssertEmptyEcology(TileMapManager map, AnimalSpawner animals)
    {
        foreach (TileCell cell in map.Cells)
        {
            Assert.IsNull(cell.Subject);
            Assert.AreEqual(0, cell.Cover.Count);
            Assert.AreEqual(0, cell.Detritus);
        }
        Assert.AreEqual(0, animals.LiveCount);
    }

    private static System.Collections.Generic.IEnumerable<ICellEnv> CollectCells(TileMapManager mapManager)
    {
        foreach (TileCell cell in mapManager.Cells)
            yield return cell;
    }

    /// <summary>全图苔藓活力之和。扩张会新增苔藓，因此单株活力不足以反映整体演化。</summary>
    private static int TotalMossVitality(TileMapManager mapManager)
    {
        int total = 0;
        foreach (TileCell cell in mapManager.Cells)
        {
            for (int i = 0; i < cell.Cover.Count; i++)
            {
                Moss moss = cell.Cover[i] as Moss;
                if (moss != null && !moss.IsDead)
                    total += moss.Vitality;
            }
        }

        return total;
    }
}

/// <summary>
/// 从场景里的 WeatherBase 提供植物需要的天气信息。
/// 只读不改 WeatherBase，符合"动物和天气不改动"的约定。
/// </summary>
public sealed class PlayModeWeatherQuery : IWeatherQuery
{
    private readonly WeatherBase source;
    private int consecutiveSameDays = 1;
    private E_WeatherType consecutiveType;

    public PlayModeWeatherQuery(WeatherBase source)
    {
        this.source = source;
        consecutiveType = source != null ? source.weatherType : E_WeatherType.Sun;
    }

    /// <summary>按当天的天气序列推进连续天数。</summary>
    public void Refresh(E_WeatherType[] sequence, int day)
    {
        if (day < 1 || day > sequence.Length)
            return;
        E_WeatherType today = sequence[day - 1];
        if (today == consecutiveType)
            consecutiveSameDays++;
        else
        {
            consecutiveType = today;
            consecutiveSameDays = 1;
        }
    }

    public bool Meets(E_PlantNeed need)
    {
        if (source == null)
            return false;
        bool rainy = source.weatherType == E_WeatherType.Rain;
        switch (need)
        {
            case E_PlantNeed.Light: return !rainy;
            case E_PlantNeed.Heat: return !rainy;
            case E_PlantNeed.Moisture: return rainy;
            case E_PlantNeed.Fungi: return rainy;
            default: return false;
        }
    }

    public int ConsecutiveSameDays
    {
        get { return consecutiveSameDays; }
    }

    public E_WeatherChange Change
    {
        get { return source != null ? source.WeatherChange : E_WeatherChange.None; }
    }
}
