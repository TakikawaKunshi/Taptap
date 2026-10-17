using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

public class EcosystemPresentationTests
{
    private GameObject root;
    private Tile tile;
    private Texture2D texture;
    private Sprite sprite;
    private GameObject prefab;
    private TileMapManager map;
    private TileCell center;
    private AnimalSpawner spawner;
    private AnimalVisualBinder binder;
    private PlantVisualBinder plants;
    private AnimalSettlementBridge bridge;
    private TimeManager time;
    private EcosystemAmbientController ambient;
    private Camera camera;
    private Random.State randomState;

    [SetUp]
    public void SetUp()
    {
        randomState = Random.state;
        Random.InitState(1731);
        root = new GameObject("Ecosystem presentation test");
        GameObject grid = Child("Grid");
        grid.AddComponent<Grid>();
        GameObject ground = Child("Map", grid.transform);
        Tilemap tilemap = ground.AddComponent<Tilemap>();
        tile = ScriptableObject.CreateInstance<Tile>();
        for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++) tilemap.SetTile(new Vector3Int(x, y, 0), tile);
        map = ground.AddComponent<TileMapManager>();
        map.InitializeCells();
        Assert.IsTrue(map.TryGetCell(Vector2Int.zero, out center));
        time = Child("Time").AddComponent<TimeManager>();
        spawner = Child("Spawner").AddComponent<AnimalSpawner>();
        Set(spawner, "spawnOnStart", false);
        Set(spawner, "map", map);
        binder = Child("Ecosystem").AddComponent<AnimalVisualBinder>();
        Set(binder, "spawner", spawner);
        plants = binder.gameObject.AddComponent<PlantVisualBinder>();
        Set(plants, "map", map);

