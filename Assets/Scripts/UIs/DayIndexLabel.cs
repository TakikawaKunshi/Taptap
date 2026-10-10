using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(TextMeshProUGUI))]
public class DayIndexLabel : MonoBehaviour
{
    [SerializeField, Tooltip("自动获取本物体上的 TMP 文字组件。")]
    private TMP_Text label;
    [SerializeField, Tooltip("拖入场景中的 TimeManager，读取当前天数并监听过夜后的日期变化。")]
    private TimeManager timeManager;
    [SerializeField, Tooltip("天数前的文字；前后缀均留空时只显示数字。")]
    private string prefix = "第 ";
    [SerializeField, Tooltip("天数后的文字。")]
    private string suffix = " 天";

    private TimeManager subscribedTimeManager;
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
        // 等待所有 Awake 完成后同步，兼容 TimeManager 设置了非第 1 天开局。
        RefreshDisplay();
        if (timeManager == null)
            Debug.LogWarning("DayIndexLabel：请在 Inspector 中绑定 TimeManager。", this);
    }

    private void OnDisable()
    {
        if (subscribedTimeManager != null)
            subscribedTimeManager.DayChanged -= OnDayChanged;
        subscribedTimeManager = null;
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

    private void OnDayChanged(int day)
    {
        RefreshDisplay();
    }

    /// <summary>显示 TimeManager 的当前天数；天数递增由 TimeManager 在过夜成功后统一处理。</summary>
    public void RefreshDisplay()
    {
        refreshRequested = false;
        if (label == null)
            label = GetComponent<TMP_Text>();

        if (isActiveAndEnabled && subscribedTimeManager != timeManager)
        {
            if (subscribedTimeManager != null)
                subscribedTimeManager.DayChanged -= OnDayChanged;
            subscribedTimeManager = timeManager;
            if (subscribedTimeManager != null)
                subscribedTimeManager.DayChanged += OnDayChanged;
        }

        if (label != null)
            label.text = prefix + (timeManager != null ? timeManager.CurrentDay.ToString() : "--") + suffix;
    }
}
