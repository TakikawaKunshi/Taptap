using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>
/// 验证植物结算真的被运行时接进来了。
/// EditMode 测试只能证明"规则是对的"，这一组证明"点过夜之后规则真的会跑"：
/// 通过 PlantSettlementBridge 订阅 TimeManager.DayChanged，
/// 在 NightSettlement 写好天气历史、天数推进之后结算全图植物，并输出晨报。
/// </summary>
public class PlantSettlementBridgeTests
{
    private const string ScenePath = "Assets/Scenes/TestScene/QH Test.unity";

    [UnitySetUp]
    public IEnumerator LoadScene()
    {
        yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
        yield return null;
        yield return null;
    }

    [UnityTest]
    public IEnumerator 点击过夜之后植物真的会结算()
    {
        TileMapManager map = Object.FindObjectOfType<TileMapManager>();
        TimeManager time = Object.FindObjectOfType<TimeManager>();
        WeatherBase weather = Object.FindObjectOfType<WeatherBase>();
        PlantSpawner spawner = Object.FindObjectOfType<PlantSpawner>();

        Assert.IsNotNull(map, "场景里必须有 TileMapManager");
        Assert.IsNotNull(time, "场景里必须有 TimeManager");
        Assert.IsNotNull(weather, "场景里必须有 WeatherBase");

        Assert.Greater(spawner.LastPlacedCount, 0, "开局应自动铺设植物");

        // 桥接组件在 OnEnable 里自动查找引用并订阅 DayChanged。
        var host = new GameObject("Plant Settlement Bridge");
        PlantSettlementBridge bridge = host.AddComponent<PlantSettlementBridge>();
        bridge.ResetSettlement();

        weather.weatherType = E_WeatherType.Rain;
        map.RefreshWeather();
        yield return null;

        Assert.IsTrue(time.TryStartNightSettlement(), "过夜请求应当被接受");
        yield return WaitForSettlement(time);

        Assert.AreEqual(2, time.CurrentDay, "过夜成功后应进入第 2 天");
        Assert.AreEqual(1, bridge.LastSettledDay, "桥接组件应完成第 1 晚的植物结算");
        Assert.IsNotNull(bridge.LastReport);
        Assert.Greater(bridge.LastReport.SettledCount, 0, "地图上铺了植物，就不该结算 0 株");

        Object.Destroy(host);
    }

    [UnityTest]
    public IEnumerator 过夜结果会写成晨报()
    {
        PlantSpawner spawner = Object.FindObjectOfType<PlantSpawner>();
        spawner.SpawnInitialLayout();

        var host = new GameObject("Plant Settlement Bridge");
        PlantSettlementBridge bridge = host.AddComponent<PlantSettlementBridge>();
        string captured = null;
        bridge.Settled += report => captured = report.BuildSummary();

        WeatherBase weather = Object.FindObjectOfType<WeatherBase>();
        weather.weatherType = E_WeatherType.Rain;
        Object.FindObjectOfType<TileMapManager>().RefreshWeather();
        yield return null;

        PlantSettleReport report = bridge.SettleDay(1, E_WeatherType.Rain);
        Assert.IsNotNull(report);

        string summary = report.BuildSummary();
        Assert.IsNotEmpty(summary, "策划案要求生成可解释的晨报");
        Assert.AreEqual(summary, captured, "Settled 事件应带出同一份摘要，供 UI 订阅");

        Object.Destroy(host);
    }

    [UnityTest]
    public IEnumerator 同一晚重复结算不会重复发放()
    {
        PlantSpawner spawner = Object.FindObjectOfType<PlantSpawner>();
        spawner.SpawnInitialLayout();

        var host = new GameObject("Plant Settlement Bridge");
        PlantSettlementBridge bridge = host.AddComponent<PlantSettlementBridge>();

        WeatherBase weather = Object.FindObjectOfType<WeatherBase>();
        weather.weatherType = E_WeatherType.Rain;
        Object.FindObjectOfType<TileMapManager>().RefreshWeather();
        yield return null;

        PlantSettleReport first = bridge.SettleDay(1, E_WeatherType.Rain);
        int settledFirst = first.SettledCount;
        int growthFirst = first.NewGrowths.Count;

        // 重复驱动同一晚应被直接跳过（返回 null），而不是再发一次成长。
        PlantSettleReport second = bridge.SettleDay(1, E_WeatherType.Rain);
        Assert.IsNull(second, "策划案：重试过夜不能重复发放");
        Assert.AreEqual(settledFirst, first.SettledCount);
        Assert.AreEqual(growthFirst, first.NewGrowths.Count);

        // 需要重放某一天时，先清空记录再结算，这时应当能重新结算出来。
        bridge.ResetSettlement();
        PlantSettleReport replay = bridge.SettleDay(1, E_WeatherType.Rain);
        Assert.IsNotNull(replay, "清空结算记录后应能重放同一晚");

        Object.Destroy(host);
    }

    private static IEnumerator WaitForSettlement(TimeManager time)
    {
        float deadline = Time.realtimeSinceStartup + 10f;
        while (time.IsSettlementRunning && Time.realtimeSinceStartup < deadline)
            yield return null;
        yield return null;
    }
}
