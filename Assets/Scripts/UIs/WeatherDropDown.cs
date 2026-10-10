using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Dropdown))]
[AddComponentMenu("UI/Weather Drop Down")]
public class WeatherDropDown : MonoBehaviour
{
    [Serializable]
    public struct WeatherOption
    {
        [Tooltip("下拉菜单中显示的文字。")]
        public string text;
        [Tooltip("选择这一项时使用的天气类型，与列表顺序无关。")]
        public E_WeatherType weatherType;
    }

    [Header("下拉框引用（自动获取本物体上的组件）")]
    [SerializeField]
    private TMP_Dropdown tmpWeatherDropDown;

    [Header("天气对象")]
    [SerializeField, Tooltip("拖入挂有 WeatherBase 的 GameObject。")]
    private GameObject weatherObject;
    private WeatherBase weatherBase;

    [SerializeField, Tooltip("绑定时间管理器后，过夜期间自动关闭并锁定天气菜单；留空则独立使用。")]
    private TimeManager timeManager;
    private TimeManager subscribedTimeManager;

    [Header("天气选项")]
    [SerializeField, Tooltip("在此编辑文字与天气类型；自动同步到 TMP_Dropdown 的 Options。")]
    private List<WeatherOption> weatherOptions = new List<WeatherOption>
    {
        new WeatherOption { text = "晴天", weatherType = E_WeatherType.Sun },
        new WeatherOption { text = "雨天", weatherType = E_WeatherType.Rain }
    };

    public WeatherBase Weather => weatherBase;

    /// <summary>每天可切换一次天气；没有绑定 TimeManager 时，按同一天处理。</summary>
    public int WeatherChangePoints => lastWeatherChangeDay == CurrentWeatherDay ? 0 : 1;
    public event Action<int> WeatherChangePointsChanged;

    private bool refreshRequested;
    private int lastWeatherChangeDay = -1;
    private int lastNotifiedPoints = -1;
    private int CurrentWeatherDay => timeManager != null ? timeManager.CurrentDay : 1;

    private void Reset()
    {
        CacheReferences();
        refreshRequested = true;
    }

    private void OnEnable()
    {
        RefreshOptions();
        if (tmpWeatherDropDown != null)
            tmpWeatherDropDown.onValueChanged.AddListener(OnWeatherChanged);
    }

    private void OnDisable()
    {
        if (tmpWeatherDropDown != null)
            tmpWeatherDropDown.onValueChanged.RemoveListener(OnWeatherChanged);
        if (subscribedTimeManager != null)
            subscribedTimeManager.StateChanged -= RefreshInteraction;
        subscribedTimeManager = null;
    }

    private void OnValidate()
    {
        // OnValidate 可能不在主线程执行，UI 更新留到下一次 Update。
        refreshRequested = true;
    }

    private void Update()
    {
        if (refreshRequested)
            RefreshOptions();
    }

    private void CacheReferences()
    {
        tmpWeatherDropDown = GetComponent<TMP_Dropdown>();
        weatherBase = weatherObject != null ? weatherObject.GetComponent<WeatherBase>() : null;
    }

    /// <summary>将 Inspector 中的选项同步到下拉框，并使模板容纳全部选项。</summary>
    [ContextMenu("刷新天气选项")]
    public void RefreshOptions()
    {
        refreshRequested = false;
        CacheReferences();
        if (tmpWeatherDropDown == null)
            return;

        if (Application.IsPlaying(gameObject) && weatherObject != null && weatherBase == null)
            Debug.LogWarning("WeatherObject 上没有 WeatherBase 组件，请先添加该组件。", this);

        int selectedIndex = tmpWeatherDropDown.value;
        var labels = new List<string>();
        if (weatherOptions != null)
        {
            bool matchedWeather = false;
            for (int i = 0; i < weatherOptions.Count; i++)
            {
                labels.Add(weatherOptions[i].text ?? string.Empty);
                if (!matchedWeather && weatherBase != null && weatherOptions[i].weatherType == weatherBase.weatherType)
                {
                    selectedIndex = i;
                    matchedWeather = true;
                }
            }
        }
        selectedIndex = Mathf.Clamp(selectedIndex, 0, Mathf.Max(0, labels.Count - 1));

        if (Application.IsPlaying(gameObject))
            tmpWeatherDropDown.Hide();
        tmpWeatherDropDown.ClearOptions();
        tmpWeatherDropDown.AddOptions(labels);
        tmpWeatherDropDown.SetValueWithoutNotify(selectedIndex);
        tmpWeatherDropDown.RefreshShownValue();
        FitTemplate(tmpWeatherDropDown.template, labels.Count);
        BindTimeManager();
        RefreshInteraction();
    }

    private void BindTimeManager()
    {
        if (!Application.IsPlaying(gameObject) || subscribedTimeManager == timeManager)
            return;
        if (subscribedTimeManager != null)
            subscribedTimeManager.StateChanged -= RefreshInteraction;
        subscribedTimeManager = timeManager;
        if (subscribedTimeManager != null)
            subscribedTimeManager.StateChanged += RefreshInteraction;
    }

    private void RefreshInteraction()
    {
        if (!Application.IsPlaying(gameObject) || tmpWeatherDropDown == null)
            return;
        bool canOperate = timeManager == null || timeManager.CanOperate;
        int points = WeatherChangePoints;
        tmpWeatherDropDown.interactable = canOperate && points > 0 && weatherBase != null
            && tmpWeatherDropDown.options.Count > 0;
        if (!tmpWeatherDropDown.interactable)
            tmpWeatherDropDown.Hide();
        if (weatherBase != null && weatherOptions != null)
        {
            // 新一天可能由其他游戏逻辑更新天气，只同步显示，不触发天气选择回调。
            for (int i = 0; i < weatherOptions.Count; i++)
            {
                if (weatherOptions[i].weatherType != weatherBase.weatherType)
                    continue;
                tmpWeatherDropDown.SetValueWithoutNotify(i);
                break;
            }
        }

        if (lastNotifiedPoints != points)
        {
            lastNotifiedPoints = points;
            WeatherChangePointsChanged?.Invoke(points);
        }
    }

