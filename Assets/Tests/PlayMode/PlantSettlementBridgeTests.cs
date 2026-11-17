using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>
/// 验证植物结算真的被运行时接进来了。
/// EditMode 测试只能证明"规则是对的"，这一组证明"点过夜之后规则真的会跑"：
/// 通过 PlantSettlementBridge 订阅 TimeManager.NightSettled，
/// 在 NightSettlement 写好天气历史后结算全图植物，包括天数上限的最后一晚。
/// </summary>
public class PlantSettlementBridgeTests
{
    private const string ScenePath = "Assets/Scenes/TestScene/QH Test.unity";

    [UnitySetUp]
    public IEnumerator LoadScene()
    {
#if UNITY_EDITOR
        // Editor tests can load this scene without changing the player's build scenes.
        yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
            ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
        yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
#endif
        yield return null;
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator UnloadScene()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
            yield break;

        // Unity 不能卸载最后一个场景；保留空场景供后续测试使用。
        if (SceneManager.sceneCount == 1)
            SceneManager.CreateScene("Empty PlayMode Test Scene");
        yield return SceneManager.UnloadSceneAsync(scene);
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

        // 桥接组件在 OnEnable 里自动查找引用并订阅 NightSettled。
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

    [UnityTest]
    public IEnumerator FinalNightSettlesEcologyOnceAndLocksGameplay()
    {
        TimeManager time = Object.FindObjectOfType<TimeManager>();
        NightSettlement night = Object.FindObjectOfType<NightSettlement>();
        PlantSettlementBridge plants = Object.FindObjectOfType<PlantSettlementBridge>();
        AnimalSettlementBridge animals = Object.FindObjectOfType<AnimalSettlementBridge>();
        WeatherBase weather = Object.FindObjectOfType<WeatherBase>();
        TileMapManager map = Object.FindObjectOfType<TileMapManager>();
        SetField(time, "maxDays", 1);
        SetField(night, "settlementDelay", 0f);
        int endings = 0;
        int dayChanges = 0;
        bool finalEcologyReady = false;
        time.DayChanged += day => dayChanges++;
        time.GameSettlementStarted += day =>
        {
            endings++;
            finalEcologyReady = day == 1 && plants.LastSettledDay == 1 && animals.LastSettledDay == 1
                && !time.CanOperate && !time.CanRequestNightSettlement;
        };

        Assert.IsTrue(time.CanOperate, "上限为一天时也应允许完整体验第一天。");
        Assert.IsTrue(time.TryStartNightSettlement());
        Assert.IsFalse(time.TryStartNightSettlement());
        yield return WaitForSettlement(time);
        Assert.IsTrue(finalEcologyReady, "游戏结算接口必须在最终植物和动物结算后调用。");
        Assert.AreEqual(1, endings);
        Assert.AreEqual(0, dayChanges, "最后一晚不再广播下一天或补充库存。");
        Assert.AreEqual(1, time.CurrentDay);
        Assert.AreEqual(DayPhase.GameSettlement, time.Phase);
        Assert.AreEqual(0, time.RemainingActions);
        Assert.AreEqual(1, weather.LastRecordedDay);

        bool actionInvoked = false;
        Assert.IsFalse(time.TryPerformFreeAction(() => { actionInvoked = true; return true; }));
        Assert.IsFalse(actionInvoked);
        Assert.IsFalse(time.TryFinishFreeAction());
        Assert.IsFalse(time.TryStartNightSettlement());
        time.ConfigureDailyActions(100);
        time.enabled = false;
        time.enabled = true;
        Assert.IsTrue(time.IsGameSettlement);
        Assert.IsFalse(time.CanOperate);

        WeatherDropDown weatherControl = Object.FindObjectOfType<WeatherDropDown>();
        TMPro.TMP_Dropdown dropdown = weatherControl.GetComponent<TMPro.TMP_Dropdown>();
        E_WeatherType original = weather.weatherType;
        Assert.IsFalse(dropdown.interactable);
        dropdown.value = (dropdown.value + 1) % dropdown.options.Count;
        Assert.AreEqual(original, weather.weatherType, "直接调用天气控件也不能改变结算结果。");
        NightSettleButton button = Object.FindObjectOfType<NightSettleButton>();
        Assert.IsTrue(button.GetComponent<UnityEngine.UI.Button>().interactable,
            "最终结算后按钮仍可点击，用于显示模拟上限提示。");
        foreach (SpeciesCard card in Object.FindObjectsOfType<SpeciesCard>())
            Assert.IsFalse(card.CanPlace);

        Assert.IsFalse(map.TryInspectAtScreenPosition(Vector2.zero));
        CameraMove cameraMove = Object.FindObjectOfType<CameraMove>();
        Camera_State cameraState = cameraMove.State;
        SetField(cameraMove, "transitionDuration", 0.01f);
        cameraMove.ChangeCameraState();
        Assert.AreNotEqual(cameraState, cameraMove.State, "模拟结束后仍可自由切换相机视角。");
        float transitionDeadline = Time.realtimeSinceStartup + 1f;
        while (cameraMove.IsTransitioning && Time.realtimeSinceStartup < transitionDeadline)
            yield return null;
        Assert.IsFalse(cameraMove.IsTransitioning, "达到天数上限不能阻止相机继续更新。");
        cameraMove.ChangeCameraState();
        Assert.AreEqual(cameraState, cameraMove.State, "可以切回原来的观察模式。");

        AnimalSettleReport finalReport = animals.LastReport;
        yield return null;
        Assert.IsFalse(time.TryStartNightSettlement());
        Assert.AreEqual(1, endings);
        Assert.AreSame(finalReport, animals.LastReport);
        Assert.AreEqual(1, plants.LastSettledDay);
        Assert.AreEqual(1, time.CurrentDay);
    }

    [UnityTest]
    public IEnumerator DayLimitAllowsEveryNightAndDoesNotOpenAnExtraDay()
    {
        TimeManager time = Object.FindObjectOfType<TimeManager>();
        SetField(time, "maxDays", 2);
        SetField(Object.FindObjectOfType<NightSettlement>(), "settlementDelay", 0f);
        int completedNights = 0;
        int endings = 0;
        time.NightSettled += day => { completedNights++; Assert.AreEqual(completedNights, day); };
        time.GameSettlementStarted += day => endings++;
        Assert.IsTrue(time.TryPerformFreeAction(() => true));
        Assert.IsTrue(time.TryStartNightSettlement());
        yield return WaitForSettlement(time);
        Assert.AreEqual(2, time.CurrentDay);
        Assert.IsFalse(time.IsGameSettlement);
        Assert.AreEqual(time.DailyActions, time.RemainingActions);
        Assert.AreEqual(0, endings);

        Assert.IsTrue(time.TryStartNightSettlement());
        yield return WaitForSettlement(time);
        Assert.AreEqual(2, completedNights);
        Assert.AreEqual(2, time.CurrentDay);
        Assert.AreEqual(2, Object.FindObjectOfType<PlantSettlementBridge>().LastSettledDay);
        Assert.AreEqual(2, Object.FindObjectOfType<AnimalSettlementBridge>().LastSettledDay);
        Assert.AreEqual(1, endings);
        Assert.IsTrue(time.IsGameSettlement);
    }

    [UnityTest]
    public IEnumerator InterruptedFinalNightCanRetryWithoutPrematureGameSettlement()
    {
        TimeManager time = Object.FindObjectOfType<TimeManager>();
        NightSettlement night = Object.FindObjectOfType<NightSettlement>();
        SetField(time, "maxDays", 1);
        SetField(night, "settlementDelay", 0f);
        int endings = 0;
        time.GameSettlementStarted += day => endings++;
        Assert.IsTrue(time.TryStartNightSettlement());
        time.enabled = false;
        yield return null;
        Assert.AreEqual(0, endings);
        Assert.AreEqual(0, night.LastSettledDay);
        Assert.IsFalse(time.IsGameSettlement);
        Assert.IsFalse(time.CanOperate);

        time.enabled = true;
        Assert.IsTrue(time.TryStartNightSettlement());
        yield return WaitForSettlement(time);
        Assert.AreEqual(1, endings);
        Assert.AreEqual(1, time.CurrentDay);
        Assert.IsTrue(time.IsGameSettlement);
    }

    private static void SetField(object target, string name, object value)
    {
        target.GetType().GetField(name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(target, value);
    }

    private static IEnumerator WaitForSettlement(TimeManager time)
    {
        float deadline = Time.realtimeSinceStartup + 10f;
        while (time.IsSettlementRunning && Time.realtimeSinceStartup < deadline)
            yield return null;
        yield return null;
    }
}
