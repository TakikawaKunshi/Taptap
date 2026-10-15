using UnityEngine;

/// <summary>
/// 天气到植物条件的固定映射。
/// 策划案要求天气只提供机会、不替植物做决定，所以这里只回答"某条需要是否成立"。
/// 做成不依赖 MonoBehaviour 的纯值对象，是为了让整套植物规则能在 EditMode 下跑;
/// 运行时的 <see cref="PlantWeatherConditionExtensions"/> 负责从 WeatherBase 取值，
/// 因此不需要改动天气侧的任何一个脚本。
/// </summary>
public sealed class PlantWeatherCondition : IWeatherQuery
{
    public PlantWeatherCondition(E_WeatherType weather,
        int consecutiveSameDays = 1,
        E_WeatherChange change = E_WeatherChange.None)
    {
        Weather = weather;
        ConsecutiveSameDays = Mathf.Max(1, consecutiveSameDays);
        Change = change;
    }

    /// <summary>当天的天气。</summary>
    public E_WeatherType Weather { get; }

    /// <summary>连续同一天气的天数，第 1 天为 1。</summary>
    public int ConsecutiveSameDays { get; }

    /// <summary>昨天到今天的天气变化；首日为 E_WeatherChange.None。</summary>
    public E_WeatherChange Change { get; }

    public bool Meets(E_PlantNeed need)
    {
        return MeetsNeed(Weather, need);
    }

    /// <summary>策划案第四章的标签映射：晴＝光照与热，雨＝潮湿与繁菌。</summary>
    public static bool MeetsNeed(E_WeatherType weather, E_PlantNeed need)
    {
        switch (need)
        {
            case E_PlantNeed.Light: return weather == E_WeatherType.Sun;
            case E_PlantNeed.Heat: return weather == E_WeatherType.Sun;
            case E_PlantNeed.Moisture: return weather == E_WeatherType.Rain;
            case E_PlantNeed.Fungi: return weather == E_WeatherType.Rain;
            default: return false;
        }
    }

    public override string ToString()
    {
        return Weather + " x" + ConsecutiveSameDays + " " + Change;
    }
}

/// <summary>把 WeatherBase 的只读历史翻译成 <see cref="PlantWeatherCondition"/>。</summary>
public static class PlantWeatherConditionExtensions
{
    /// <summary>
    /// 取某一晚结算完成后的天气快照。
    /// 过夜流程里 NightSettlement 会先 RecordDayWeather 再进入下一天，
    /// 因此订阅 DayChanged 时一定能读到已经写好的 LastRecordedDay / LastRecordedWeather。
    /// </summary>
    public static PlantWeatherCondition ToPlantCondition(this WeatherBase source, E_WeatherType fallback)
    {
        if (source == null || !source.HasRecordedWeather)
            return new PlantWeatherCondition(fallback);

        return new PlantWeatherCondition(
            source.LastRecordedWeather,
            source.SameWeatherCount,
            source.WeatherChange);
    }

    public static PlantWeatherCondition ToPlantCondition(this WeatherBase source)
    {
        return source == null
            ? new PlantWeatherCondition(E_WeatherType.Sun)
            : ToPlantCondition(source, source.weatherType);
    }
}
