using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 植物结算的运行时入口。
/// 之前整套植物规则写好了却没有被任何运行流程调用，只有测试在用。
/// 这里用"订阅 TimeManager.DayChanged"的方式接入：
/// 过夜协程里 NightSettlement 先写入天气历史、TimeManager 再推进天数并广播 DayChanged，
/// 因此收到事件时 LastRecordedDay 就是刚刚结算完的那一晚，不需要改动
/// NightSettlement / TimeManager / WeatherBase / TileMapManager 的任何一行。
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
            subscribedTimeManager.DayChanged -= OnDayChanged;
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
            subscribedTimeManager.DayChanged -= OnDayChanged;
        subscribedTimeManager = timeManager;
        if (subscribedTimeManager != null)
            subscribedTimeManager.DayChanged += OnDayChanged;
    }

    /// <summary>
    /// TimeManager 在过夜成功后才推进天数，所以 newDay-1 就是刚结算完的那一晚。
    /// </summary>
    private void OnDayChanged(int newDay)
    {
        int day = newDay - 1;
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
        // 过夜按钮被连点、或 DayChanged 在同一天上被重复派发时，植物不能因此多发一次成长。
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
