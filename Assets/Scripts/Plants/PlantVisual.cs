using UnityEngine;

/// <summary>
/// 将用户的植物预设体实例关联到逻辑对象，外观、动画及渲染排序由预设体自行定义。
/// 种子植物可按阶段切换指定渲染器的图片；蘑菇按活力调整大小。
/// </summary>
[DisallowMultipleComponent]
public class PlantVisual : MonoBehaviour
{
    [Header("种子植物阶段外观（裸子植物 / 被子植物）")]
    [SerializeField, Tooltip("拖入显示植物主体的 SpriteRenderer，可位于子物体上。留空时仅自动选择层级中唯一的 SpriteRenderer。")]
    private SpriteRenderer targetRenderer;
    [SerializeField, InspectorName("种子 (Seed)")]
    private Sprite seedSprite;
    [SerializeField, InspectorName("幼芽 (Sprout)")]
    private Sprite sproutSprite;
    [SerializeField, InspectorName("幼株 (Seedling)")]
    private Sprite seedlingSprite;
    [SerializeField, InspectorName("成熟株 (Mature)")]
    private Sprite matureSprite;
    [SerializeField, InspectorName("巨型株 (Giant)"), Tooltip("仅在逻辑进入巨型阶段后显示；不改变成长条件、缩放或占地。")]
    private Sprite giantSprite;

    public PlantsBase Plant { get; private set; }
    public GameObject SourcePrefab { get; private set; }
    private Vector3 originalScale;
    private SpriteRenderer initializedRenderer;
    private Sprite defaultSprite;
    private int warnedMissingStages;
    private bool warnedMissingRenderer;

    private Sprite FirstConfiguredSprite => seedSprite != null ? seedSprite
        : sproutSprite != null ? sproutSprite
        : seedlingSprite != null ? seedlingSprite
        : matureSprite != null ? matureSprite : giantSprite;

    public void Bind(PlantsBase target, GameObject sourcePrefab)
    {
        Plant = target;
        SourcePrefab = sourcePrefab;
        originalScale = sourcePrefab != null ? sourcePrefab.transform.localScale : transform.localScale;
        Refresh();
    }

    /// <summary>只读逻辑状态：种子植物按阶段换图，蘑菇从原始缩放计算大小，避免累乘。</summary>
    public void Refresh(int mushroomReferenceVitality = 5)
    {
        if (Plant == null)
            return;
        gameObject.name = Plant.DescribeShort();
        if (Plant is Mushroom mushroom)
            transform.localScale = originalScale * Mathf.Sqrt(
                Mathf.Max(0, mushroom.Vitality) / (float)Mathf.Max(1, mushroomReferenceVitality));
        else if (Plant is SeedPlantsBase seed)
            RefreshSeedSprite(seed);
    }

    private void RefreshSeedSprite(SeedPlantsBase seed)
    {
        // 全部留空表示沿用预设体原有外观，兼容已有植物和自定义动画。
        if (FirstConfiguredSprite == null)
            return;

        if (targetRenderer == null)
        {
            SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
            if (renderers.Length == 1)
                targetRenderer = renderers[0];
            else
            {
                if (!warnedMissingRenderer)
                    Debug.LogWarning("PlantVisual：" + name
                        + " 已配置阶段图片，请在 Target Renderer 中指定植物主体的 SpriteRenderer。", this);
                warnedMissingRenderer = true;
                return;
            }
        }

        warnedMissingRenderer = false;
        if (initializedRenderer != targetRenderer)
        {
            initializedRenderer = targetRenderer;
            // 在第一次换图前保存默认图片，缺图时不能误用上一阶段的图片作为默认值。
            defaultSprite = targetRenderer.sprite;
            warnedMissingStages = 0;
        }

        Sprite sprite = SpriteForStage(seed.Stage);
        int stageBit = 1 << (int)seed.Stage;
        if (sprite == null)
        {
            sprite = defaultSprite != null ? defaultSprite : FirstConfiguredSprite;
            if ((warnedMissingStages & stageBit) == 0)
                Debug.LogWarning("PlantVisual：" + name + " 未配置" + seed.StageName
                    + "阶段图片，使用默认图片（默认图片为空时使用首个已配置的阶段图片）。", this);
            warnedMissingStages |= stageBit;
        }
        else
        {
            warnedMissingStages &= ~stageBit;
        }

        if (targetRenderer.sprite != sprite)
            targetRenderer.sprite = sprite;
    }

    private Sprite SpriteForStage(E_PlantStage stage)
    {
        switch (stage)
        {
            case E_PlantStage.Seed: return seedSprite;
            case E_PlantStage.Sprout: return sproutSprite;
            case E_PlantStage.Seedling: return seedlingSprite;
            case E_PlantStage.Mature: return matureSprite;
            case E_PlantStage.Giant: return giantSprite;
            default: return null;
        }
    }
}
