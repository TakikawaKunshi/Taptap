using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 植物结算的运行时入口。
/// 订阅 TimeManager.NightSettled，在天气历史写入后结算植物，再通过 Settled 驱动动物。
/// 生态结算不依赖日期递增，因此达到天数上限的最后一晚也不会遗漏。
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Ecology/Plant Settlement Bridge")]
public class PlantSettlementBridge : MonoBehaviour
{
    [Header("引用（留空时自动查找）")]
    [SerializeField, Tooltip("拖入场景中的 TimeManager；过夜成功后由它推进天数。")]
    private TimeManager timeManager;
    [SerializeField, Tooltip("拖入天气对象上的 WeatherBase，应与天气下拉框使用同一个对象。")]
    private WeatherBase weather;
    [SerializeField, Tooltip("拖入挂有 TileMapManager 的环境地图。")]
    private TileMapManager map;
    [SerializeField, Tooltip("晨报输出到此面板的 DailySV；留空则只发事件不写面板。")]
    private MessagePanel messagePanel;

    [Header("行为")]
    [SerializeField, Tooltip("结算后把晨报写入消息面板的 Daily 分类。")]
    private bool publishMorningReport = true;
    [SerializeField, Tooltip("勾选后可以在 Inspector 里手动重放某一天，用于复现 bug。")]
    private bool verboseLog = true;

    [Header("蘑菇生长")]
    [SerializeField, Min(Mushroom.MinimumMaxVitality), InspectorName("蘑菇活力上限")]
    [Tooltip("最低为初始活力 2。雨夜达到上限的当晚起，上下左右有效邻格各产生 1 份余屑；运行时修改从下次结算生效，调低不会扣除已有活力。")]
    private int mushroomMaxVitality = Mushroom.DefaultMaxVitality;

    public int MushroomMaxVitality => Mathf.Max(Mushroom.MinimumMaxVitality, mushroomMaxVitality);

    private readonly PlantSettler settler = new PlantSettler();
    private TimeManager subscribedTimeManager;
    private int lastSettledDay;

    /// <summary>最近一晚的植物结算报告。</summary>
    public PlantSettleReport LastReport { get; private set; }

    /// <summary>最近一次已经结算完的夜晚；同一天不会重复结算。</summary>
    public int LastSettledDay
    {
        get { return lastSettledDay; }
    }

    /// <summary>界面与晨报可以订阅它；植物状态时无需轮询。</summary>
    public event Action<PlantSettleReport> Settled;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        BindTimeManager();
    }

    private void OnDisable()
    {
        if (subscribedTimeManager != null)
            subscribedTimeManager.NightSettled -= OnNightSettled;
        subscribedTimeManager = null;
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
    }

    private void BindTimeManager()
    {
        if (!isActiveAndEnabled || subscribedTimeManager == timeManager)
            return;
        if (subscribedTimeManager != null)
            subscribedTimeManager.NightSettled -= OnNightSettled;
        subscribedTimeManager = timeManager;
        if (subscribedTimeManager != null)
            subscribedTimeManager.NightSettled += OnNightSettled;
    }

    /// <summary>
    /// 接收刚完成天气记录的夜晚日期，包括不再推进到下一天的最后一晚。
    /// </summary>
    private void OnNightSettled(int day)
    {
        if (day < 1 || day <= lastSettledDay)
            return;
        if (timeManager == null || map == null)
            return;

        WeatherBase source = weather != null ? weather : map.Weather;
        if (source == null || !source.HasRecordedWeather || source.LastRecordedDay != day)
        {
            if (verboseLog)
                Debug.LogWarning("PlantSettlementBridge：第 " + day + " 晚没有可用的天气记录，跳过植物结算。", this);
            return;
        }

        SettleDay(day, source.LastRecordedWeather, source.SameWeatherCount, source.WeatherChange);
    }

    /// <summary>
    /// 结算指定一晚的全部植物。对外暴露是为了让 PlayMode 测试、回放工具
    /// 和"重放某一天"的调试菜单都能直接驱动，不必依赖 TimeManager 的协程。
    /// </summary>
    public PlantSettleReport SettleDay(int day, E_WeatherType dayWeather,
        int consecutiveSameDays = 1, E_WeatherChange change = E_WeatherChange.None)
    {
        if (map == null)
            ResolveReferences();
        if (map == null || day < 1)
            return null;

        // 幂等：同一晚只允许结算一次。
        // 过夜按钮被连点、或 NightSettled 在同一天上被重复派发时，植物不能因此多发一次成长。
        // 需要重放某一天时先调用 ResetSettlement 清掉记录。
        if (day <= lastSettledDay)
        {
            if (verboseLog)
                Debug.LogWarning("PlantSettlementBridge：第 " + day + " 晚已结算过（最近结算到第 "
                                 + lastSettledDay + " 晚），跳过以避免重复发放。", this);
            return null;
        }

        map.InitializeCells();
        if (map.CellCount == 0)
            return null;

        var condition = new PlantWeatherCondition(dayWeather, consecutiveSameDays, change);
        settler.MushroomMaxVitality = MushroomMaxVitality;
        PlantSettleReport report = settler.Settle(day, condition, map.Cells);
        lastSettledDay = day;
        LastReport = report;
        Publish(report);
        return report;
    }

    /// <summary>对任意格子集合结算，便于局部刷新与测试。</summary>
    public PlantSettleReport SettleCells(int day, PlantWeatherCondition condition, IEnumerable<ICellEnv> cells)
    {
        if (day < 1 || cells == null)
            return null;

        settler.MushroomMaxVitality = MushroomMaxVitality;
        PlantSettleReport report = settler.Settle(day, condition, cells);
        LastReport = report;
        Publish(report);
        return report;
    }

    private void Publish(PlantSettleReport report)
    {
        if (report == null)
            return;

        string summary = report.BuildSummary();
        if (verboseLog)
            Debug.Log("PlantSettlementBridge：\n" + summary, this);
        if (publishMorningReport && messagePanel != null)
            messagePanel.ShowMessage(summary, MessageTag.Daily);

        Settled?.Invoke(report);
    }

    /// <summary>清空地图上的植物与该 Unit 的结算记录，用于反复验证七日流程。</summary>
    [ContextMenu("清空结算记录")]
    public void ResetSettlement()
    {
        lastSettledDay = 0;
        LastReport = null;
    }
}
