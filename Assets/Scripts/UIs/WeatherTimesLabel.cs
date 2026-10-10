using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(TextMeshProUGUI))]
public class WeatherTimesLabel : MonoBehaviour
{
    [SerializeField, Tooltip("自动获取本物体上的 TMP 文字组件。")]
    private TMP_Text label;
    [SerializeField, Tooltip("拖入天气下拉框上的 WeatherDropDown，读取其剩余切换点数。")]
    private WeatherDropDown weatherDropDown;
    [SerializeField, Tooltip("显示在点数前的文字；留空则只显示数字。")]
    private string prefix = "切换天气点数：";

    private WeatherDropDown subscribedWeatherDropDown;
    private bool refreshRequested;

    private void Reset()
    {
        label = GetComponent<TMP_Text>();
    }

    private void OnEnable()
    {
        RefreshDisplay();
    }

    private void Start()
    {
        RefreshDisplay();
        if (weatherDropDown == null)
            Debug.LogWarning("WeatherTimesLabel：请绑定天气菜单上的 WeatherDropDown。", this);
    }

    private void OnDisable()
    {
        if (subscribedWeatherDropDown != null)
            subscribedWeatherDropDown.WeatherChangePointsChanged -= OnPointsChanged;
        subscribedWeatherDropDown = null;
    }

    private void OnValidate()
    {
        refreshRequested = true;
    }

    private void Update()
    {
        if (refreshRequested)
            RefreshDisplay();
    }

    private void OnPointsChanged(int points)
    {
        RefreshDisplay();
    }

    /// <summary>同步点数文字；不修改点数，也不修改天气。</summary>
    public void RefreshDisplay()
    {
        refreshRequested = false;
        if (label == null)
            label = GetComponent<TMP_Text>();

        if (isActiveAndEnabled && subscribedWeatherDropDown != weatherDropDown)
        {
            if (subscribedWeatherDropDown != null)
                subscribedWeatherDropDown.WeatherChangePointsChanged -= OnPointsChanged;
            subscribedWeatherDropDown = weatherDropDown;
            if (subscribedWeatherDropDown != null)
                subscribedWeatherDropDown.WeatherChangePointsChanged += OnPointsChanged;
        }

        if (label != null)
            label.text = prefix + (weatherDropDown != null ? weatherDropDown.WeatherChangePoints.ToString() : "--");
    }
}
