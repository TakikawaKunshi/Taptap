using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>
/// 在真实的 QH Test 场景里验证动物链路。
/// 与 EditMode 测试的区别：这里走真实的 TileMapManager、真实的邻接与真实的世界视图，
/// 因此能确认动物真的落到了地图上、真的会动、真的会吃、真的会饿死。
/// </summary>
public class AnimalSettlementSceneTests
{
    private const string ScenePath = "Assets/Scenes/TestScene/QH Test.unity";

    [UnitySetUp]
    public IEnumerator LoadScene()
    {
        yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
        yield return null;
        yield return null;
        WeatherBase weather = Object.FindObjectOfType<WeatherBase>();
        if (weather != null)
            weather.weatherType = E_WeatherType.Rain;
        yield return null;
        yield return null;
    }

    [Test]
    public void 场景的动物接线齐全()
    {
        AnimalSpawner spawner = Object.FindObjectOfType<AnimalSpawner>();
        AnimalSettlementBridge bridge = Object.FindObjectOfType<AnimalSettlementBridge>();

        Assert.IsNotNull(spawner, "场景里必须有 AnimalSpawner");
        Assert.IsNotNull(spawner.Map, "AnimalSpawner 必须绑定 TileMapManager");
        Assert.IsNotNull(bridge, "场景里必须有 AnimalSettlementBridge");
    }

    [Test]
    public void 铺设初始动物后五种动物都在地图上()
    {
        AnimalSpawner spawner = Object.FindObjectOfType<AnimalSpawner>();
        spawner.SpawnInitialLayout();

        // 策划案第八章：青蛙、蚯蚓、蜗牛、蚂蚁、蜘蛛各一只，初始饱腹 2。
        Assert.AreEqual(5, spawner.LiveCount, "初始配置应当投放 5 只动物");

        int frogs = 0, worms = 0, snails = 0, ants = 0, spiders = 0;
        foreach (AnimalsBase animal in spawner.Animals)
        {
            Assert.IsNotNull(animal.CurrentCell, animal.DisplayName + " 必须落到具体的格子上");
            Assert.AreEqual(2, animal.Satiety, animal.DisplayName + " 的初始饱腹应为 2");
            switch (animal.Species)
            {
                case E_AnimalSpecies.Frog: frogs++; break;
                case E_AnimalSpecies.Earthworm: worms++; break;
                case E_AnimalSpecies.Snail: snails++; break;
                case E_AnimalSpecies.Ant: ants++; break;
                case E_AnimalSpecies.Spider: spiders++; break;
            }
        }

        Assert.AreEqual(1, frogs, "应有一只青蛙");
        Assert.AreEqual(1, worms, "应有一只蚯蚓");
        Assert.AreEqual(1, snails, "应有一只蜗牛");
        Assert.AreEqual(1, ants, "应有一只蚂蚁");
        Assert.AreEqual(1, spiders, "应有一只蜘蛛");
    }

    [Test]
    public void 过夜后动物真的会发生活动()
    {
        PlantSpawner plants = Object.FindObjectOfType<PlantSpawner>();
        AnimalSpawner spawner = Object.FindObjectOfType<AnimalSpawner>();
        AnimalSettlementBridge bridge = Object.FindObjectOfType<AnimalSettlementBridge>();

        plants.SpawnInitialLayout();
        spawner.SpawnInitialLayout();

        AnimalSettleReport report = bridge.SettleDay(1, E_WeatherType.Rain);
        Assert.IsNotNull(report, "第一晚应当能正常结算");
        Assert.AreEqual(5, report.SettledCount, "5 只动物都应当参与结算");
        Assert.IsNotEmpty(report.BuildSummary(), "策划案要求晨报能解释发生了什么");
    }

