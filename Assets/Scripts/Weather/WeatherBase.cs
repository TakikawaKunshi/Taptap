using System;
using UnityEngine;

public enum E_WeatherType
{
    Sun,
    Rain
}

public enum E_WeatherChange
{
    None,       // 尚未结算，或没有连续的昨日记录。
    SunToSun,   // 昨天晴，今天晴。
    SunToRain,  // 昨天晴，今天雨。
    RainToSun,  // 昨天雨，今天晴。
    RainToRain  // 昨天雨，今天雨。
}

[DisallowMultipleComponent]
public class WeatherBase : MonoBehaviour
{
    [Tooltip("当前选择的天气；只有过夜结算才会记录为当天的天气。")]
    public E_WeatherType weatherType;

    public int SameWeatherCount { get; private set; }
    public int LastRecordedDay { get; private set; }
    public E_WeatherType LastRecordedWeather { get; private set; }
    public bool HasRecordedWeather => LastRecordedDay > 0;

    /// <summary>最近记录日相对其前一天的天气变化；以 LastRecordedDay 为准，在过夜记录时更新。</summary>
    public E_WeatherChange WeatherChange { get; private set; } = E_WeatherChange.None;

    /// <summary>当天记录、连续天气计数和 WeatherChange 全部更新后触发；首日也会触发。</summary>
    public event Action WeatherHistoryChanged;

    /// <summary>记录指定日期的天气。重复/过期日期返回 false，不重复累计，也不覆盖当前选择。</summary>
    public bool RecordDayWeather(int day, E_WeatherType dayWeather)
    {
        if (day < 1)
            throw new ArgumentOutOfRangeException(nameof(day), "天数必须从 1 开始。");
        if (!Enum.IsDefined(typeof(E_WeatherType), dayWeather))
            throw new ArgumentOutOfRangeException(nameof(dayWeather));
        if (day <= LastRecordedDay)
            return false;

        bool hasYesterday = HasRecordedWeather && day == LastRecordedDay + 1;
        E_WeatherChange change = hasYesterday
            ? GetWeatherChange(LastRecordedWeather, dayWeather)
            : E_WeatherChange.None;
        bool consecutive = hasYesterday && dayWeather == LastRecordedWeather;
        SameWeatherCount = consecutive ? SameWeatherCount + 1 : 1;
        LastRecordedDay = day;
        LastRecordedWeather = dayWeather;
        WeatherChange = change;
        WeatherHistoryChanged?.Invoke();
        return true;
    }

    public bool RecordDayWeather(int day) => RecordDayWeather(day, weatherType);

    private static E_WeatherChange GetWeatherChange(E_WeatherType yesterday, E_WeatherType today)
    {
        return (yesterday, today) switch
        {
            (E_WeatherType.Sun, E_WeatherType.Sun) => E_WeatherChange.SunToSun,
            (E_WeatherType.Sun, E_WeatherType.Rain) => E_WeatherChange.SunToRain,
            (E_WeatherType.Rain, E_WeatherType.Sun) => E_WeatherChange.RainToSun,
            (E_WeatherType.Rain, E_WeatherType.Rain) => E_WeatherChange.RainToRain,
            _ => throw new ArgumentOutOfRangeException(nameof(today), "尚未定义该天气组合对应的变化类型。")
        };
    }

    /// <summary>仅查询当前选择是否与上一次记录的天气相同；此方法不再修改连续天气计数。</summary>
    public bool IsSameWeather() => HasRecordedWeather && weatherType == LastRecordedWeather;
}
