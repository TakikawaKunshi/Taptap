using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>列表和卡片共用的物种配置；每张卡片独立保存当天剩余数量。</summary>
[Serializable]
public struct SpeciesInfo
{
    public Sprite Icon;
    public string Name;
    [TextArea(2, 5)] public string Description;
    [Min(0)] public int DailyCount;
    [Tooltip("投放到环境中的物种预设体，不是 UI 卡片预设体。")]
    public GameObject Prefab;
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
    [SerializeField, Tooltip("生成物种的父物体：与 Tilemap 同级且挂有 FacingCamera。留空时自动查找；有多个候选时请手动指定。")]
    private Transform speciesParent;

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
            card.Initialize(info, timeManager, environment, placementCamera, speciesParent);
            card.gameObject.SetActive(true);
            cards.Add(card);
        }
        initialized = true;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(SpeciesScroll.content);
        SpeciesScroll.StopMovement();
        SpeciesScroll.verticalNormalizedPosition = 1f;
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
        layout.childControlHeight = false;
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
            ConfigureLayout();
    }
}
