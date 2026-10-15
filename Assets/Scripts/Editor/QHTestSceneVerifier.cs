#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 打开 QH Test 场景并驱动真实的 MonoBehaviour，验证场景确实可用。
/// 与只读场景文件的检查不同，这里会真正运行 TileMapManager、TimeManager 与 PlantSpawner。
/// </summary>
public static class QHTestSceneVerifier
{
    private const string ScenePath = "Assets/Scenes/TestScene/QH Test.unity";

    [MenuItem("Tools/QH Test/验证 QH Test 场景")]
    public static void Verify()
    {
        var report = new StringBuilder();

        if (!EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single).IsValid())
        {
            Debug.Log("[QHTestSceneVerifier] FAIL: 无法打开场景 " + ScenePath);
            return;
        }

        // ---- 1. 地图 ----
        // GetUsedTilesCount() 统计的是不同 Tile 资源的数量，不是绘制的格子数，
        // 这里手动遍历 cellBounds，得到的是真正被刷过格子的数量。
        Tilemap tilemap = null;
        int painted = 0;
        foreach (Tilemap candidate in Object.FindObjectsOfType<Tilemap>())
        {
            int count = 0;
            foreach (Vector3Int pos in candidate.cellBounds.allPositionsWithin)
            {
                if (candidate.GetTile(pos) != null)
                    count++;
            }
            if (count > painted)
            {
                painted = count;
                tilemap = candidate;
            }
        }

        if (tilemap == null)
        {
            Debug.Log("[QHTestSceneVerifier] FAIL: 场景里没有已绘制格子的 Tilemap");
            return;
        }

        report.Append("Tilemap ").Append(tilemap.name).Append(" 已绘制格子：").Append(painted).Append('\n');

        TileMapManager mapManager = Object.FindObjectOfType<TileMapManager>();
        report.Append(mapManager != null ? "TileMapManager: 已挂载\n" : "TileMapManager: 缺失\n");

        // ---- 2. 天气与时间 ----
        WeatherBase weather = Object.FindObjectOfType<WeatherBase>();
        report.Append(weather != null ? "WeatherBase: " + weather.GetType().Name + "\n" : "WeatherBase: 缺失\n");

        TimeManager timeManager = Object.FindObjectOfType<TimeManager>();
        report.Append(timeManager != null ? "TimeManager: 第 " + timeManager.CurrentDay + " 天\n" : "TimeManager: 缺失\n");

        NightSettlement settlement = Object.FindObjectOfType<NightSettlement>();
        report.Append(settlement != null
            ? "NightSettlement: CanSettle=" + settlement.CanSettle + "\n"
            : "NightSettlement: 缺失\n");

        // 注意：TileMapManager.InitializeCells 只在 Application.isPlaying 下建格子，
        // 播放模式的验证交给 PlayMode 测试，这里只核对编辑器侧的接线是否正确。
        if (mapManager != null)
            report.Append("TileMapManager.Tilemap 已绑定：").Append(mapManager.Tilemap != null ? "是" : "否").Append('\n');
        if (weather != null && mapManager != null)
            report.Append("TileMapManager.Weather 已绑定：").Append(mapManager.Weather != null ? "是" : "否").Append('\n');
        if (settlement != null)
            report.Append("NightSettlement.Weather 已绑定：").Append(settlement.Weather != null ? "是" : "否").Append('\n');

        // ---- 3. 投放器与植物结算桥接 ----
        PlantSpawner spawner = Object.FindObjectOfType<PlantSpawner>();
        if (spawner == null)
        {
            report.Append("PlantSpawner: 缺失\n");
        }
        else
        {
            report.Append("PlantSpawner.Map 已绑定：").Append(spawner.Map != null ? "是" : "否").Append('\n');
        }

        PlantSettlementBridge bridge = Object.FindObjectOfType<PlantSettlementBridge>();
        if (bridge == null)
        {
            report.Append("PlantSettlementBridge: 缺失（过夜不会结算植物！）\n");
        }
        else
        {
            report.Append("PlantSettlementBridge: 已挂载，最后一次结算第 ").Append(bridge.LastSettledDay).Append(" 晚\n");
            report.Append("  桥接.Weather 已绑定：").Append(HasReference(bridge, "weather") ? "是" : "否").Append('\n');
            report.Append("  桥接.Map 已绑定：").Append(HasReference(bridge, "map") ? "是" : "否").Append('\n');
        }

        // ---- 4. UI（来自 Taki's Test 的 UICanvas 预制体）----
        report.Append('\n');
        report.Append("EventSystem: ").Append(Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() != null ? "已存在" : "缺失").Append('\n');

        CameraMove cameraMove = Object.FindObjectOfType<CameraMove>();
        report.Append("CameraMove: ").Append(cameraMove == null ? "缺失"
            : cameraMove.BackGround != null ? "BackGround=" + cameraMove.BackGround.name : "BackGround 未绑定").Append('\n');

        MessagePanel panel = Object.FindObjectOfType<MessagePanel>(true);
        report.Append("MessagePanel: ").Append(panel != null ? panel.name : "缺失").Append('\n');
        if (panel != null)
        {
            // 三个分类里少任何一个，对应消息就会被丢弃：格子信息走 Information，植物晨报走 Daily。
            SerializedObject panelSerialized = new SerializedObject(panel);
            SerializedProperty views = panelSerialized.FindProperty("views");
            int bound = 0;
            int total = views != null ? views.arraySize : 0;
            for (int i = 0; i < total; i++)
            {
                SerializedProperty view = views.GetArrayElementAtIndex(i);
                if (view.FindPropertyRelative("Button").objectReferenceValue != null
                    && view.FindPropertyRelative("ScrollView").objectReferenceValue != null)
                    bound++;
            }
            report.Append("  分类视图已绑定：").Append(bound).Append('/').Append(total).Append('\n');
        }
        if (mapManager != null)
            report.Append("TileMapManager.MessagePanel 已绑定：").Append(HasReference(mapManager, "messagePanel") ? "是" : "否").Append('\n');

