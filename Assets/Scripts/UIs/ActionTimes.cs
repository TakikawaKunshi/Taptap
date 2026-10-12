using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>配置每日行动力，并显示 TimeManager 中的实际剩余行动力。</summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class ActionTimes : MonoBehaviour
{
    [Header("文字组件（自动获取本物体上的 Text 或 TMP）")]
    [SerializeField] private Text label;
    [SerializeField] private TMP_Text tmpLabel;

    [Header("行动力")]
    [SerializeField, Tooltip("与生物投放匣使用同一个 TimeManager；留空时自动查找。")]
    private TimeManager timeManager;
    [SerializeField, Min(0), Tooltip("所有生物共用的每日投放次数；成功过夜后恢复此额度。运行时修改会保留当天已消耗次数。")]
    private int dailyActions = 2;
    [SerializeField] private string prefix = "行动力：";
    [SerializeField] private string suffix = "";

    private TimeManager subscribedTimeManager;
    private bool refreshRequested;

    public int DailyActions => Mathf.Max(0, dailyActions);
    public int RemainingActions => timeManager != null ? timeManager.RemainingActions : 0;

    private void Reset()
    {
        CacheLabels();
    }

    private void OnEnable()
    {
        RefreshDisplay();
    }

    private void Start()
    {
        RefreshDisplay();
        if (!Application.IsPlaying(gameObject))
            return;
        if (timeManager == null)
            Debug.LogWarning("ActionTimes：请绑定与生物投放匣相同的 TimeManager。", this);
        if (label == null && tmpLabel == null)
            Debug.LogWarning("ActionTimes：请挂载到具有 Text 或 TextMeshProUGUI 的对象上。", this);
    }

    private void OnDisable()
    {
        UnbindTimeManager();
    }

    private void OnValidate()
    {
        dailyActions = Mathf.Max(0, dailyActions);
        refreshRequested = true;
    }

    private void Update()
    {
        if (refreshRequested)
            RefreshDisplay();
    }

    private void CacheLabels()
    {
        if (label == null)
            label = GetComponent<Text>();
        if (tmpLabel == null)
            tmpLabel = GetComponent<TMP_Text>();
    }

    /// <summary>应用 Inspector 配置并刷新显示；重新启用文字不会补回已消耗的次数。</summary>
    public void RefreshDisplay()
    {
        refreshRequested = false;
        CacheLabels();
        if (Application.IsPlaying(gameObject))
        {
            if (timeManager == null)
                timeManager = FindObjectOfType<TimeManager>(true);
            if (timeManager != null)
                timeManager.ConfigureDailyActions(DailyActions);

            if (isActiveAndEnabled && subscribedTimeManager != timeManager)
            {
                UnbindTimeManager();
                subscribedTimeManager = timeManager;
                if (subscribedTimeManager != null)
                    subscribedTimeManager.StateChanged += RenderText;
            }
        }
        RenderText();
    }

    private void RenderText()
    {
        string count = !Application.IsPlaying(gameObject) ? DailyActions.ToString()
            : timeManager != null ? timeManager.RemainingActions.ToString() : "--";
        string text = prefix + count + suffix;
        if (label != null)
            label.text = text;
        if (tmpLabel != null)
            tmpLabel.text = text;
    }

    private void UnbindTimeManager()
    {
        if (subscribedTimeManager != null)
            subscribedTimeManager.StateChanged -= RenderText;
        subscribedTimeManager = null;
    }
}
