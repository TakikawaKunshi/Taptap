using System.Collections.Generic;
using UnityEngine;

/// <summary>测试用的固定天气。只实现植物需要的三个提问，不依赖 WeatherBase。</summary>
public class FakeWeather : IWeatherQuery
{
    private readonly bool light;
    private readonly bool moisture;
    private readonly bool heat;
    private readonly bool fungi;

    public FakeWeather(E_WeatherType weather, int consecutiveSameDays, E_WeatherChange change)
    {
        light = weather == E_WeatherType.Sun;
        heat = light;
        moisture = weather == E_WeatherType.Rain;
        fungi = moisture;
        ConsecutiveSameDays = consecutiveSameDays;
        Change = change;
    }

    public int ConsecutiveSameDays { get; private set; }

    public E_WeatherChange Change { get; private set; }

    public bool Meets(E_PlantNeed need)
    {
        switch (need)
        {
            case E_PlantNeed.Light: return light;
            case E_PlantNeed.Moisture: return moisture;
            case E_PlantNeed.Heat: return heat;
            case E_PlantNeed.Fungi: return fungi;
            default: return false;
        }
    }

    public static FakeWeather Rain(int consecutive = 1)
    {
        return new FakeWeather(E_WeatherType.Rain, consecutive, E_WeatherChange.None);
    }

    public static FakeWeather Sun(int consecutive = 1)
    {
        return new FakeWeather(E_WeatherType.Sun, consecutive, E_WeatherChange.None);
    }
}