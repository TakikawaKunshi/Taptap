using UnityEngine;

/// <summary>
/// 一株植物的占位可视化。
///
/// 为什么需要它：植物本体是纯逻辑对象，不挂在场景上，地图只持有它的引用。
/// 数据层这样最干净，但测试时 Game 视图空空如也，规则对不对只能靠翻 Console。
/// 这里把每株植物画成一张贴图：物种决定染色，进度决定大小，枯萎变灰半透明；
/// 同时把 DescribeShort 写进物体名，Hierarchy 里能直接读到准确数值。
///
/// 临时用图换成正式美术资源时，只要在 Binder 上换贴图引用，本类一行都不用动。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class PlantVisual : MonoBehaviour
{
    private PlantsBase plant;
    private SpriteRenderer spriteRenderer;
    private bool isSubject;

    /// <summary>对应的植物逻辑对象；地图把它回收后 Binder 会销毁本物体。</summary>
    public PlantsBase Plant
    {
        get { return plant; }
    }

    /// <summary>
    /// 绑定植物并应用一次外观。sprite 与排序层来自 Binder，所有植株共用。
    /// </summary>
    public void Bind(PlantsBase target, Sprite sprite, int sortingOrder, bool subject)
    {
        plant = target;
        isSubject = subject;

        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.sprite = sprite;
        spriteRenderer.sortingOrder = sortingOrder;
        Refresh();
    }

    /// <summary>按当前逻辑状态刷新外观。只读植物状态，不反过来改它。</summary>
    public void Refresh()
    {
        if (plant == null || spriteRenderer == null || spriteRenderer.sprite == null)
            return;

        gameObject.name = plant.DescribeShort();
        spriteRenderer.color = ResolveColor();

        float size = ResolveSize();
        Vector2 aspect = SpriteAspect();
        transform.localScale = new Vector3(size * aspect.x, size * aspect.y, 1f);
    }

    /// <summary>主体植物画大一点；地表覆盖层再缩小，避免盖住主体。</summary>
    private float ResolveSize()
    {
        float size;

        SporePlantsBase spore = plant as SporePlantsBase;
        if (spore != null)
        {
            // 孢子植物的进度就是活力：0 点几乎看不见，5 点接近一整格。
            size = 0.28f + Mathf.Max(0, Mathf.Min(spore.Vitality, 5)) * 0.08f;
        }
        else
        {
            SeedPlantsBase seed = plant as SeedPlantsBase;
            if (seed != null)
            {
                switch (seed.Stage)
                {
                    case E_PlantStage.Seed: size = 0.22f; break;
                    case E_PlantStage.Sprout: size = 0.34f; break;
                    case E_PlantStage.Seedling: size = 0.48f; break;
                    case E_PlantStage.Mature: size = 0.62f; break;
                    case E_PlantStage.Giant: size = 0.85f; break;
                    default: size = 0.4f; break;
                }
            }
            else
            {
                size = 0.4f;
            }
        }

        return isSubject ? size : size * 0.62f;
    }

    /// <summary>临时图是人形插画，按物种染色才能一眼分清；枯萎统一灰掉。</summary>
    private Color ResolveColor()
    {
        if (plant.IsDead)
            return new Color(0.3f, 0.3f, 0.3f, 0f);
        if (plant.IsWithering)
            return new Color(0.5f, 0.5f, 0.5f, 0.55f);

        switch (plant.Species)
        {
            case E_PlantSpecies.Algae: return new Color(0.55f, 1f, 0.9f);
            case E_PlantSpecies.Moss: return new Color(0.6f, 1f, 0.55f);
            case E_PlantSpecies.Mushroom: return new Color(1f, 0.78f, 0.55f);
            case E_PlantSpecies.Gymnosperm: return new Color(0.55f, 0.85f, 1f);
            case E_PlantSpecies.Angiosperm: return new Color(1f, 0.65f, 0.85f);
            default: return Color.white;
        }
    }

    /// <summary>贴图不是正方形时按长边归一，避免拉伸变形。</summary>
    private Vector2 SpriteAspect()
    {
        Rect rect = spriteRenderer.sprite.rect;
        float longer = Mathf.Max(rect.width, rect.height);
        if (longer <= 0f)
            return Vector2.one;
        return new Vector2(rect.width / longer, rect.height / longer);
    }
}