    [UnityTest]
    public IEnumerator 过夜后植物与动物按策划案顺序结算同一晚()
    {
        TileMapManager map = Object.FindObjectOfType<TileMapManager>();
        TimeManager time = Object.FindObjectOfType<TimeManager>();
        WeatherBase weather = Object.FindObjectOfType<WeatherBase>();
        PlantSpawner plants = Object.FindObjectOfType<PlantSpawner>();
        AnimalSpawner animals = Object.FindObjectOfType<AnimalSpawner>();
        PlantSettlementBridge plantBridge = Object.FindObjectOfType<PlantSettlementBridge>();
        AnimalSettlementBridge animalBridge = Object.FindObjectOfType<AnimalSettlementBridge>();

        Assert.IsNotNull(plantBridge, "场景里必须有植物结算桥接");
        Assert.IsNotNull(animalBridge, "场景里必须有动物结算桥接");

        plants.SpawnInitialLayout();
        animals.SpawnInitialLayout();
        plantBridge.ResetSettlement();
        animalBridge.ResetSettlement();

        weather.weatherType = E_WeatherType.Rain;
        map.RefreshWeather();
        yield return null;

        Assert.IsTrue(time.TryStartNightSettlement(), "过夜请求应当被接受");
        yield return WaitForSettlement(time);

        // 策划案第七章：先跑植物（第 3 至 5 步），再跑动物（第 6 至 8 步），且必须是同一晚。
        Assert.AreEqual(2, time.CurrentDay, "过夜成功后应进入第 2 天");
        Assert.AreEqual(1, plantBridge.LastSettledDay, "植物应当先完成第 1 晚的结算");
        Assert.AreEqual(1, animalBridge.LastSettledDay, "动物应当紧接着结算同一晚");
        Assert.IsNotNull(animalBridge.LastReport, "动物结算应当产出可解释的报告");
    }

    [Test]
    public void 过夜后至少有一只动物真的换了格子()
    {
        PlantSpawner plants = Object.FindObjectOfType<PlantSpawner>();
        AnimalSpawner spawner = Object.FindObjectOfType<AnimalSpawner>();
        AnimalSettlementBridge bridge = Object.FindObjectOfType<AnimalSettlementBridge>();

        plants.SpawnInitialLayout();
        spawner.SpawnInitialLayout();

        AnimalSettleReport report = bridge.SettleDay(1, E_WeatherType.Rain);
        Assert.IsNotNull(report);
        Assert.Greater(report.MovedCount, 0,
            "地图上应当真的有动物换过格子，否则晨报里的“移动”只是记了个数字");

        // 移动记录应当带上起点与终点，玩家才能在晨报里核对。
        bool loggedMove = false;
        for (int i = 0; i < report.Logs.Count; i++)
        {
            if (report.Logs[i].RuleId.EndsWith(".MOVE"))
            {
                loggedMove = true;
                Assert.IsTrue(report.Logs[i].Text.Contains("移动到"),
                    "位移日志要写明从哪一格到哪一格");
            }
        }

        Assert.IsTrue(loggedMove, "每次位移都应当在晨报里留下一条带坐标的记录");
    }

    private static IEnumerator WaitForSettlement(TimeManager time)
    {
        float deadline = Time.realtimeSinceStartup + 10f;
        while (time.IsSettlementRunning && Time.realtimeSinceStartup < deadline)
            yield return null;
        yield return null;
    }

    [Test]
    public void 重复结算同一晚不会让动物二次行动()
    {
        PlantSpawner plants = Object.FindObjectOfType<PlantSpawner>();
        AnimalSpawner spawner = Object.FindObjectOfType<AnimalSpawner>();
        AnimalSettlementBridge bridge = Object.FindObjectOfType<AnimalSettlementBridge>();

        plants.SpawnInitialLayout();
        spawner.SpawnInitialLayout();

        Assert.IsNotNull(bridge.SettleDay(1, E_WeatherType.Rain));
        Assert.IsNull(bridge.SettleDay(1, E_WeatherType.Rain),
            "同一晚重复结算应当被跳过，否则动物会被重复扣饱腹");
    }

    [Test]
    public void 清空动物后回到开局状态()
    {
        AnimalSpawner spawner = Object.FindObjectOfType<AnimalSpawner>();
        spawner.SpawnInitialLayout();
        Assert.Greater(spawner.LiveCount, 0);

        spawner.ClearAllAnimals();
        Assert.AreEqual(0, spawner.LiveCount, "清空后不应再有动物");
    }
}
