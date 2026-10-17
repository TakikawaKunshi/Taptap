using System.Collections.Generic;
using UnityEngine;

/// <summary>按物种实例化用户配置的预设体，并同步动物位置；未配置的物种不创建显示物体。</summary>
[DisallowMultipleComponent]
public class AnimalVisualBinder : MonoBehaviour
{
    [SerializeField, Tooltip("留空时自动查找场景里的 AnimalSpawner。")]
    private AnimalSpawner spawner;

    [Header("物种预设体（留空不创建，外观由预设体决定）")]
    [SerializeField, InspectorName("青蛙 (Frog)")] private GameObject frogPrefab;
    [SerializeField, InspectorName("蚯蚓 (Earthworm)")] private GameObject earthwormPrefab;
    [SerializeField, InspectorName("蜗牛 (Snail)")] private GameObject snailPrefab;
    [SerializeField, InspectorName("蚂蚁 (Ant)")] private GameObject antPrefab;
    [SerializeField, InspectorName("蜘蛛 (Spider)")] private GameObject spiderPrefab;
    [SerializeField, InspectorName("鱼类 (Fish)")] private GameObject fishPrefab;
    [SerializeField, InspectorName("沙蚕 (Sandworm)")] private GameObject sandwormPrefab;
    [SerializeField, InspectorName("麻雀 (Sparrow)")] private GameObject sparrowPrefab;
    [SerializeField, InspectorName("奶蛙 (MilkFrog)")] private GameObject milkFrogPrefab;

    [Header("同步")]
    [SerializeField, Min(0.05f)] private float syncInterval = 0.25f;
    [SerializeField, Tooltip("同一格多只动物沿对角线错开的间距。")]
    private float spacing = 0.18f;

    private readonly Dictionary<AnimalsBase, AnimalVisual> visuals
        = new Dictionary<AnimalsBase, AnimalVisual>();
    private readonly HashSet<AnimalsBase> seen = new HashSet<AnimalsBase>();
    private readonly List<AnimalsBase> gone = new List<AnimalsBase>();
    private readonly Dictionary<ICellEnv, int> cellVisualCounts = new Dictionary<ICellEnv, int>();
    private float nextSync;

    public IReadOnlyDictionary<AnimalsBase, AnimalVisual> Visuals => visuals;

    public AnimalSpawner Spawner
    {
        get
        {
            if (spawner == null)
                spawner = FindObjectOfType<AnimalSpawner>();
            return spawner;
        }
    }

    public GameObject GetPrefab(E_AnimalSpecies species)
    {
        switch (species)
        {
            case E_AnimalSpecies.Frog: return frogPrefab;
            case E_AnimalSpecies.Earthworm: return earthwormPrefab;
            case E_AnimalSpecies.Snail: return snailPrefab;
            case E_AnimalSpecies.Ant: return antPrefab;
            case E_AnimalSpecies.Spider: return spiderPrefab;
            case E_AnimalSpecies.Fish: return fishPrefab;
            case E_AnimalSpecies.Sandworm: return sandwormPrefab;
            case E_AnimalSpecies.Sparrow: return sparrowPrefab;
            case E_AnimalSpecies.MilkFrog: return milkFrogPrefab;
            default: return null;
        }
    }

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

    /// <summary>同步实例、位置和名称；更换或清空预设体配置也会在下次同步时生效。</summary>
    public void SyncNow()
    {
        nextSync = Time.realtimeSinceStartup + syncInterval;
        if (spawner == null)
            spawner = FindObjectOfType<AnimalSpawner>();
        if (spawner == null)
            return;

        seen.Clear();
        cellVisualCounts.Clear();
        IReadOnlyList<AnimalsBase> animals = spawner.Animals;
        for (int i = 0; i < animals.Count; i++)
            Touch(animals[i]);

        gone.Clear();
        foreach (KeyValuePair<AnimalsBase, AnimalVisual> pair in visuals)
        {
            if (!seen.Contains(pair.Key))
                gone.Add(pair.Key);
        }

        for (int i = 0; i < gone.Count; i++)
        {
            RemoveVisual(visuals[gone[i]]);
            visuals.Remove(gone[i]);
        }
    }

    [ContextMenu("立即同步一次")]
    private void SyncFromMenu()
    {
        SyncNow();
    }

    private void Touch(AnimalsBase animal)
    {
        if (animal == null || animal.IsDead || animal.CurrentCell == null)
            return;

        GameObject prefab = GetPrefab(animal.Species);
        if (prefab == null)
            return;

        seen.Add(animal);
        cellVisualCounts.TryGetValue(animal.CurrentCell, out int order);
        cellVisualCounts[animal.CurrentCell] = order + 1;

        AnimalVisual visual;
        visuals.TryGetValue(animal, out visual);
        if (visual != null && visual.SourcePrefab != prefab)
        {
            RemoveVisual(visual);
            visual = null;
        }
        if (visual == null)
        {
            GameObject go = Instantiate(prefab, transform, false);
            visual = go.GetComponent<AnimalVisual>();
            if (visual == null)
                visual = go.AddComponent<AnimalVisual>();
            visual.Bind(animal, prefab);
            visuals[animal] = visual;
        }

        // 预设体的缩放、旋转、材质、颜色与渲染排序保持用户配置。
        TileCell cell = animal.CurrentCell as TileCell;
        Vector3 origin = cell != null ? cell.WorldPosition : Vector3.zero;
        visual.transform.position = origin + OffsetFor(order);
        visual.Refresh();
    }

    private Vector3 OffsetFor(int order)
    {
        float step = spacing * order;
        return new Vector3(step, step * 0.5f, 0f);
    }

    private void RemoveVisual(AnimalVisual visual)
    {
        if (visual == null)
            return;

        // Destroy 在帧末执行，先隐藏旧实例，避免切换预设体时短暂重叠。
        visual.gameObject.SetActive(false);
        if (Application.isPlaying)
            Destroy(visual.gameObject);
        else
            DestroyImmediate(visual.gameObject);
    }
}
