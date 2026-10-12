using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public class NightSettleButton : MonoBehaviour
{
    [SerializeField, Tooltip("自动获取本物体上的 Button；点击事件由本脚本绑定。")]
    private Button settleButton;
    [SerializeField, Tooltip("拖入场景中的 TimeManager，由其统一处理过夜和进入下一天。")]
    private TimeManager timeManager;
    [SerializeField, Tooltip("可选；留空时自动查找按钮下的 TMP 文字。")]
    private TMP_Text buttonLabel;

    private TimeManager subscribedTimeManager;
    private bool refreshRequested;

    private void Reset()
    {
        settleButton = GetComponent<Button>();
        buttonLabel = GetComponentInChildren<TMP_Text>(true);
    }

    private void OnEnable()
    {
        settleButton = GetComponent<Button>();
        settleButton.onClick.AddListener(OnNightSettleClicked);
        RefreshDisplay();
    }

    private void Start()
    {
        RefreshDisplay();
        if (timeManager == null)
            Debug.LogWarning("NightSettleButton：请在 Inspector 中绑定 TimeManager。", this);
    }

    private void OnDisable()
    {
        if (settleButton != null)
        {
            settleButton.onClick.RemoveListener(OnNightSettleClicked);
            settleButton.interactable = false;
        }
        if (subscribedTimeManager != null)
            subscribedTimeManager.StateChanged -= RefreshDisplay;
        subscribedTimeManager = null;
    }

    private void OnValidate()
    {
        refreshRequested = true;
    }

    private void Update()
    {
        // 同时响应结算组件被启用/停用或引用被修复，不在此处发起结算。
        bool canSettle = timeManager != null && timeManager.CanRequestNightSettlement;
        if (refreshRequested || (settleButton != null && settleButton.interactable != canSettle))
            RefreshDisplay();
    }

    private void OnNightSettleClicked()
    {
        if (isActiveAndEnabled && settleButton != null && settleButton.IsActive()
            && settleButton.IsInteractable() && timeManager != null)
            timeManager.TryStartNightSettlement();
    }

    public void RefreshDisplay()
    {
        refreshRequested = false;
        if (settleButton == null)
            settleButton = GetComponent<Button>();
        if (buttonLabel == null)
            buttonLabel = GetComponentInChildren<TMP_Text>(true);

        if (isActiveAndEnabled && subscribedTimeManager != timeManager)
        {
            if (subscribedTimeManager != null)
                subscribedTimeManager.StateChanged -= RefreshDisplay;
            subscribedTimeManager = timeManager;
            if (subscribedTimeManager != null)
                subscribedTimeManager.StateChanged += RefreshDisplay;
        }

        bool canSettle = isActiveAndEnabled && timeManager != null && timeManager.CanRequestNightSettlement;
        if (settleButton != null)
            settleButton.interactable = canSettle;
        if (buttonLabel != null)
        {
            if (timeManager != null && timeManager.IsSettlementRunning)
                buttonLabel.text = "过夜结算中…";
            else if (timeManager != null && timeManager.IsNightSettlement)
                buttonLabel.text = "重试过夜结算";
            else
                buttonLabel.text = "过夜结算";
        }
    }
}
