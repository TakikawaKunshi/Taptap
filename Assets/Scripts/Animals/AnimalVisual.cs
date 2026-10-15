using UnityEngine;

/// <summary>
/// 一只动物的占位可视化。
/// 物种决定染色，饱腹决定大小，饥饿到衰弱会明显缩小并变暗，
/// 携带物会用一小圈标记表示。状态写在物体名上，便于在 Hierarchy 里读数值。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class AnimalVisual : MonoBehaviour
{
    private AnimalsBase animal;
    private SpriteRenderer spriteRenderer;
    private GameObject marker;

    public AnimalsBase Animal => animal;

    public void Bind(AnimalsBase target, Sprite sprite, int sortingOrder)
    {
        animal = target;
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.sprite = sprite;
        spriteRenderer.sortingOrder = sortingOrder;
        Refresh();
    }

    public void Refresh()
    {
        if (animal == null || spriteRenderer == null || spriteRenderer.sprite == null)
            return;

        gameObject.name = animal.DescribeShort();
        spriteRenderer.color = ResolveColor();

        float size = 0.22f + animal.Satiety * 0.07f;
        if (animal.HungerState == E_HungerState.Hungry)
            size += 0.04f;
        else if (animal.HungerState == E_HungerState.Weak)
            size -= 0.06f;

        Vector2 aspect = SpriteAspect();
        transform.localScale = new Vector3(size * aspect.x, size * aspect.y, 1f);

        UpdateCarriedMarker(size);
    }

    /// <summary>动物都在同一张贴图上，靠颜色区分；饥饿状态一眼可辨。</summary>
    private Color ResolveColor()
    {
        if (animal.HungerState == E_HungerState.Dead)
            return new Color(0.3f, 0.3f, 0.3f, 0f);
        if (animal.HungerState == E_HungerState.Weak)
            return new Color(0.55f, 0.55f, 0.55f, 0.85f);

        switch (animal.Species)
        {
            case E_AnimalSpecies.Frog: return new Color(0.6f, 0.95f, 0.6f);
            case E_AnimalSpecies.Earthworm: return new Color(1f, 0.7f, 0.6f);
            case E_AnimalSpecies.Snail: return new Color(0.85f, 0.75f, 1f);
            case E_AnimalSpecies.Ant: return new Color(1f, 0.9f, 0.55f);
            case E_AnimalSpecies.Spider: return new Color(0.7f, 0.85f, 1f);
            default: return Color.white;
        }
    }

    /// <summary>携带种子等资源时头顶多一个小方块，方便肉眼追踪搬运过程。</summary>
    private void UpdateCarriedMarker(float size)
    {
        bool carry = animal.Carried != E_ResourceKind.None;
        if (carry && marker == null)
        {
            marker = new GameObject("Carried");
            marker.transform.SetParent(transform, false);
            SpriteRenderer markerSprite = marker.AddComponent<SpriteRenderer>();
            markerSprite.sprite = spriteRenderer.sprite;
            markerSprite.sortingOrder = spriteRenderer.sortingOrder + 1;
            markerSprite.color = Color.white;
        }
        else if (!carry && marker != null)
        {
            Destroy(marker);
            marker = null;
        }

        if (marker == null)
            return;

        marker.transform.localPosition = new Vector3(size * 0.9f, size * 1.1f, 0f);
        marker.transform.localScale = new Vector3(size * 0.35f, size * 0.35f, 1f);
    }

    private Vector2 SpriteAspect()
    {
        Rect rect = spriteRenderer.sprite.rect;
        float longer = Mathf.Max(rect.width, rect.height);
        if (longer <= 0f)
            return Vector2.one;
        return new Vector2(rect.width / longer, rect.height / longer);
    }
}
