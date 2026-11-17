using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public class NightSettleButton : MonoBehaviour
{
    private const string SimulationLimitMessage = "该生态箱已达到模拟上限，感谢游玩喵";

    [SerializeField, Tooltip("自动获取本物体上的 Button；点击事件由本脚本绑定。")]
    private Button settleButton;
    [SerializeField, Tooltip("拖入场景中的 TimeManager，由其统一处理过夜和进入下一天。")]
    private TimeManager timeManager;
    [SerializeField, Tooltip("可选；留空时自动查找按钮下的 TMP 文字。")]
    private TMP_Text buttonLabel;
    [SerializeField, Tooltip("模拟达到上限时向此面板的 InformationSV 写入提示；留空时自动查找同 Canvas 或同场景中的面板。")]
    private MessagePanel messagePanel;

    private TimeManager subscribedTimeManager;
    private bool refreshRequested;

    private bool CanClick => isActiveAndEnabled && timeManager != null && timeManager.isActiveAndEnabled
        && (timeManager.IsGameSettlement || timeManager.CanRequestNightSettlement);

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
        {
            subscribedTimeManager.StateChanged -= RefreshDisplay;
            subscribedTimeManager.GameSettlementStarted -= OnGameSettlementStarted;
        }
        subscribedTimeManager = null;
    }

    private void OnValidate()
    {
        refreshRequested = true;
    }

    private void Update()
    {
        // 同时响应结算组件被启用/停用或引用被修复，不在此处发起结算。
        bool canSettle = CanClick;
        if (refreshRequested || (settleButton != null && settleButton.interactable != canSettle))
            RefreshDisplay();
    }

    private void OnNightSettleClicked()
    {
        if (!CanClick || settleButton == null || !settleButton.IsActive() || !settleButton.IsInteractable())
            return;
        if (timeManager.IsGameSettlement)
            ShowSimulationLimitMessage();
        else
            timeManager.TryStartNightSettlement();
    }

    private void OnGameSettlementStarted(int day)
    {
        ShowSimulationLimitMessage();
    }

    private void ShowSimulationLimitMessage()
    {
        if (messagePanel == null)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
                messagePanel = canvas.GetComponentInChildren<MessagePanel>(true);
            if (messagePanel == null)
            {
                foreach (MessagePanel candidate in FindObjectsOfType<MessagePanel>(true))
                {
                    if (candidate.gameObject.scene != gameObject.scene)
                        continue;
                    messagePanel = candidate;
                    break;
                }
            }
        }
        if (messagePanel == null)
        {
            Debug.LogWarning("NightSettleButton：请绑定 MessagePanel，才能显示模拟上限提示。", this);
            return;
        }

        // 消息按最新优先排列；TMP 不计算最后一个换行的高度，六个换行才能留出五行空白。
        messagePanel.ShowInformation(SimulationLimitMessage + "\n\n\n\n\n\n");
        messagePanel.SelectTab(MessageTag.Information);
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
            {
                subscribedTimeManager.StateChanged -= RefreshDisplay;
                subscribedTimeManager.GameSettlementStarted -= OnGameSettlementStarted;
            }
            subscribedTimeManager = timeManager;
            if (subscribedTimeManager != null)
            {
                subscribedTimeManager.StateChanged += RefreshDisplay;
                subscribedTimeManager.GameSettlementStarted += OnGameSettlementStarted;
            }
        }

        bool canSettle = CanClick;
        if (settleButton != null)
            settleButton.interactable = canSettle;
        if (buttonLabel != null)
        {
            if (timeManager != null && timeManager.IsGameSettlement)
                buttonLabel.text = "游戏结算";
            else if (timeManager != null && timeManager.IsSettlementRunning)
                buttonLabel.text = "过夜结算中…";
            else if (timeManager != null && timeManager.IsNightSettlement)
                buttonLabel.text = "重试过夜结算";
            else
                buttonLabel.text = "过夜结算";
        }
    }
}
