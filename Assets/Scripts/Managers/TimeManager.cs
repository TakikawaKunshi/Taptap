using System;
using System.Collections;
using UnityEngine;

public enum DayPhase
{
    FirstFreeAction,
    SecondFreeAction,
    NightSettlement,
    WaitingForNightSettlement
}

[DisallowMultipleComponent]
public class TimeManager : MonoBehaviour
{
    [SerializeField, Min(1), Tooltip("新游戏从第几天开始，默认第 1 天。")]
    private int startingDay = 1;
    [SerializeField, Tooltip("负责过夜结算的组件；不指定时尝试获取本物体上的 NightSettlement。")]
    private NightSettlement nightSettlement;

    public int CurrentDay { get; private set; } = 1;
    public DayPhase Phase { get; private set; } = DayPhase.FirstFreeAction;
    public bool IsNightSettlement => Phase == DayPhase.NightSettlement;
    public bool CanOperate => isActiveAndEnabled
        && (Phase == DayPhase.FirstFreeAction || Phase == DayPhase.SecondFreeAction);
    public bool IsSettlementRunning => settlementRoutine != null;
    public bool CanRequestNightSettlement => isActiveAndEnabled && !IsSettlementRunning
        && nightSettlement != null && nightSettlement.CanSettle;
    public string LastSettlementError { get; private set; }
    public int RemainingActions => Phase == DayPhase.FirstFreeAction ? 2
        : Phase == DayPhase.SecondFreeAction ? 1 : 0;

    public event Action<int> DayChanged;
    public event Action<DayPhase> PhaseChanged;
    // 面板和输入控件订阅此事件，也能响应停用、恢复和结算失败。
    public event Action StateChanged;

    private Coroutine settlementRoutine;
    private int pendingNightDay;
    private E_WeatherType pendingNightWeather;
    private int lastAdvanceFrame = -1;
    private int lastNightRequestFrame = -1;
    private int settlementRequestId;
    private bool advancing;

    private void Awake()
    {
        CurrentDay = Mathf.Max(1, startingDay);
        if (nightSettlement == null)
            nightSettlement = GetComponent<NightSettlement>();
    }

    private void OnEnable()
    {
        // 重新启用只刷新 UI；中断的过夜也必须再次点击按钮才会继续。
        StateChanged?.Invoke();
    }

    private void Start()
    {
        DayChanged?.Invoke(CurrentDay);
        PhaseChanged?.Invoke(Phase);
        StateChanged?.Invoke();
    }

    private void OnDisable()
    {
        settlementRequestId++;
        if (settlementRoutine != null)
        {
            StopCoroutine(settlementRoutine);
            LastSettlementError = "结算已中断，请点击过夜结算按钮继续。";
        }
        settlementRoutine = null;
        StateChanged?.Invoke();
    }

    /// <summary>结束一次自由操作；同帧重复调用、过夜中或停用时返回 false。</summary>
    public bool TryFinishFreeAction()
    {
        if (!CanOperate || advancing || lastAdvanceFrame == Time.frameCount)
            return false;
        advancing = true;
        try
        {
            lastAdvanceFrame = Time.frameCount;
            if (Phase == DayPhase.FirstFreeAction)
            {
                Phase = DayPhase.SecondFreeAction;
            }
            else
            {
                // 两次操作耗尽仅进入等待状态，不自动执行过夜。
                Phase = DayPhase.WaitingForNightSettlement;
            }
            PhaseChanged?.Invoke(Phase);
            StateChanged?.Invoke();
            return true;
        }
        finally
        {
            advancing = false;
        }
    }

    /// <summary>由过夜按钮点击调用。允许直接结束当天；中断或失败后重试保留原结算日期和天气。</summary>
    public bool TryStartNightSettlement()
    {
        if (!isActiveAndEnabled || IsSettlementRunning || advancing || lastNightRequestFrame == Time.frameCount)
            return false;
        if (!HasSettlementConfiguration())
            return false;

        advancing = true;
        try
        {
            bool phaseChanged = !IsNightSettlement;
            if (phaseChanged)
            {
                // 只有点击时才固定当天的天气；重试不覆盖之前的快照。
                pendingNightDay = CurrentDay;
                pendingNightWeather = nightSettlement.Weather.weatherType;
            }
            lastNightRequestFrame = Time.frameCount;
            LastSettlementError = null;
            Phase = DayPhase.NightSettlement;
            int requestId = ++settlementRequestId;
            settlementRoutine = StartCoroutine(FinishNight(requestId));
            if (phaseChanged)
                PhaseChanged?.Invoke(Phase);
            StateChanged?.Invoke();
            return true;
        }
        finally
        {
            advancing = false;
        }
    }

    private bool HasSettlementConfiguration()
    {
        if (nightSettlement != null && nightSettlement.CanSettle)
            return true;
        Debug.LogWarning("TimeManager：请配置已启用的 NightSettlement 及其 Weather 引用。", this);
        return false;
    }

    /// <summary>NightSettlement 仅接受当前按钮请求；失效请求不能在恢复组件后继续写入结算结果。</summary>
    internal bool IsNightSettlementAuthorized(NightSettlement source, int requestId, int day, E_WeatherType weather)
    {
        return isActiveAndEnabled && IsNightSettlement && IsSettlementRunning
            && source == nightSettlement && requestId == settlementRequestId
            && day == CurrentDay && day == pendingNightDay && weather == pendingNightWeather;
    }

    private IEnumerator FinishNight(int requestId)
    {
        // 至少跨一帧，确保输入锁定生效，也让协程句柄先完成赋值。
        yield return null;
        if (nightSettlement != null && nightSettlement.CanSettle)
            yield return nightSettlement.SettleNight(this, requestId, pendingNightDay, pendingNightWeather);

        if (requestId != settlementRequestId || !isActiveAndEnabled)
            yield break;

        settlementRoutine = null;
        if (nightSettlement == null || nightSettlement.LastSettledDay != pendingNightDay)
        {
            LastSettlementError = nightSettlement != null && !string.IsNullOrEmpty(nightSettlement.LastError)
                ? nightSettlement.LastError : "结算未完成，请检查 NightSettlement 和 Weather 引用。";
            Debug.LogWarning("TimeManager：" + LastSettlementError, this);
            StateChanged?.Invoke();
            yield break;
        }

        advancing = true;
        try
        {
            // 只有过夜成功才进入下一天，发布事件时所有状态都已更新。
            CurrentDay = pendingNightDay + 1;
            Phase = DayPhase.FirstFreeAction;
            LastSettlementError = null;
            lastAdvanceFrame = Time.frameCount;
            lastNightRequestFrame = Time.frameCount;
            DayChanged?.Invoke(CurrentDay);
            PhaseChanged?.Invoke(Phase);
            StateChanged?.Invoke();
        }
        finally
        {
            advancing = false;
        }
    }
}