        texture = new Texture2D(2, 2);
        texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
        texture.Apply();
        sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f, 2f);
        GameObject templates = Child("Templates");
        templates.SetActive(false);
        prefab = Child("Species prefab", templates.transform);
        Child("Art", prefab.transform).AddComponent<SpriteRenderer>().sprite = sprite;
        foreach (string name in new[] { "frog", "earthworm", "snail", "ant", "spider", "milkFrog" })
            Set(binder, name + "Prefab", prefab);
        Set(plants, "mushroomPrefab", prefab);
        Set(plants, "detritusPrefab", prefab);

        bridge = Child("Settlement").AddComponent<AnimalSettlementBridge>();
        Set(bridge, "map", map);
        Set(bridge, "spawner", spawner);
        Set(bridge, "publishMorningReport", false);
        ambient = binder.gameObject.AddComponent<EcosystemAmbientController>();
        Set(ambient, "settlementBridge", bridge);
        Set(ambient, "timeManager", time);
        Set(ambient, "idleDelay", 0f);
        Set(ambient, "restSeconds", Vector2.zero);
        Set(ambient, "moveSpeed", 1f);

        camera = Child("Camera").AddComponent<Camera>();
        camera.transform.position = center.WorldPosition + Vector3.back * 10f;
        camera.orthographic = true;
        camera.orthographicSize = 4f;
        camera.pixelRect = new Rect(0, 0, 256, 256);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
        Object.DestroyImmediate(sprite);
        Object.DestroyImmediate(texture);
        Object.DestroyImmediate(tile);
        Time.timeScale = 1f;
        Random.state = randomState;
    }

    [TestCase(E_AnimalSpecies.Frog)]
    [TestCase(E_AnimalSpecies.Earthworm)]
    [TestCase(E_AnimalSpecies.Snail)]
    [TestCase(E_AnimalSpecies.Ant)]
    [TestCase(E_AnimalSpecies.Spider)]
    [TestCase(E_AnimalSpecies.MilkFrog)]
    public void EverySupportedAnimalWalksSmoothlyAndCommitsCellOnlyOnArrival(E_AnimalSpecies species)
    {
        AnimalsBase animal = Spawn(species);
        AnimalVisual visual = binder.Visuals[animal];
        Vector3 start = visual.transform.position;
        ambient.Tick(0.2f, false);
        float firstStep = Vector3.Distance(start, visual.transform.position);
        Assert.Greater(firstStep, 0f);
        Assert.Less(firstStep, 0.2f);
        Assert.AreSame(center, animal.CurrentCell);
        int satiety = animal.Satiety;
        for (int i = 0; i < 80 && animal.CurrentCell == center; i++)
        {
            Vector3 before = visual.transform.position;
            binder.SyncNow();
            ambient.Tick(0.05f, false);
            Assert.LessOrEqual(Vector3.Distance(before, visual.transform.position), 0.074f,
                "最快物种的平滑移动速度也不应超过每秒 1.45 单位。");
        }
        Assert.AreNotSame(center, animal.CurrentCell);
        Assert.AreEqual(1, Mathf.Abs(animal.Coordinates.x) + Mathf.Abs(animal.Coordinates.y));
        Assert.Less(Vector3.Distance(((TileCell)animal.CurrentCell).WorldPosition, visual.transform.position), 0.001f);
        Assert.AreEqual(satiety, animal.Satiety);
        Assert.AreEqual(0, animal.LastSettledDay);
        Assert.AreEqual(0, animal.Starvation);
    }

    [Test]
    public void InputPausesInPlaceAndIdleDelayResumesEvenWithoutActions()
    {
        AnimalsBase animal = Spawn(E_AnimalSpecies.Frog);
        ambient.Tick(0.4f, false);
        Vector3 moving = binder.Visuals[animal].transform.position;
        Set(ambient, "idleDelay", 1.5f);
        ambient.Tick(0.4f, true);
        ambient.Tick(1f, false);
        Assert.AreEqual(moving, binder.Visuals[animal].transform.position);
        time.ConfigureDailyActions(0);
        Assert.IsFalse(time.CanOperate);
        ambient.Tick(0.6f, false);
        Assert.AreNotEqual(moving, binder.Visuals[animal].transform.position);
        moving = binder.Visuals[animal].transform.position;
        Time.timeScale = 0f;
        ambient.Tick(1f, false);
        Assert.AreEqual(moving, binder.Visuals[animal].transform.position);
    }

    [Test]
    public void SpeciesMoveOnTheirOwnSchedulesAndUseDifferentSpeeds()
    {
        Set(ambient, "restSeconds", Vector2.one);
        AnimalsBase ant = Spawn(E_AnimalSpecies.Ant);
        AnimalsBase snail = Spawn(E_AnimalSpecies.Snail);
        Vector3 antStart = binder.Visuals[ant].transform.position;
        Vector3 snailStart = binder.Visuals[snail].transform.position;
        ambient.Tick(0.6f, false);
        Assert.Greater(Vector3.Distance(antStart, binder.Visuals[ant].transform.position), 0.05f);
        Assert.AreEqual(snailStart, binder.Visuals[snail].transform.position, "蜗牛自己的停留尚未结束。");
        Assert.AreEqual(0, ant.LastSettledDay);
        Assert.AreEqual(0, snail.LastSettledDay);
    }

    [Test]
    public void HungryMilkFrogSmoothlyApproachesPreyAndPausesForInput()
    {
        AnimalsBase milk = Spawn(E_AnimalSpecies.MilkFrog);
        Set(milk, "satiety", 0);
        map.TryGetCell(Vector2Int.right, out TileCell target);
        AnimalsBase prey = Spawn(E_AnimalSpecies.Frog, target);
        Vector3 start = binder.Visuals[milk].transform.position;
        ambient.Tick(0.2f, false);
        Vector3 displayed = binder.Visuals[milk].transform.position;
        Assert.Greater(Vector3.Dot(displayed - start, target.WorldPosition - start), 0f);
        Assert.Less(Vector3.Distance(start, displayed), 0.2f);
        Assert.AreSame(center, milk.CurrentCell);
        ambient.Tick(1f, true);
        Assert.AreEqual(displayed, binder.Visuals[milk].transform.position);
        Assert.AreEqual(0, milk.Satiety);
        Assert.AreEqual(0, milk.LastSettledDay);
        Assert.IsFalse(prey.IsDead);
        Assert.AreSame(binder.Visuals[milk], SpeciesVisualPicker.Pick(map, camera,
            camera.WorldToScreenPoint(displayed)));
    }

    [Test]
    public void MilkFrogPatrolAvoidsImmediateBacktracking()
    {
        AnimalsBase milk = Spawn(E_AnimalSpecies.MilkFrog);
        ambient.Tick(5f, false);
        Assert.AreNotSame(center, milk.CurrentCell);
        ambient.Tick(5f, false);
        Assert.AreNotSame(center, milk.CurrentCell, "有其他通路时不应立即退回刚离开的格子。");
    }

    [Test]
    public void NightAndExternalSettlementCancelOldDestination()
    {
        AnimalsBase animal = Spawn(E_AnimalSpecies.Frog);
        ambient.Tick(0.5f, false);
        Set(time, "<Phase>k__BackingField", DayPhase.NightSettlement);
        ambient.Tick(10f, false);
        Assert.AreSame(center, animal.CurrentCell);
        Assert.AreEqual(center.WorldPosition, binder.Visuals[animal].transform.position);
        map.TryGetCell(new Vector2Int(-1, -1), out TileCell other);
        animal.MoveTo(other);
        ambient.Tick(10f, false);
        Assert.AreSame(other, animal.CurrentCell);
        Assert.AreEqual(other.WorldPosition, binder.Visuals[animal].transform.position);
    }

    [Test]
    public void ObstaclesBlockFrogButClimbingAnimalsStillWalkAndMapBoundaryIsRespected()
    {
        foreach (ICellEnv neighbor in center.Neighbors)
            Assert.IsTrue(PlantSettler.Place(new Angiosperm(), neighbor));
        AnimalsBase frog = Spawn(E_AnimalSpecies.Frog);
        for (int i = 0; i < 15; i++) ambient.Tick(0.5f, false);
        Assert.AreEqual(center.WorldPosition, binder.Visuals[frog].transform.position);
        AnimalsBase ant = Spawn(E_AnimalSpecies.Ant);
        bool moved = false;
        for (int i = 0; i < 200; i++)
        {
            ambient.Tick(0.2f, false);
            moved |= ant.CurrentCell != center;
            Assert.IsTrue(map.TryGetCell(ant.Coordinates, out TileCell registered));
            Assert.AreSame(registered, ant.CurrentCell);
        }
        Assert.IsTrue(moved);
    }

    [Test]
    public void NewObstacleDuringStepReturnsSmoothlyToOrigin()
    {
        AnimalsBase animal = Spawn(E_AnimalSpecies.Frog);
        ambient.Tick(0.5f, false);
        foreach (ICellEnv neighbor in center.Neighbors) PlantSettler.Place(new Angiosperm(), neighbor);
        for (int i = 0; i < 100; i++)
        {
            Vector3 previous = binder.Visuals[animal].transform.position;
            ambient.Tick(0.05f, false);
            Assert.LessOrEqual(Vector3.Distance(previous, binder.Visuals[animal].transform.position), 0.064f);
        }
        Assert.AreSame(center, animal.CurrentCell);
        Assert.AreEqual(center.WorldPosition, binder.Visuals[animal].transform.position);
    }

    [Test]
    public void RandomRoutesUseMultipleDirectionsAndNewDropsJoinAutomatically()
    {
        AnimalsBase first = Spawn(E_AnimalSpecies.Frog);
        var directions = new HashSet<Vector2Int>();
        for (int i = 0; i < 24; i++)
        {
            first.MoveTo(center);
            ambient.enabled = false;
            ambient.enabled = true;
            ambient.Tick(5f, false);
            directions.Add(first.Coordinates);
        }
        Assert.Greater(directions.Count, 1);
        AnimalsBase added = Spawn(E_AnimalSpecies.MilkFrog);
        ambient.Tick(5f, false);
        Assert.AreNotSame(center, added.CurrentCell);
    }

    [Test]
    public void MovingAnimalRemainsPickableBeforeLateUpdateAfterBinderSynchronization()
    {
        AnimalsBase animal = Spawn(E_AnimalSpecies.Frog);
        ambient.Tick(0.7f, false);
        AnimalVisual visual = binder.Visuals[animal];
        Vector3 displayed = visual.transform.position;
        ambient.SendMessage("Update");
        binder.SendMessage("Update");
        Assert.AreEqual(displayed, visual.transform.position);
        Assert.AreSame(visual, SpeciesVisualPicker.Pick(map, camera, camera.WorldToScreenPoint(displayed)));
    }

    [TestCase(E_AnimalSpecies.Frog)]
    [TestCase(E_AnimalSpecies.MilkFrog)]
    public void SuccessfulPredationImmediatelyRemovesPreyWithoutDoubleRecycling(E_AnimalSpecies species)
    {
        AnimalsBase hunter = Spawn(species);
        Set(hunter, "satiety", 0);
        map.TryGetCell(Vector2Int.right, out TileCell target);
        AnimalsBase prey = Spawn(E_AnimalSpecies.Earthworm, target);
        AnimalVisual old = binder.Visuals[prey];
        AnimalSettleReport report = bridge.SettleDay(1, E_WeatherType.Sun);
        Assert.AreEqual(1, report.HuntedCount);
        Assert.IsTrue(prey.IsDead);
        Assert.IsFalse(old.gameObject.activeSelf, "不等待 0.25 秒或下一帧。");
        Assert.IsFalse(binder.Visuals.ContainsKey(prey));
        CollectionAssert.DoesNotContain(spawner.Animals, prey);
        int detritus = TotalDetritus();
        ambient.Tick(0f, true);
        ambient.Tick(0f, true);
        Assert.AreEqual(prey.RecycleYield, detritus);
        Assert.AreEqual(detritus, TotalDetritus());
    }

    [Test]
    public void DeathOutsideBridgeAndPlantEatenToZeroAreCleanedBeforeRendering()
    {
        AnimalsBase animal = Spawn(E_AnimalSpecies.Snail);
        AnimalVisual old = binder.Visuals[animal];
        var mushroom = new Mushroom();
        Assert.IsTrue(PlantSettler.Place(mushroom, center));
        plants.SyncNow();
        PlantVisual oldPlant = plants.Visuals[mushroom];
        Assert.IsTrue(animal.TakeBite(2));
        Assert.IsTrue(mushroom.TakeBite(mushroom.Vitality));
        ambient.Tick(0f, false);
        Assert.IsFalse(old.gameObject.activeSelf);
        Assert.IsFalse(oldPlant.gameObject.activeSelf);
        Assert.AreEqual(0, spawner.LiveCount);
        Assert.IsFalse(plants.Visuals.ContainsKey(mushroom));
    }

    [UnityTest]
    public IEnumerator RuntimeLoopKeepsMovingDespitePeriodicBinderUpdates()
    {
        AnimalsBase animal = Spawn(E_AnimalSpecies.Ant);
        Vector3 start = binder.Visuals[animal].transform.position;
        yield return new WaitForSeconds(0.8f);
        Assert.Greater(Vector3.Distance(start, binder.Visuals[animal].transform.position), 0.01f);
        Assert.AreEqual(0, animal.LastSettledDay);
    }

    [TestCase(false, 0f)]
    [TestCase(false, -45f)]
    [TestCase(true, 0f)]
    [TestCase(true, -45f)]
    public void CameraIncludesNearAndFarBoundsWithoutChangingFreeZoom(bool orthographic, float angle)
    {
        camera.orthographic = orthographic;
        camera.transform.rotation = Quaternion.Euler(angle, 0f, 0f);
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 10f;
        Transform near = SpriteObject("Near", 0.15f);
        Transform far = SpriteObject("Far", 30f);
        near.gameObject.layer = 8;
        camera.cullingMask &= ~(1 << 8);
        float[] distances = camera.layerCullDistances;
        distances[8] = 0.1f;
        camera.layerCullDistances = distances;
        var visibility = camera.gameObject.AddComponent<EcosystemCameraVisibility>();
        Set(visibility, "ecosystemRoot", binder.transform);
        Vector3 position = camera.transform.position;
        Quaternion rotation = camera.transform.rotation;
        foreach (float zoom in new[] { 0.5f, 3f, 8f })
        {
            camera.orthographicSize = zoom;
            camera.fieldOfView = 35f + zoom;
            visibility.RefreshNow();
            Assert.AreEqual(zoom, camera.orthographicSize);
            Assert.AreEqual(35f + zoom, camera.fieldOfView);
            Assert.AreEqual(position, camera.transform.position);
            Assert.AreEqual(rotation, camera.transform.rotation);
            Assert.Greater(camera.farClipPlane, 30f);
            Assert.Less(camera.nearClipPlane, 0.1f);
            Plane[] frustum = GeometryUtility.CalculateFrustumPlanes(camera);
            Assert.IsTrue(GeometryUtility.TestPlanesAABB(frustum, near.GetComponent<SpriteRenderer>().bounds));
            Assert.IsTrue(GeometryUtility.TestPlanesAABB(frustum, far.GetComponent<SpriteRenderer>().bounds));
        }
        Assert.AreNotEqual(0, camera.cullingMask & (1 << 8));
        Assert.AreEqual(0f, camera.layerCullDistances[8]);
        Assert.IsFalse(camera.useOcclusionCulling);
        visibility.enabled = false;
        Assert.AreEqual(0.3f, camera.nearClipPlane);
        Assert.AreEqual(10f, camera.farClipPlane);
        Assert.AreEqual(0, camera.cullingMask & (1 << 8));
        Assert.AreEqual(0.1f, camera.layerCullDistances[8]);
        Assert.IsTrue(camera.useOcclusionCulling);
    }

    [Test]
    public void SpriteSortingStaysAboveGroundAndPreservesInternalOrderAndExistingGroups()
    {
        Transform visual = SpriteObject("Billboard", 3f);
        SpriteRenderer art = visual.GetComponent<SpriteRenderer>();
        art.sortingOrder = -3;
        SpriteRenderer detail = Child("Detail", visual).AddComponent<SpriteRenderer>();
        detail.sprite = sprite;
        detail.sortingOrder = 7;
        Transform existing = SpriteObject("Custom group", 3f);
        SortingGroup custom = existing.gameObject.AddComponent<SortingGroup>();
        custom.sortingOrder = 21;
        SpriteRenderer ground = Child("Ground renderer").AddComponent<SpriteRenderer>();
        ground.sortingOrder = 4;
        var visibility = camera.gameObject.AddComponent<EcosystemCameraVisibility>();
        Set(visibility, "ecosystemRoot", binder.transform);
        Set(visibility, "groundRenderer", ground);
        visibility.RefreshNow();
        Assert.Greater(visual.GetComponent<SortingGroup>().sortingOrder, ground.sortingOrder);
        Assert.AreEqual(-3, art.sortingOrder);
        Assert.AreEqual(7, detail.sortingOrder);
        Assert.AreEqual(21, custom.sortingOrder);
        visibility.enabled = false;
        Assert.IsFalse(visual.GetComponent<SortingGroup>().enabled);
        Assert.AreEqual(21, custom.sortingOrder);
    }

    [UnityTest]
    public IEnumerator RenderedPixelsKeepBiologyVisibleAcrossPerspectiveAndOrthographicZoom()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            Assert.Ignore("实际像素验证需要图形设备。");
        Shader shader = Shader.Find(GraphicsSettings.currentRenderPipeline != null
            ? "Universal Render Pipeline/2D/Sprite-Unlit-Default" : "Sprites/Default");
        Assert.IsNotNull(shader);
        var material = new Material(shader);
        var target = new RenderTexture(64, 64, 24);
        var pixels = new Texture2D(64, 64, TextureFormat.RGB24, false);
        Transform biological = SpriteObject("Visible biological sprite", 10f);
        biological.position = center.WorldPosition;
        biological.localScale = Vector3.one;
        SpriteRenderer art = biological.GetComponent<SpriteRenderer>();
        art.sharedMaterial = material;
        art.color = Color.red;
        SpriteRenderer ground = Child("Foreground ground").AddComponent<SpriteRenderer>();
        ground.sprite = sprite;
        ground.sharedMaterial = material;
        ground.color = Color.green;
        ground.transform.position = center.WorldPosition + Vector3.back * 0.05f;
        ground.transform.localScale = Vector3.one * 10f;
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 20f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.allowHDR = false;
        camera.targetTexture = target;
        camera.rect = new Rect(0f, 0f, 1f, 1f);
        var visibility = camera.gameObject.AddComponent<EcosystemCameraVisibility>();
        Set(visibility, "ecosystemRoot", binder.transform);
        Set(visibility, "groundRenderer", ground);
        try
        {
            foreach (bool orthographic in new[] { false, true })
            {
                camera.orthographic = orthographic;
                foreach (float distance in new[] { 0.15f, 3f, 30f })
                {
                    camera.transform.position = center.WorldPosition + Vector3.back * distance;
                    camera.orthographicSize = distance;
                    visibility.RefreshNow();
                    yield return null;
                    yield return null;
                    RenderTexture previous = RenderTexture.active;
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                    pixels.Apply();
                    RenderTexture.active = previous;
                    Color actual = pixels.GetPixel(32, 32);
                    Assert.Greater(actual.r, 0.8f, "生物应显示：orthographic=" + orthographic
                        + ", distance=" + distance + ", actual=" + actual + ", viewport=" + camera.pixelRect);
                    Assert.Less(actual.g, 0.2f, "地面不应遮住生物。");
                }
            }
        }
        finally
        {
            camera.targetTexture = null;
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(pixels);
            Object.DestroyImmediate(material);
        }
    }

    private Transform SpriteObject(string name, float depth)
    {
        GameObject go = Child(name, binder.transform);
        go.transform.position = camera.transform.position + camera.transform.forward * depth;
        go.transform.rotation = camera.transform.rotation;
        go.transform.localScale = Vector3.one * 0.05f;
        go.AddComponent<SpriteRenderer>().sprite = sprite;
        return go.transform;
    }

    private AnimalsBase Spawn(E_AnimalSpecies species, TileCell cell = null)
    {
        Assert.IsTrue(spawner.TrySpawn(species, cell != null ? cell : center, out AnimalsBase animal));
        binder.SyncNow();
        return animal;
    }

    private int TotalDetritus()
    {
        int total = 0;
        foreach (ICellEnv cell in map.Cells) total += cell.Detritus;
        return total;
    }

    private GameObject Child(string name, Transform parent = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent != null ? parent : root.transform, false);
        return go;
    }

    private static void Set(object target, string name, object value)
    {
        for (System.Type type = target.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) continue;
            field.SetValue(target, value);
            return;
        }
        Assert.Fail("Missing field " + name);
    }
}
