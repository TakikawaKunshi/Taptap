/// <summary>
/// 植物看到的天气。
/// 只暴露"条件是否成立"和连续天数，不暴露天气自身的实现，
/// 因此植物逻辑可以在不启动 WeatherBase 的情况下单独测试。
/// 策划案要求天气只提供机会、不替植物做决定，所以这里没有任何"应该做什么"的接口。
/// </summary>
public interface IWeatherQuery
{
    /// <summary>当天的天气标签是否成立。</summary>
    bool Meets(E_PlantNeed need);

    /// <summary>连续同一天气的天数，第 1 天为 1。</summary>
    int ConsecutiveSameDays { get; }

    /// <summary>昨天到今天的天气变化；首日为 E_WeatherChange.None。</summary>
    E_WeatherChange Change { get; }
}