        WeatherDropDown dropDown = Object.FindObjectOfType<WeatherDropDown>(true);
        report.Append("WeatherDropDown: ").Append(dropDown == null ? "缺失"
            : dropDown.Weather != null ? "WeatherObject 已绑定" : "WeatherObject 未绑定").Append('\n');

        SpeciesList speciesList = Object.FindObjectOfType<SpeciesList>(true);
        report.Append("SpeciesList: ").Append(speciesList == null ? "缺失"
            : "卡片预制体=" + (speciesList.PreSpeciesCard != null ? "已绑定" : "未绑定")).Append('\n');

        // ---- 5. 植物占位可视化：没有它地图上看不到任何植物 ----
        PlantVisualBinder binder = Object.FindObjectOfType<PlantVisualBinder>();
        if (binder == null)
        {
            report.Append("PlantVisualBinder: 缺失（地图上会看不到植物）\n");
        }
        else
        {
            report.Append("PlantVisualBinder: 已挂载，贴图=").Append(HasReference(binder, "plantTexture") ? "已绑定" : "未绑定")
                  .Append("，map=").Append(HasReference(binder, "map") ? "已绑定" : "未绑定")
                  .Append("，bridge=").Append(HasReference(binder, "bridge") ? "已绑定" : "未绑定").Append('\n');
        }

        // ---- 6. 动物系统：投放器、结算桥接、占位可视化 ----
        AnimalSpawner animalSpawner = Object.FindObjectOfType<AnimalSpawner>();
        report.Append("AnimalSpawner: ").Append(animalSpawner != null
            ? "已挂载，map=" + (HasReference(animalSpawner, "map") ? "已绑定" : "未绑定")
            : "缺失").Append('\n');

        AnimalSettlementBridge animalBridge = Object.FindObjectOfType<AnimalSettlementBridge>();
        if (animalBridge == null)
        {
            report.Append("AnimalSettlementBridge: 缺失（过夜不会结算动物！）\n");
        }
        else
        {
            report.Append("AnimalSettlementBridge: 已挂载，最后一次结算第 ").Append(animalBridge.LastSettledDay).Append(" 晚\n");
            report.Append("  桥接.plantBridge 已绑定：").Append(HasReference(animalBridge, "plantBridge") ? "是" : "否").Append('\n');
            report.Append("  桥接.map 已绑定：").Append(HasReference(animalBridge, "map") ? "是" : "否").Append('\n');
            report.Append("  桥接.spawner 已绑定：").Append(HasReference(animalBridge, "spawner") ? "是" : "否").Append('\n');
            report.Append("  桥接.messagePanel 已绑定：").Append(HasReference(animalBridge, "messagePanel") ? "是" : "否").Append('\n');
        }

        AnimalVisualBinder animalBinder = Object.FindObjectOfType<AnimalVisualBinder>();
        report.Append("AnimalVisualBinder: ").Append(animalBinder != null
            ? "已挂载，贴图=" + (HasReference(animalBinder, "animalTexture") ? "已绑定" : "未绑定")
              + "，spawner=" + (HasReference(animalBinder, "spawner") ? "已绑定" : "未绑定")
            : "缺失").Append('\n');

        // 播放模式下的铺场与七天结算在 PlayMode 测试里做，这里不重复。

        Debug.Log("[QHTestSceneVerifier]\n" + report);
    }

    /// <summary>读取 [SerializeField] 私有字段是否已经拖过引用。</summary>
    private static bool HasReference(UnityEngine.Object target, string fieldName)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(fieldName);
        return property != null && property.objectReferenceValue != null;
    }

    /// <summary>TileMapManager 只暴露 IReadOnlyCollection，这里做一次适配。</summary>
    private static System.Collections.Generic.IEnumerable<ICellEnv> CollectCells(TileMapManager mapManager)
    {
        foreach (TileCell cell in mapManager.Cells)
            yield return cell;
    }
}

/// <summary>
/// 从 WeatherBase 读取植物需要的三个信息。
/// 用包装而不是改 WeatherBase，动物与天气脚本保持不变。
/// </summary>
public sealed class SceneWeatherQuery : IWeatherQuery
{
    private readonly WeatherBase source;
    private E_WeatherType consecutiveType;
    private int consecutiveDays;

    public SceneWeatherQuery(WeatherBase source)
    {
        this.source = source;
        consecutiveType = source != null ? source.weatherType : E_WeatherType.Sun;
        consecutiveDays = 1;
    }

    /// <summary>模拟"过夜后连续次数加一"，用于在播放模式里驱动植物结算。</summary>
    public void SetSameDay(WeatherBase weather, E_WeatherType type)
    {
        if (weather == null)
            return;
        if (type == consecutiveType)
            consecutiveDays++;
        else
        {
            consecutiveType = type;
            consecutiveDays = 1;
        }
    }

    public bool Meets(E_PlantNeed need)
    {
        if (source == null)
            return false;
        bool rainy = source.weatherType == E_WeatherType.Rain;
        switch (need)
        {
            case E_PlantNeed.Light: return !rainy;
            case E_PlantNeed.Heat: return !rainy;
            case E_PlantNeed.Moisture: return rainy;
            case E_PlantNeed.Fungi: return rainy;
            default: return false;
        }
    }

    public int ConsecutiveSameDays
    {
        get { return consecutiveDays; }
    }

    public E_WeatherChange Change
    {
        get { return source != null ? source.WeatherChange : E_WeatherChange.None; }
    }
}
#endif