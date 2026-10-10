using System;
using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class NightSettlement : MonoBehaviour
{
    [SerializeField, Tooltip("拖入天气对象上的 WeatherBase；应与天气下拉框使用同一个对象。")]
    private WeatherBase weather;
    [SerializeField, Min(0f), Tooltip("过夜展示时长（真实秒数），不受 Time.timeScale 影响。")]
    private float settlementDelay = 0.5f;

    public WeatherBase Weather => weather;
    public bool CanSettle => isActiveAndEnabled && weather != null;
    public int LastSettledDay { get; private set; }
    public string LastError { get; private set; }

    /// <summary>仅处理 TimeManager 从过夜按钮收到的有效请求，不在生命周期或阶段切换时自动结算。</summary>
    internal IEnumerator SettleNight(TimeManager owner, int requestId, int day, E_WeatherType dayWeather)
    {
        if (owner == null || !owner.IsNightSettlementAuthorized(this, requestId, day, dayWeather))
            yield break;

        LastError = null;
        if (day > 0 && day == LastSettledDay)
            yield break;
        if (day < 1 || day < LastSettledDay || !CanSettle)
        {
            LastError = "结算日期无效，或 NightSettlement / Weather 未正确配置。";
            yield break;
        }

        WeatherBase weatherSource = weather;
        if (settlementDelay > 0f)
            yield return new WaitForSecondsRealtime(settlementDelay);

        // 等待期间管理器可能已停用或收到新的请求，旧协程不得继续提交天气和资源变化。
        if (owner == null || !owner.IsNightSettlementAuthorized(this, requestId, day, dayWeather))
            yield break;
        if (day == LastSettledDay)
            yield break;

        try
        {
            if (!isActiveAndEnabled || weatherSource == null)
                throw new InvalidOperationException("过夜期间结算组件被停用，或天气对象已被销毁。");
            if (weatherSource.LastRecordedDay > day
                || (weatherSource.LastRecordedDay == day && weatherSource.LastRecordedWeather != dayWeather))
                throw new InvalidOperationException("天气记录与本次结算日期或天气不一致。");

            // 天气计数先更新，供后续效果读取；重试同一天不会重复累计。
            weatherSource.RecordDayWeather(day, dayWeather);
            ApplyNightEffects(day, dayWeather);
            LastSettledDay = day;
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            Debug.LogException(exception, this);
        }
    }

    /// <summary>扩展点：按顺序加入天气效果、资源和角色状态结算。重复重试时应按 day 防止重复发放。</summary>
    protected virtual void ApplyNightEffects(int day, E_WeatherType dayWeather)
    {
        // 当前项目尚无资源/角色结算逻辑，暂不产生额外游戏效果。
    }
}
