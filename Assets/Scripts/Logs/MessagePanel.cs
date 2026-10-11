using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>消息所属的分类，与 MessageBar 的三个按钮对应。</summary>
public enum MessageTag
{
    Information,
    Daily,
    FoodChain
}

/// <summary>切换消息分类，并在各自的 Scroll View 中追加、保留消息。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class MessagePanel : MonoBehaviour
{
    [Serializable]
    private class MessageView
    {
        public MessageTag Tag;
        public Button Button;
        public ScrollRect ScrollView;

        [NonSerialized] public UnityAction ClickAction;
    }

    [Header("分类绑定（留空时按 MessageBar / ContentPanel 下的名称查找）")]
    [SerializeField] private MessageView[] views =
    {
        new MessageView { Tag = MessageTag.Information },
        new MessageView { Tag = MessageTag.Daily },
        new MessageView { Tag = MessageTag.FoodChain }
    };
    [SerializeField] private MessageTag defaultTag = MessageTag.Information;

    [Header("消息文字")]
    [SerializeField, Tooltip("留空时使用分类按钮的 TMP 字体，便于沿用现有中文字体。")]
    private TMP_FontAsset messageFont;
    [SerializeField, Min(1f)] private float fontSize = 20f;
    [SerializeField] private Color fontColor = new Color(0.2f, 0.2f, 0.2f, 1f);

    private bool initialized;
    private MessageTag selectedTag;

    public MessageTag SelectedTag => initialized ? selectedTag : defaultTag;

    private void Reset()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        Initialize();
    }

    private void OnEnable()
    {
        Initialize();
        foreach (MessageView view in views)
        {
            if (view == null || view.Button == null || view.ClickAction != null)
                continue;

            MessageTag tag = view.Tag;
            view.ClickAction = () => SelectTab(tag);
            view.Button.onClick.AddListener(view.ClickAction);
        }
        SelectTab(selectedTag);
    }

    private void OnDisable()
    {
        foreach (MessageView view in views)
        {
            if (view == null || view.ClickAction == null)
                continue;
            if (view.Button != null)
                view.Button.onClick.RemoveListener(view.ClickAction);
            view.ClickAction = null;
        }
    }

    private void ResolveReferences()
    {
        foreach (MessageView view in views)
        {
            if (view == null)
                continue;

            if (view.Button == null)
            {
                Transform button = transform.Find("MessageBar/" + view.Tag + "Btn");
                if (button != null)
                    view.Button = button.GetComponent<Button>();
            }
            if (view.ScrollView == null)
            {
                Transform scroll = transform.Find("ContentPanel/" + view.Tag + "SV");
                if (scroll != null)
                    view.ScrollView = scroll.GetComponent<ScrollRect>();
            }
            if (messageFont == null && view.Button != null)
            {
                TMP_Text label = view.Button.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                    messageFont = label.font;
            }
        }
    }

    private void Initialize()
    {
        if (initialized)
            return;

        ResolveReferences();
        selectedTag = defaultTag;
        foreach (MessageView view in views)
        {
            if (view == null)
                continue;
            if (view.Button == null || view.ScrollView == null)
                Debug.LogWarning("MessagePanel：请检查 " + view.Tag + " 的 Button 和 Scroll View 绑定。", this);
            if (view.ScrollView != null)
                ConfigureContent(view.ScrollView);
        }
        initialized = true;
    }

    private void ConfigureContent(ScrollRect scroll)
    {
        if (scroll.viewport == null)
            scroll.viewport = scroll.transform.Find("Viewport") as RectTransform;

        if (scroll.content == null && scroll.viewport != null)
        {
            scroll.content = scroll.viewport.Find("Content") as RectTransform;
            if (scroll.content == null)
            {
                var container = new GameObject("Content", typeof(RectTransform));
                container.layer = scroll.gameObject.layer;
                container.transform.SetParent(scroll.viewport, false);
                scroll.content = container.GetComponent<RectTransform>();
            }
        }
        if (scroll.content == null)
        {
            Debug.LogWarning("MessagePanel：" + scroll.name + " 缺少 Viewport / Content，请检查 Scroll Rect。", this);
            return;
        }

        RectTransform content = scroll.content;
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = new Vector2(0f, content.sizeDelta.y);
        content.anchoredPosition = Vector2.zero;

        VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
        {
            layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 6f;
        }
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
        if (fitter == null)
            fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.horizontal = false;
        scroll.vertical = true;
    }

    private MessageView FindView(MessageTag tag)
    {
        foreach (MessageView view in views)
        {
            if (view != null && view.Tag == tag)
                return view;
        }
        return null;
    }

    /// <summary>只切换视图的显示状态，不清空或重新生成任何消息。</summary>
    public void SelectTab(MessageTag tag)
    {
        Initialize();
        MessageView target = FindView(tag);
        if (target == null || target.ScrollView == null)
        {
            Debug.LogWarning("MessagePanel：未找到分类 " + tag + " 对应的 Scroll View。", this);
            return;
        }

        selectedTag = tag;
        foreach (MessageView view in views)
        {
            if (view != null && view.ScrollView != null)
                view.ScrollView.gameObject.SetActive(view == target);
        }
        if (target.ScrollView.content != null)
            LayoutRebuilder.MarkLayoutForRebuild(target.ScrollView.content);
    }

    /// <summary>向 InformationSV 追加观察信息，保留历史消息且不切换当前标签。</summary>
    public void ShowInformation(string message)
    {
        ShowMessage(message, MessageTag.Information);
    }

    /// <summary>
    /// 在指定分类的 Content 容器下追加一条独立消息。隐藏分类也可以接收消息；不会自动切换标签。
    /// 消息保留到面板对象被销毁，不负责跨场景或退出游戏后的存档。
    /// </summary>
    public void ShowMessage(string message, MessageTag tag)
    {
        if (string.IsNullOrEmpty(message))
            return;

        // 允许其他脚本在本组件 Awake 之前，或整个面板隐藏时追加消息。
        Initialize();
        MessageView view = FindView(tag);
        if (view == null || view.ScrollView == null || view.ScrollView.content == null)
        {
            Debug.LogWarning("MessagePanel：无法添加消息，请检查 " + tag + " 的 Scroll View / Content 绑定。", this);
            return;
        }

        RectTransform content = view.ScrollView.content;
        var item = new GameObject("MessageContent", typeof(RectTransform));
        item.layer = content.gameObject.layer;
        item.transform.SetParent(content, false);
        item.transform.SetAsLastSibling();

        TextMeshProUGUI label = item.AddComponent<TextMeshProUGUI>();
        label.font = messageFont != null ? messageFont : TMP_Settings.defaultFontAsset;
        label.fontSize = Mathf.Max(1f, fontSize);
        label.color = fontColor;
        label.alignment = TextAlignmentOptions.TopLeft;
        label.enableAutoSizing = false;
        label.enableWordWrapping = true;
        label.overflowMode = TextOverflowModes.Overflow;
        label.richText = false;
        label.raycastTarget = false;
        label.text = message;
        LayoutRebuilder.MarkLayoutForRebuild(content);
    }

    /// <summary>字符串标签支持 Information、Daily、FoodChain，忽略大小写及首尾空格。</summary>
    public void ShowMessage(string message, string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)
            || !Enum.TryParse(tag.Trim(), true, out MessageTag parsedTag)
            || !Enum.IsDefined(typeof(MessageTag), parsedTag))
        {
            Debug.LogWarning("MessagePanel：未知消息标签 " + tag + "。请使用 Information、Daily 或 FoodChain。", this);
            return;
        }
        ShowMessage(message, parsedTag);
    }
}
