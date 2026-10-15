using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 把登记在案的動物同步成可见的占位物体。
///
/// 与 PlantVisualBinder 同一套思路：动物也是纯逻辑对象，不挂在场景上，
/// 因此这里用一张贴图把它画出来，让用户能确认"它真的动了、真的吃了东西"。
/// 物种决定染色，饱腹决定大小，饥饿发灰；DescribeShort 写进物体名便于读数值。
/// 临时图换成正式美术时只要换 Binder 上的贴图引用。
/// </summary>
[DisallowMultipleComponent]
public class AnimalVisualBinder : MonoBehaviour
{
    [SerializeField] private AnimalSpawner spawner;
    [SerializeField, Tooltip("留空时自动查找场景里的 AnimalSpawner。")]
    private PlantSettlementBridge plantBridge;
    [SerializeField, Tooltip("临时用图；换正式美术时直接替换这张贴图即可。")]
    private Texture2D animalTexture;
    [SerializeField] private Material spriteMaterial;
    [SerializeField, Min(0.05f)] private float syncInterval = 0.25f;
    [SerializeField, Tooltip("画在地形与植物之上，避免被格子盖住。")]
    private int sortingOrder = 50;
    [SerializeField, Tooltip("同一格多只动物沿对角线错开的间距。")]
    private float spacing = 0.18f;

    private readonly Dictionary<AnimalsBase, AnimalVisual> visuals
        = new Dictionary<AnimalsBase, AnimalVisual>();
    private readonly HashSet<AnimalsBase> seen = new HashSet<AnimalsBase>();
    private readonly List<AnimalsBase> gone = new List<AnimalsBase>();
    private Sprite cachedSprite;
    private float nextSync;

    private void OnEnable()
    {
        SyncNow();
    }

    private void Update()
    {
        if (!Application.isPlaying || Time.realtimeSinceStartup < nextSync)
            return;
        SyncNow();
    }

    /// <summary>按登记表重画一遍；动物移动后位置也会随之更新。</summary>
    public void SyncNow()
    {
        nextSync = Time.realtimeSinceStartup + syncInterval;
        if (spawner == null)
            spawner = FindObjectOfType<AnimalSpawner>();
        if (spawner == null)
            return;

        seen.Clear();
        IReadOnlyList<AnimalsBase> animals = spawner.Animals;
        for (int i = 0; i < animals.Count; i++)
            Touch(animals[i], i);

        gone.Clear();
        foreach (KeyValuePair<AnimalsBase, AnimalVisual> pair in visuals)
        {
            if (!seen.Contains(pair.Key))
                gone.Add(pair.Key);
        }

        for (int i = 0; i < gone.Count; i++)
        {
            AnimalVisual visual;
            if (!visuals.TryGetValue(gone[i], out visual))
                continue;
            if (visual != null)
                Destroy(visual.gameObject);
            visuals.Remove(gone[i]);
        }
    }

    [ContextMenu("立即同步一次")]
    private void SyncFromMenu()
    {
        SyncNow();
    }

    private void Touch(AnimalsBase animal, int order)
    {
        if (animal == null || animal.IsDead || animal.CurrentCell == null)
            return;

        seen.Add(animal);

        AnimalVisual visual;
        if (!visuals.TryGetValue(animal, out visual) || visual == null)
        {
            GameObject go = new GameObject(animal.DescribeShort());
            go.transform.SetParent(transform, false);
            visual = go.AddComponent<AnimalVisual>();
            visual.GetComponent<SpriteRenderer>().sharedMaterial = ResolveMaterial();
            visual.Bind(animal, ResolveSprite(), sortingOrder);
            visuals[animal] = visual;
        }

        // ICellEnv 是给规则层用的抽象，不含渲染信息；位置要向地图层的具体格子要。
        TileCell cell = animal.CurrentCell as TileCell;
        Vector3 origin = cell != null ? cell.WorldPosition : Vector3.zero;

        // 同一格上的多只动物沿对角线排开，避免完全重叠后看不清数量。
        visual.transform.position = origin + OffsetFor(order);
        visual.Refresh();
    }

    private Vector3 OffsetFor(int order)
    {
        float step = spacing * order;
        return new Vector3(step, step * 0.5f, 0f);
    }

    private Material ResolveMaterial()
    {
        if (spriteMaterial != null)
            return spriteMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogWarning("AnimalVisualBinder：找不到可用的精灵着色器，动物可能显示为粉色。", this);
            return null;
        }

        spriteMaterial = new Material(shader);
        return spriteMaterial;
    }

    /// <summary>
    /// 临时图是竖版插画，取中央偏上的方形用于避免变成细长条。
    /// 五个物种共用同一份 Sprite，只是染色不同。
    /// </summary>
    private Sprite ResolveSprite()
    {
        if (cachedSprite != null)
            return cachedSprite;
        if (animalTexture == null)
            return null;

        try
        {
            int side = Mathf.Min(animalTexture.width, animalTexture.height);
            int x = (animalTexture.width - side) / 2;
            int y = Mathf.Min(animalTexture.height - side, (int)(animalTexture.height * 0.2f));
            Color[] pixels = animalTexture.GetPixels(x, y, side, side);

            Texture2D cropped = new Texture2D(side, side, TextureFormat.RGBA32, false);
            cropped.SetPixels(pixels);
            cropped.Apply();
            cropped.filterMode = FilterMode.Bilinear;

            cachedSprite = Sprite.Create(cropped, new Rect(0, 0, side, side),
                new Vector2(0.5f, 0.5f), side);
            cachedSprite.name = animalTexture.name + "_crop";
            return cachedSprite;
        }
        catch (UnityException)
        {
            Debug.LogWarning("AnimalVisualBinder：贴图 " + animalTexture.name
                             + " 未开启 Read/Write，无法生成占位图。", this);
            return null;
        }
    }
}
