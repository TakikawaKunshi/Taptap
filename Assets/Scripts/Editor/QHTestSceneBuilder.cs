#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

/// <summary>
/// 一键搭建 QH Test 场景。
///
/// 为什么需要它：QH Test.unity 原本只有一个 Main Camera，项目里也没有任何 Tile 资产，
/// 因此无法直接测试植物。手写场景 YAML 容易损坏，这里改为用 Unity 自己的 API 生成。
///
/// 地图数据来自 Taki's Test：读取它的 Tilemap 与 Tile 资产，复制到新场景，
/// 复用现有美术资源，不重新造 tile。
/// UI 与相机控制同样来自 Taki's Test：它把整套 HUD 做成了 UICanvas 预制体，
/// 这里直接实例化同一份预制体并按 Taki 的接线方式绑好引用，
/// 避免手工在 Inspector 里拖十几处引用。
/// </summary>
public static class QHTestSceneBuilder
{
    /// <summary>batchmode 下不能弹窗；统一走 Debug.Log，便于自动化验证时读取结果。</summary>
    private static void Report(string title, string message)
    {
        Debug.Log("[QHTestSceneBuilder] " + message);
        if (!Application.isBatchMode)
            EditorUtility.DisplayDialog(title, message, "好");
    }

    /// <summary>
    /// 把 QH Test 场景登记到 Build Settings。
    /// PlayMode 测试按路径加载场景，未登记的场景无法被加载。
    /// </summary>
    private static void RegisterInBuildSettings()
    {
        EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes;
        for (int i = 0; i < existing.Length; i++)
        {
            if (existing[i].path != TargetScenePath)
                continue;
            if (existing[i].enabled)
                return;
            existing[i].enabled = true;
            EditorBuildSettings.scenes = existing;
            return;
        }

        var updated = new EditorBuildSettingsScene[existing.Length + 1];
        updated[0] = new EditorBuildSettingsScene(TargetScenePath, true);
        for (int i = 0; i < existing.Length; i++)
            updated[i + 1] = existing[i];
        EditorBuildSettings.scenes = updated;
    }

    private const string SourceScenePath = "Assets/Scenes/TestScene/Taki's Test.unity";
    private const string TargetScenePath = "Assets/Scenes/TestScene/QH Test.unity";
    private const string TileAssetPath = "Assets/Resources/TestResources/Images/斜坡1.asset";

    /// <summary>Taki's Test 里整套 HUD 用的预制体；QH Test 直接实例化同一份。</summary>
    private const string UiCanvasPrefabPath = "Assets/Prefabs/UI/UICanvas.prefab";

    /// <summary>策划案第八章建议的 12x12 灰盒规模；这里用它限制铺设范围。</summary>
    private const int HalfWidth = 12;
    private const int HalfHeight = 8;

    [MenuItem("Tools/QH Test/搭建 QH Test 场景")]
    public static void Build()
    {
        // 先读坐标：这是纯文件解析，不持有任何资源引用。
        List<Vector3Int> tilePositions = ReadSourceTilePositions();
        if (tilePositions.Count == 0)
        {
            Report("搭建失败", "没有从 Taki's Test 读到任何已绘制的格子。");
            return;
        }

        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() == false)
            return;

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 新建场景会卸载旧的资源，必须在之后才加载 Tile，
        // 否则拿到的引用会立刻失效（MissingReferenceException）。
        TileBase sourceTile = ResolveSourceTile();
        if (sourceTile == null)
            return;
        string tileName = sourceTile.name;

