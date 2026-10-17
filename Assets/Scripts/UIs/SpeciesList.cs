using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum SpeciesKind
{
    [InspectorName("未配置")] None,
    [InspectorName("植物")] Plant,
    [InspectorName("动物")] Animal
}

/// <summary>卡片选择逻辑物种，外观由场景中的 Visual Binder 配置。</summary>
[Serializable]
public struct SpeciesInfo
{
    public Sprite Icon;
    public string Name;
    [TextArea(2, 5)] public string Description;
    [Min(0)] public int DailyCount;
    public SpeciesKind Kind;
    public E_PlantSpecies PlantSpecies;
    public E_AnimalSpecies AnimalSpecies;

    public bool IsConfigured => Kind == SpeciesKind.Plant
        ? Enum.IsDefined(typeof(E_PlantSpecies), PlantSpecies)
        : Kind == SpeciesKind.Animal && AnimalSettler.IsSupported(AnimalSpecies);
}

/// <summary>生态工具投放匣：按配置顺序生成卡片，并管理纵向滚动布局。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class SpeciesList : MonoBehaviour, IScrollHandler
{
    [Header("物种与卡片")]
    [SerializeField, Tooltip("按此列表从上到下生成卡片。")]
    private List<SpeciesInfo> species = new List<SpeciesInfo>();
    [Tooltip("挂有 SpeciesCard 的 UI 卡片预设体。")]
    public SpeciesCard PreSpeciesCard;
    [Tooltip("子物体 SpeciesScroll 上的 ScrollRect，需要绑定其 Content。")]
    public ScrollRect SpeciesScroll;

    [Header("排列与滚动")]
    [SerializeField, Min(0f), Tooltip("相邻卡片之间的垂直间距，单位为 Canvas 像素。")]
    private float verticalSpacing = 10f;
    [SerializeField, Min(1f)] private float scrollSensitivity = 40f;

    [Header("投放与过夜")]
    [SerializeField] private TimeManager timeManager;
    [SerializeField, Tooltip("可投放的环境地图；仅接受此地图上已经绘制的格子。")]
    private TileMapManager environment;
    [SerializeField, Tooltip("留空使用 MainCamera。")]
    private Camera placementCamera;
    [SerializeField, Tooltip("植物外观配置；留空时查找管理同一地图的唯一 Binder。")]
    private PlantVisualBinder plantVisualBinder;
    [SerializeField, Tooltip("动物外观与登记表；留空时查找管理同一地图的唯一 Binder。")]
    private AnimalVisualBinder animalVisualBinder;

    private readonly List<SpeciesCard> cards = new List<SpeciesCard>();
    private bool initialized;
    private bool layoutDirty;
    public IReadOnlyList<SpeciesCard> Cards => cards;

    private void Reset()
    {
        SpeciesScroll = GetComponentInChildren<ScrollRect>(true);
    }

    private void Start()
    {
        InitializeCards();
    }

    /// <summary>开局调用一次。重复调用不会复制卡片或恢复已消耗的数量。</summary>
    public void InitializeCards()
    {
        if (initialized || !Application.isPlaying)
            return;
        if (SpeciesScroll == null)
            SpeciesScroll = GetComponentInChildren<ScrollRect>(true);
        if (PreSpeciesCard == null || SpeciesScroll == null || SpeciesScroll.content == null)
        {
            Debug.LogWarning("SpeciesList：请绑定 PreSpeciesCard、SpeciesScroll 及其 Content。", this);
            return;
        }
        if (timeManager == null)
            timeManager = FindObjectOfType<TimeManager>();
        if (environment == null)
            environment = FindObjectOfType<TileMapManager>();

        ConfigureLayout();
        foreach (SpeciesInfo info in species)
        {
            SpeciesCard card = Instantiate(PreSpeciesCard, SpeciesScroll.content, false);
            card.transform.SetAsLastSibling();
            card.Initialize(info, timeManager, environment, placementCamera, plantVisualBinder, animalVisualBinder);
            card.gameObject.SetActive(true);
            cards.Add(card);
        }
        initialized = true;
        RefreshLayout();
        SpeciesScroll.StopMovement();
        SpeciesScroll.verticalNormalizedPosition = 1f;
    }

    /// <summary>按预设体尺寸刷新已有卡片，不重新生成卡片或重置投放数量。</summary>
    [ContextMenu("刷新卡片布局")]
    public void RefreshLayout()
    {
        if (!initialized || PreSpeciesCard == null || SpeciesScroll == null || SpeciesScroll.content == null)
            return;

        float scrollPosition = SpeciesScroll.verticalNormalizedPosition;
        ConfigureLayout();
        float height = GetPrefabCardHeight();
        foreach (SpeciesCard card in cards)
        {
            if (card == null)
                continue;
            LayoutElement element = card.GetComponent<LayoutElement>();
            if (element == null)
                element = card.gameObject.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            element.flexibleHeight = 0f;
        }

        // 只重建当前列表，ContentSizeFitter 据此更新滚动范围。
        LayoutRebuilder.ForceRebuildLayoutImmediate(SpeciesScroll.content);
        SpeciesScroll.verticalNormalizedPosition = scrollPosition;
    }

    private float GetPrefabCardHeight()
    {
        var cardRect = (RectTransform)PreSpeciesCard.transform;
        float height = Mathf.Max(1f, cardRect.rect.height, LayoutUtility.GetPreferredHeight(cardRect));
        RectTransform background = cardRect.Find("BackGround") as RectTransform;
        if (background != null)
        {
            // 美术可能只调整背景：根节点仍为 100 时，150 高的背景也必须完整占位。
            // 使用卡片局部坐标，兼容背景偏移与缩放；不修改预设体本身。
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(cardRect, background);
            float visibleHeight = bounds.size.y + 2f * Mathf.Abs(bounds.center.y - cardRect.rect.center.y);
            height = Mathf.Max(height, visibleHeight);
        }
        return height;
    }

    private void ConfigureLayout()
    {
        RectTransform content = SpeciesScroll.content;
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = new Vector2(0f, content.sizeDelta.y);

        VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
            layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childScaleHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = Mathf.Max(0f, verticalSpacing);

        ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
        if (fitter == null)
            fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        SpeciesScroll.horizontal = false;
        SpeciesScroll.vertical = true;
        SpeciesScroll.movementType = ScrollRect.MovementType.Clamped;
        SpeciesScroll.scrollSensitivity = Mathf.Max(1f, scrollSensitivity);
        layoutDirty = false;
    }

    // ScrollView 和卡片区域由 ScrollRect 接收；面板其余区域也能转发滚轮。
    public void OnScroll(PointerEventData eventData)
    {
        if (isActiveAndEnabled && SpeciesScroll != null && SpeciesScroll.IsActive())
            SpeciesScroll.OnScroll(eventData);
    }

    private void OnValidate()
    {
        layoutDirty = true;
    }

    private void LateUpdate()
    {
        if (initialized && layoutDirty && SpeciesScroll != null && SpeciesScroll.content != null)
            RefreshLayout();
    }
}
