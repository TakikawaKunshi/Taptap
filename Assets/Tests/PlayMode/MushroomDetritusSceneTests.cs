using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

public class MushroomDetritusSceneTests
{
    [TestCase(0, 0, 4)]
    [TestCase(1, 1, 2)]
    public void ConfiguredCapProducesVisibleDetritusOnExistingCardinalTiles(int x, int y, int count)
    {
        var root = new GameObject("Mushroom detritus scene test");
        Tile tile = ScriptableObject.CreateInstance<Tile>();
        try
        {
            WeatherBase weather = root.AddComponent<WeatherBase>();
            weather.weatherType = E_WeatherType.Rain;
            GameObject grid = Child("Grid", root.transform);
            grid.AddComponent<Grid>();
            GameObject ground = Child("Ground", grid.transform);
            Tilemap tilemap = ground.AddComponent<Tilemap>();
            for (int row = -1; row <= 1; row++)
                for (int column = -1; column <= 1; column++)
                    tilemap.SetTile(new Vector3Int(column, row, 0), tile);
            TileMapManager map = ground.AddComponent<TileMapManager>();
            Set(map, "weather", weather);
            map.InitializeCells();
            Assert.IsTrue(map.TryGetCell(new Vector2Int(x, y), out TileCell source));
            var mushroom = new Mushroom();
            Assert.IsTrue(PlantSettler.Place(mushroom, source));

            PlantSettlementBridge bridge = root.AddComponent<PlantSettlementBridge>();
            Set(bridge, "map", map);
            Set(bridge, "weather", weather);
            Set(bridge, "verboseLog", false);
            Set(bridge, "publishMorningReport", false);
            Set(bridge, "mushroomMaxVitality", 3);
            GameObject templates = Child("Templates", root.transform);
            templates.SetActive(false);
            GameObject prefab = Child("Detritus prefab", templates.transform);
            prefab.AddComponent<SpriteRenderer>();
            PlantVisualBinder binder = Child("Visuals", root.transform).AddComponent<PlantVisualBinder>();
            Set(binder, "map", map);
            Set(binder, "detritusPrefab", prefab);

            PlantSettleReport report = bridge.SettleDay(1, E_WeatherType.Rain);
            binder.SyncNow();
            Assert.AreEqual(3, mushroom.Vitality);
            Assert.AreEqual(count, report.Produced[E_ResourceKind.Detritus]);
            Assert.AreEqual(count, binder.DetritusVisuals.Count);
            foreach (TileCell cell in map.Cells)
            {
                Vector2Int offset = cell.Coordinates - source.Coordinates;
                bool isNeighbor = Mathf.Abs(offset.x) + Mathf.Abs(offset.y) == 1;
                Assert.AreEqual(isNeighbor ? 1 : 0, cell.Detritus);
                if (!isNeighbor) continue;
                GameObject visual = binder.DetritusVisuals[cell];
                Assert.IsTrue(visual.activeInHierarchy);
                Assert.AreEqual(cell.WorldPosition, visual.transform.position);
            }

            Assert.IsNull(bridge.SettleDay(1, E_WeatherType.Rain));
            bridge.SettleDay(2, E_WeatherType.Rain);
            binder.SyncNow();
            Assert.AreEqual(3, mushroom.Vitality);
            Assert.AreEqual(count, binder.DetritusVisuals.Count, "同一格只显示一堆，不重复创建外观。");
            foreach (ICellEnv neighbor in source.Neighbors) Assert.AreEqual(2, neighbor.Detritus);
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(tile);
        }
    }

    private static GameObject Child(string name, Transform parent)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child;
    }

    private static void Set(object target, string field, object value)
    {
        target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    }
}