        // ---- 相机：与 Taki's Test 一致的正交相机 ----
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.19f, 0.30f, 0.47f);
        cameraObject.AddComponent<AudioListener>();

        // ---- 相机控制：与 Taki's Test 相同；BackGround 等 Grid 建好后再绑定 ----
        CameraMove cameraMove = cameraObject.AddComponent<CameraMove>();

        // ---- 灯光：URP 下平行光保证精灵可见 ----
        var lightObject = new GameObject("Directional Light");
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;

        // ---- Grid + Tilemap，参数与 Taki's Test 一致 ----
        GameObject gridObject = new GameObject("Grid");
        gridObject.transform.position = new Vector3(-0.15f, -0.15f, 0f);
        Grid grid = gridObject.AddComponent<Grid>();
        grid.cellSize = new Vector3(1f, 1f, 0f);
        grid.cellGap = Vector3.zero;
        grid.cellLayout = GridLayout.CellLayout.Rectangle;
        // 不设置 cellSwizzle，保持默认 XY；Taki's Test 的 m_CellSwizzle 也是 0。

        GameObject tilemapObject = new GameObject("Tilemap");
        tilemapObject.transform.SetParent(gridObject.transform, false);
        Tilemap tilemap = tilemapObject.AddComponent<Tilemap>();
        TilemapRenderer tilemapRenderer = tilemapObject.AddComponent<TilemapRenderer>();
        tilemapRenderer.sortingOrder = 0;

        // ---- 刷格子：以 Taki's Test 的布局为准，并裁剪到策划案建议的规模 ----
        int painted = 0;
        foreach (Vector3Int position in tilePositions)
        {
            if (Mathf.Abs(position.x) > HalfWidth || Mathf.Abs(position.y) > HalfHeight)
                continue;
            tilemap.SetTile(position, sourceTile);
            painted++;
        }

        tilemap.CompressBounds();

        // ---- 天气对象 ----
        GameObject weatherObject = new GameObject("WeatherObject");
        WeatherBase weather = weatherObject.AddComponent<SunWeather>();

        // ---- 时间与结算 ----
        GameObject gameManager = new GameObject("GameManager");
        TimeManager timeManager = gameManager.AddComponent<TimeManager>();
        NightSettlement settlement = gameManager.AddComponent<NightSettlement>();
        SetObjectReference(settlement, "weather", weather);

        // ---- 地图管理 ----
        TileMapManager mapManager = tilemapObject.AddComponent<TileMapManager>();
        SetObjectReference(mapManager, "tilemap", tilemap);
        SetObjectReference(mapManager, "weather", weather);

        // ---- 植物投放器 ----
        PlantSpawner spawner = gameManager.AddComponent<PlantSpawner>();
        SetObjectReference(spawner, "map", mapManager);

        // ---- 朝向相机：物种父物体与 Tilemap 同级 ----
        GameObject speciesRoot = new GameObject("Species");
        speciesRoot.transform.SetParent(gridObject.transform, false);
        speciesRoot.AddComponent<FacingCamera>();

        // ---- 相机 BackGround：与 Taki's Test 一样指向地图根节点 ----
        SetObjectReference(cameraMove, "BackGround", gridObject);

        // ---- 植物结算桥接：过夜成功后驱动一次植物结算并输出晨报 ----
        PlantSettlementBridge bridge = gameManager.AddComponent<PlantSettlementBridge>();
        SetObjectReference(bridge, "timeManager", timeManager);
        SetObjectReference(bridge, "weather", weather);
        SetObjectReference(bridge, "map", mapManager);

        // ---- 植物可视化：预设体由用户在 Inspector 中按物种配置，留空不显示 ----
        PlantVisualBinder plantBinder = speciesRoot.AddComponent<PlantVisualBinder>();
        SetObjectReference(plantBinder, "map", mapManager);
        SetObjectReference(plantBinder, "bridge", bridge);

        // ---- 动物：不占格子，登记在 Animals 根物体下的投放器里 ----
        GameObject animalsRoot = new GameObject("Animals");
        animalsRoot.transform.SetParent(gridObject.transform, false);
        // 与 Species 一样朝向相机，便于使用平面精灵预设体。
        animalsRoot.AddComponent<FacingCamera>();

        AnimalSpawner animalSpawner = animalsRoot.AddComponent<AnimalSpawner>();
        SetObjectReference(animalSpawner, "map", mapManager);

        AnimalVisualBinder animalBinder = animalsRoot.AddComponent<AnimalVisualBinder>();
        SetObjectReference(animalBinder, "spawner", animalSpawner);

        // ---- 动物结算桥接：挂在植物结算之后运行（策划案第 6 至 8 步）----
        AnimalSettlementBridge animalBridge = gameManager.AddComponent<AnimalSettlementBridge>();
        SetObjectReference(animalBridge, "plantBridge", bridge);
        SetObjectReference(animalBridge, "timeManager", timeManager);
        SetObjectReference(animalBridge, "weather", weather);
        SetObjectReference(animalBridge, "map", mapManager);
        SetObjectReference(animalBridge, "spawner", animalSpawner);

        // ---- UI：实例化 Taki's Test 使用的同一份 UICanvas 预制体 ----
        GameObject canvas = InstantiateUiCanvas();
        BindUiReferences(canvas, timeManager, weatherObject, mapManager, camera, plantBinder, animalBinder);

        MessagePanel messagePanel = EnsureMessagePanel(canvas);
        if (messagePanel != null)
        {
            // 格子点击的信息输出到 InformationSV；植物晨报与动物晨报都输出到 DailySV。
            SetObjectReference(mapManager, "messagePanel", messagePanel);
            SetObjectReference(bridge, "messagePanel", messagePanel);
            SetObjectReference(animalBridge, "messagePanel", messagePanel);
        }

        // ---- EventSystem：拖放与所有 UI 点击都依赖它 ----
        CreateEventSystem();

        // ---- 保存 ----
        Directory.CreateDirectory(Path.GetDirectoryName(TargetScenePath));
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene, TargetScenePath);
        if (!saved)
        {
            Report("搭建失败", "场景保存失败。");
            return;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 把场景加入 Build Settings，否则 PlayMode 测试无法按路径加载它。
        RegisterInBuildSettings();

        Report("QH Test 搭建完成", string.Format(
            "已刷 {0} 个格子（来源 Taki's Test，Tile={1}）。\n"
            + "UI={2}\n\n下一步：进入 Play，植物和动物会自动按初始配置投放。",
            painted, tileName, canvas != null ? "UICanvas 已实例化并接线" : "未找到 UICanvas 预制体！"));
    }

    /// <summary>
    /// 实例化 Taki's Test 使用的 UICanvas 预制体。
    /// 用 InstantiatePrefab 而不是 new GameObject：预制体里的 HUD 有几十层结构，
    /// 手写不可能一致，而 Taki 的场景本身就是这份预制体的实例。
    /// </summary>
    private static GameObject InstantiateUiCanvas()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(UiCanvasPrefabPath);
        if (prefab == null)
        {
            Debug.LogError("[QHTestSceneBuilder] 找不到 UI 预制体：" + UiCanvasPrefabPath);
            return null;
        }

        GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (instance == null)
        {
            Debug.LogError("[QHTestSceneBuilder] UICanvas 预制体实例化失败。");
            return null;
        }

        instance.name = prefab.name;
        return instance;
    }

    /// <summary>
    /// 按 Taki's Test 的接线方式把场景对象绑到 UI 上。
    /// 这些字段在预制体里都是空的（TimeManager、地图、相机、天气对象都是运行时才知道的），
    /// 所以必须在这里补，否则 HUD 打开就全是警告。
    /// </summary>
    private static void BindUiReferences(GameObject canvas, TimeManager timeManager,
        GameObject weatherObject, TileMapManager mapManager, Camera camera,
        PlantVisualBinder plantBinder, AnimalVisualBinder animalBinder)
    {
        if (canvas == null)
            return;

        WeatherDropDown dropDown = canvas.GetComponentInChildren<WeatherDropDown>(true);
        if (dropDown != null)
        {
            SetObjectReference(dropDown, "weatherObject", weatherObject);
            SetObjectReference(dropDown, "timeManager", timeManager);
        }

        SpeciesList speciesList = canvas.GetComponentInChildren<SpeciesList>(true);
        if (speciesList != null)
        {
            SetObjectReference(speciesList, "timeManager", timeManager);
            SetObjectReference(speciesList, "environment", mapManager);
            SetObjectReference(speciesList, "placementCamera", camera);
            SetObjectReference(speciesList, "plantVisualBinder", plantBinder);
            SetObjectReference(speciesList, "animalVisualBinder", animalBinder);
        }

        NightSettleButton settleButton = canvas.GetComponentInChildren<NightSettleButton>(true);
        if (settleButton != null)
            SetObjectReference(settleButton, "timeManager", timeManager);

        DayIndexLabel dayIndex = canvas.GetComponentInChildren<DayIndexLabel>(true);
        if (dayIndex != null)
            SetObjectReference(dayIndex, "timeManager", timeManager);

        ActionTimes actionTimes = canvas.GetComponentInChildren<ActionTimes>(true);
        if (actionTimes != null)
            SetObjectReference(actionTimes, "timeManager", timeManager);
    }

    /// <summary>UI 的点击、拖放与滚轮都依赖 EventSystem；Taki's Test 里也有一个。</summary>
    private static void CreateEventSystem()
    {
        if (UnityEngine.Object.FindObjectOfType<EventSystem>() != null)
            return;

        var eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<StandaloneInputModule>();
    }

    /// <summary>
    /// 定位 Taki's Test 使用的 Tile 资产。
    /// 用 LoadAllAssetsAtPath 取主资产而不是 LoadAssetAtPath：Tile 会把它引用的
    /// Sprite 也加载进来，只有主 TileBase 才能安全地传给 SetTile。
    /// </summary>
    private static TileBase ResolveSourceTile()
    {
        TileBase tile = LoadTileAt(TileAssetPath);
        if (tile != null)
            return tile;

        string[] guids = AssetDatabase.FindAssets("t:Tile", new[] { "Assets" });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            tile = LoadTileAt(path);
            if (tile != null)
            {
                Debug.LogWarning("按路径没找到 Tile，改用项目内第一个 Tile：" + path);
                return tile;
            }
        }

        Report("搭建失败", "项目里找不到任何 Tile 资产。");
        return null;
    }

    /// <summary>从资产文件里挑出主 TileBase。</summary>
    private static TileBase LoadTileAt(string path)
    {
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
        foreach (UnityEngine.Object asset in assets)
        {
            // 跳过 Sprite 等子资产，只取主 TileBase。
            TileBase tile = asset as TileBase;
            if (tile != null)
                return tile;
        }

        return null;
    }

    /// <summary>
    /// 读取 Taki's Test 的已绘制格子坐标。
    /// 直接解析场景文件而不是加载场景，避免为了读数据而打开别人的场景。
    /// </summary>
    private static List<Vector3Int> ReadSourceTilePositions()
    {
        var result = new List<Vector3Int>();
        string fullPath = Path.GetFullPath(SourceScenePath);
        if (!File.Exists(fullPath))
        {
            Debug.LogError("找不到来源场景：" + SourceScenePath);
            return result;
        }

        foreach (string line in File.ReadAllLines(fullPath))
        {
            int index = line.IndexOf("- first: {x: ", StringComparison.Ordinal);
            if (index < 0)
                continue;
            // 形如：  - first: {x: -8, y: -8, z: 0}
            string payload = line.Substring(index + "- first: {x: ".Length).TrimEnd();
            payload = payload.Substring(0, payload.IndexOf('}'));
            string[] parts = payload.Split(',');
            if (parts.Length != 3)
                continue;

            // 每段都带 "y: " / "z: " 前缀，需要先去掉键名再取数值。
            System.Nullable<int> x = ReadCoordinate(parts[0], 0);
            System.Nullable<int> y = ReadCoordinate(parts[1], 1);
            System.Nullable<int> z = ReadCoordinate(parts[2], 2);
            if (!x.HasValue || !y.HasValue || !z.HasValue)
                continue;
            result.Add(new Vector3Int(x.Value, y.Value, z.Value));
        }

        return result;
    }

    /// <summary>
    /// 从 "x: -8" 这样的片段里取出数值。
    /// keyIndex 为 0 表示片段开头就是数值，其余表示前面带有第 keyIndex 个键名。
    /// </summary>
    private static System.Nullable<int> ReadCoordinate(string segment, int keyIndex)
    {
        string text = segment;
        if (keyIndex > 0)
        {
            int colon = segment.IndexOf(':');
            if (colon < 0)
                return null;
            text = segment.Substring(colon + 1);
        }

        text = text.Trim();
        int value;
        return int.TryParse(text, out value) ? value : (int?)null;
    }

    /// <summary>
    /// 给 [SerializeField] 私有字段赋值。
    /// 用 SerializedObject 而不是反射：与 Inspector 手动拖拽的行为完全一致，
    /// 并且会正确处理 Undo 与 Prefab 覆盖。
    /// </summary>
    private static void SetObjectReference(UnityEngine.Object target, string fieldName, UnityEngine.Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(fieldName);
        if (property == null)
        {
            Debug.LogErrorFormat("{0} 上找不到序列化字段 {1}", target.GetType().Name, fieldName);
            return;
        }

        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>写入 [SerializeField] 的数值字段。</summary>
    private static void SetFloatValue(UnityEngine.Object target, string fieldName, float value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(fieldName);
        if (property == null)
        {
            Debug.LogErrorFormat("{0} 上找不到序列化字段 {1}", target.GetType().Name, fieldName);
            return;
        }

        property.floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>写入 [SerializeField] 的颜色字段。</summary>
    private static void SetColorValue(UnityEngine.Object target, string fieldName, Color value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(fieldName);
        if (property == null)
        {
            Debug.LogErrorFormat("{0} 上找不到序列化字段 {1}", target.GetType().Name, fieldName);
            return;
        }

        property.colorValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>按名字递归查找子物体；UI 层级很深，直接 Find 容易漏。</summary>
    private static Transform FindChildRecursive(Transform root, string name)
    {
        if (root == null)
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == name)
                return child;

            Transform found = FindChildRecursive(child, name);
            if (found != null)
                return found;
        }
        return null;
    }

    /// <summary>
    /// 保证场景里有一个能真正显示消息的 MessagePanel。
    ///
    /// 为什么需要它：UICanvas 预制体里只有名为 MessagePanel 的空 GameObject，
    /// MessagePanel 组件是 Taki 在 Taki's Test 场景里手动加上的（场景级 override，不在预制体里）；
    /// 而且预制体只做了 InformationSV，DailySV / FoodChainSV 同样是 Taki 在场景里另建的。
    /// 少了这些，格子点击信息与植物晨报都会因为找不到对应分类的 Scroll View 而被丢弃。
    /// 这里按 Taki 的做法补齐：克隆 InformationSV 生成另外两个分类的视图，再挂上组件。
    /// </summary>
    private static MessagePanel EnsureMessagePanel(GameObject canvas)
    {
        if (canvas == null)
            return null;

        Transform panelTransform = FindChildRecursive(canvas.transform, "MessagePanel");
        if (panelTransform == null)
        {
            Debug.LogWarning("[QHTestSceneBuilder] UICanvas 里没有名为 MessagePanel 的子物体。");
            return null;
        }

        Transform contentPanel = FindChildRecursive(panelTransform, "ContentPanel");
        if (contentPanel == null)
        {
            Debug.LogWarning("[QHTestSceneBuilder] MessagePanel 下没有 ContentPanel。");
        }
        else
        {
            Transform template = FindChildRecursive(contentPanel, "InformationSV");
            if (template == null)
            {
                Debug.LogWarning("[QHTestSceneBuilder] 找不到 InformationSV，无法克隆其它分类的 Scroll View。");
            }
            else
            {
                int order = template.GetSiblingIndex();
                CreateMessageScrollView(template, contentPanel, "DailySV", order + 1);
                CreateMessageScrollView(template, contentPanel, "FoodChainSV", order + 2);
            }
        }

        MessagePanel panel = panelTransform.GetComponent<MessagePanel>();
        if (panel == null)
            panel = panelTransform.gameObject.AddComponent<MessagePanel>();

        // 组件会按名称自动解析 MessageBar/<Tag>Btn 与 ContentPanel/<Tag>SV，
        // 这里只补预制体里没有的字号与颜色，与 Taki's Test 保持一致。
        SetFloatValue(panel, "fontSize", 20f);
        SetColorValue(panel, "fontColor", new Color(0.2f, 0.2f, 0.2f, 1f));
        return panel;
    }

    /// <summary>克隆一个 Scroll View 作为其它消息分类的容器，并清掉克隆带来的示例内容。</summary>
    private static void CreateMessageScrollView(Transform template, Transform parent, string name, int siblingIndex)
    {
        if (FindChildRecursive(parent, name) != null)
            return;

        GameObject clone = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
        clone.name = name;
        clone.transform.SetSiblingIndex(siblingIndex);

        ScrollRect scroll = clone.GetComponent<ScrollRect>();
        if (scroll == null || scroll.content == null)
            return;

        for (int i = scroll.content.childCount - 1; i >= 0; i--)
            UnityEngine.Object.DestroyImmediate(scroll.content.GetChild(i).gameObject);
    }
}
#endif