    private void OnWeatherChanged(int index)
    {
        // 编辑预览只更新 UI，不改变场景天气，也不增加连续天气计数。
        if (!Application.IsPlaying(gameObject) || weatherOptions == null || index < 0 || index >= weatherOptions.Count)
            return;
        weatherBase = weatherObject != null ? weatherObject.GetComponent<WeatherBase>() : null;
        if ((timeManager != null && !timeManager.CanOperate) || WeatherChangePoints == 0)
        {
            // 不仅禁用 UI，也拦截直接设置 Dropdown.value 的请求，并恢复为实际天气。
            RefreshInteraction();
            return;
        }

        if (weatherBase == null || weatherBase.weatherType == weatherOptions[index].weatherType)
            return;

        // 必须先扣点，再通知 UI，防止回调中再次切换天气。
        lastWeatherChangeDay = CurrentWeatherDay;
        weatherBase.weatherType = weatherOptions[index].weatherType;
        RefreshInteraction();
    }

    private void FitTemplate(RectTransform template, int optionCount)
    {
        if (template == null)
            return;

        // Template 默认处于隐藏状态，查找时也要包含未激活的子物体。
        Toggle item = template.GetComponentInChildren<Toggle>(true);
        if (item == null)
            return;
        RectTransform itemRect = item.transform as RectTransform;
        RectTransform content = itemRect != null ? itemRect.parent as RectTransform : null;
        if (content == null || content == template)
            return;

        float itemHeight = itemRect.rect.height;
        if (itemHeight <= 0f)
            return;

        float topPadding = Mathf.Max(0f, content.rect.yMax - itemRect.localPosition.y - itemRect.rect.yMax);
        float bottomPadding = Mathf.Max(0f, itemRect.localPosition.y + itemRect.rect.yMin - content.rect.yMin);

        // TMP.Show 只使用 rect.height 计算行距和 Content 高度，不考虑 Item 的 localScale。
        // 先把纵向缩放烘焙为真实高度，否则放大的选项会重叠，并超出 Viewport 被裁剪。
        itemHeight = BakeItemVerticalScale(itemRect);
        float prototypeHeight = itemHeight + topPadding + bottomPadding;

        ScrollRect scrollRect = template.GetComponentInChildren<ScrollRect>(true);
        RectTransform viewport = scrollRect != null && scrollRect.viewport != null
            ? scrollRect.viewport : content.parent as RectTransform;

        // 让 Viewport 铺满菜单，移除滚动条预留空间与额外缩放，避免选项仍被裁剪。
        if (viewport != null && viewport != template)
        {
            viewport.localScale = Vector3.one;
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
        }

        if (scrollRect != null)
        {
            scrollRect.StopMovement();
            scrollRect.horizontal = false;
            scrollRect.vertical = false;
            if (scrollRect.horizontalScrollbar != null)
                scrollRect.horizontalScrollbar.gameObject.SetActive(false);
            if (scrollRect.verticalScrollbar != null)
                scrollRect.verticalScrollbar.gameObject.SetActive(false);
            scrollRect.horizontalScrollbar = null;
            scrollRect.verticalScrollbar = null;
        }

        content.localScale = Vector3.one;
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        // 保留单项模板高度。TMP_Dropdown.Show 会自行扩展副本的 Content；提前扩展会重复计算留白。
        content.sizeDelta = new Vector2(0f, prototypeHeight);
        itemRect.anchorMin = new Vector2(itemRect.anchorMin.x, 1f);
        itemRect.anchorMax = new Vector2(itemRect.anchorMax.x, 1f);
        itemRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, itemHeight);
        itemRect.anchoredPosition = new Vector2(itemRect.anchoredPosition.x,
            -topPadding - itemHeight * (1f - itemRect.pivot.y));

        float menuHeight = itemHeight * Mathf.Max(1, optionCount) + topPadding + bottomPadding;
        template.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, menuHeight);
    }

    /// <summary>将 Item 的纵向缩放换算为布局高度，同时保留子物体的显示大小和位置。</summary>
    private static float BakeItemVerticalScale(RectTransform itemRect)
    {
        float scale = Mathf.Abs(itemRect.localScale.y);
        float oldHeight = itemRect.rect.height;
        if (Mathf.Approximately(scale, 1f) || scale <= 0f)
            return oldHeight;

        float height = oldHeight * scale;
        float heightChange = height - oldHeight;
        for (int i = 0; i < itemRect.childCount; i++)
        {
            Transform child = itemRect.GetChild(i);
            Vector3 childScale = child.localScale;
            childScale.y *= scale;
            child.localScale = childScale;
            if (child is RectTransform childRect)
            {
                // 父物体增高时，抵消 Stretch 对子物体高度的影响，避免文字/背景被放大两次。
                Vector2 size = childRect.sizeDelta;
                size.y -= heightChange * (childRect.anchorMax.y - childRect.anchorMin.y);
                childRect.sizeDelta = size;
                Vector2 position = childRect.anchoredPosition;
                position.y *= scale;
                childRect.anchoredPosition = position;
            }
            else
            {
                Vector3 position = child.localPosition;
                position.y *= scale;
                child.localPosition = position;
            }
        }

        Vector3 itemScale = itemRect.localScale;
        itemScale.y = Mathf.Sign(itemScale.y);
        itemRect.localScale = itemScale;
        itemRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        return height;
    }
}
