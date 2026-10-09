using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum E_WeatherType
{
    Sun,
    Rain,
}


public class WeatherBase : MonoBehaviour
{
    public E_WeatherType weatherType;
    private int sameWeatherCount = 0;
    private E_WeatherType lastWeatherType;//记录上一次的天气类型



    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public bool IsSameWeather()
    {
        if (lastWeatherType == weatherType)
        {
            sameWeatherCount++;
            return true;
        }
        else
        {
            lastWeatherType = weatherType;
            sameWeatherCount = 1;
            return false;
        }
    }

    public int SameWeatherCount
    {
        get
        {
            return sameWeatherCount;
        }
    }
}
