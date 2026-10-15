using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 动物结算的运行时入口。
///
/// 它订阅植物结算桥接的 Settled 事件而不是 DayChanged：
/// 策划案第七章规定先跑植物（第 3 至 5 步）再跑动物（第 6 至 8 步），
/// 用事件串联可以让顺序明确写在代码里，而不是依赖组件的启用先后。
/// </summary>
public class AnimalSettlementBridge : MonoBehaviour
{
    [Header("引用（留空时自动查找）")]
    [SerializeField, Tooltip("植物结算桥接；动物结算必须排在它之后。")]
    private PlantSettlementBridge plantBridge;
    [SerializeField] private TimeManager timeManager;
    [SerializeField] private WeatherBase weather;
    [SerializeField, Tooltip("拖入挂有 TileMapManager 的环境地图。")]
    private TileMapManager map;
    [SerializeField, Tooltip("拖入投放动物用的 AnimalSpawner；不填则取场景里唯一的一个。")]
    private AnimalSpawner spawner;
    [SerializeField, Tooltip("晨报追加到此面板的 Daily 分类。")]
    private MessagePanel messagePanel;

    [Header("行为")]
    [SerializeField] private bool publishMorningReport = true;
    [SerializeField] private bool verboseLog = true;

    private readonly AnimalSettler settler = new AnimalSettler();
    private PlantSettlementBridge subscribedPlantBridge;
    private int lastSettledDay;

    public AnimalSettleReport LastReport { get; private set; }

    public int LastSettledDay => lastSettledDay;

    /// <summary>界面与晨报可以订阅它，动物状态变化时无需轮询。</summary>
    public event Action<AnimalSettleReport> Settled;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        BindPlantBridge();
    }

    private void OnDisable()
    {
        UnbindPlantBridge();
    }

    private void ResolveReferences()
    {
        if (timeManager == null)
            timeManager = FindObjectOfType<TimeManager>();
        if (weather == null)
            weather = FindObjectOfType<WeatherBase>();
        if (map == null)
            map = FindObjectOfType<TileMapManager>();
        if (messagePanel == null)
            messagePanel = FindObjectOfType<MessagePanel>();
        if (plantBridge == null)
            plantBridge = FindObjectOfType<PlantSettlementBridge>();
        if (spawner == null)
            spawner = FindObjectOfType<AnimalSpawner>();
    }

    /// <summary>
    /// 挂在植物结算的 Settled 事件上，而不是直接订阅 DayChanged。
    /// 策划案第七章规定：先更新植物并处理空间竞争（第 3 至 5 步），
    /// 再更新动物并处理捕食取食（第 6 至 8 步）。
    /// 这样动物看到的已经是植物当晚变化后的世界，两条链路的顺序不会被组件启用顺序影响。
    /// </summary>
    private void BindPlantBridge()
    {
        if (!isActiveAndEnabled || subscribedPlantBridge == plantBridge || plantBridge == null)
            return;

        subscribedPlantBridge = plantBridge;
        subscribedPlantBridge.Settled += OnPlantsSettled;
    }

    private void UnbindPlantBridge()
    {
        if (subscribedPlantBridge != null)
            subscribedPlantBridge.Settled -= OnPlantsSettled;
        subscribedPlantBridge = null;
    }

    private void OnPlantsSettled(PlantSettleReport plantReport)
    {
        // 晚数直接取植物报告上的，而不是靠 TimeManager 反推：
        // 这样"植物先跑、动物紧接着跑同一晚"是写在代码里的事实，不受调用方式影响。
        int day = plantReport != null ? plantReport.Day : 0;
        if (day < 1 || day <= lastSettledDay)
            return;

        WeatherBase source = weather != null ? weather : (map != null ? map.Weather : null);
        if (source == null || !source.HasRecordedWeather || source.LastRecordedDay != day)
        {
            if (verboseLog)
                Debug.LogWarning("AnimalSettlementBridge：第 " + day + " 晚没有可用的天气记录，跳过动物结算。", this);
            return;
        }

        SettleDay(day, source.LastRecordedWeather, source.SameWeatherCount, source.WeatherChange);
    }

    /// <summary>
    /// 结算指定一晚的全部动物。对外暴露是为了让测试、回放工具与调试菜单可以直接驱动。
    /// </summary>
    public AnimalSettleReport SettleDay(int day, E_WeatherType dayWeather,
        int consecutiveSameDays = 1, E_WeatherChange change = E_WeatherChange.None)
    {
        if (map == null || spawner == null)
            ResolveReferences();
        if (map == null || day < 1)
            return null;

        map.InitializeCells();
        if (map.CellCount == 0)
            return null;

        // 同上：动物只在植物之后结算，因此这里也幂等，重复调用不会多发一次。
        if (day <= lastSettledDay)
        {
            if (verboseLog)
                Debug.LogWarning("AnimalSettlementBridge：第 " + day + " 晚的动物已经结算过，跳过。", this);
            return null;
        }

        var condition = new PlantWeatherCondition(dayWeather, consecutiveSameDays, change);
        AnimalSettleReport report = settler.Settle(day, condition, map.Cells, spawner.Animals);
        lastSettledDay = day;
        LastReport = report;
        Publish(report);
        return report;
    }

    private void Publish(AnimalSettleReport report)
    {
        if (report == null)
            return;

        if (publishMorningReport && messagePanel != null)
        {
            string summary = report.BuildSummary();
            if (!string.IsNullOrEmpty(summary))
                messagePanel.ShowMessage(summary, MessageTag.Daily);
        }

        if (Settled != null)
            Settled(report);
    }

    [ContextMenu("清空动物结算记录")]
    public void ResetSettlement()
    {
        lastSettledDay = 0;
    }

    /// <summary>当前登记的动物数量，用于面板提示。</summary>
    public int LiveCount
    {
        get { return spawner != null ? spawner.LiveCount : 0; }
    }
}
