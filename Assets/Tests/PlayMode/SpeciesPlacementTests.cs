using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

public class SpeciesPlacementTests
{
    private GameObject root;
    private Tile tile;
    private TileMapManager map;
    private TimeManager time;
    private AnimalSpawner animals;
    private PlantVisualBinder plantBinder;
    private AnimalVisualBinder animalBinder;
    private SpeciesCard card;
    private Camera camera;
    private TileCell center;
    private GameObject visualPrefab;
    private AudioClip placementSound;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("Species placement test");
        GameObject grid = Child("Grid", root.transform);
        grid.AddComponent<Grid>();
        GameObject ground = Child("Tilemap", grid.transform);
        Tilemap tilemap = ground.AddComponent<Tilemap>();
        tile = ScriptableObject.CreateInstance<Tile>();
        for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
                tilemap.SetTile(new Vector3Int(x, y, 0), tile);
        map = ground.AddComponent<TileMapManager>();
        map.InitializeCells();
        Assert.IsTrue(map.TryGetCell(Vector2Int.zero, out center));

        time = Child("Time", root.transform).AddComponent<TimeManager>();
        time.ConfigureDailyActions(10);
        animals = Child("Animals", root.transform).AddComponent<AnimalSpawner>();
        SetField(animals, "map", map);
        SetField(animals, "spawnOnStart", false);
        plantBinder = Child("Plant visuals", root.transform).AddComponent<PlantVisualBinder>();
        SetField(plantBinder, "map", map);
        animalBinder = Child("Animal visuals", root.transform).AddComponent<AnimalVisualBinder>();
        SetField(animalBinder, "spawner", animals);

        // 模板在隐藏容器中；实例应保留用户配置的层级与缩放。
        GameObject templates = Child("Templates", root.transform);
        templates.SetActive(false);
        visualPrefab = Child("Custom visual", templates.transform);
        visualPrefab.transform.localScale = new Vector3(0.7f, 1.2f, 1f);
        Child("Artwork", visualPrefab.transform).AddComponent<SpriteRenderer>();
        SetField(plantBinder, "angiospermPrefab", visualPrefab);
        SetField(plantBinder, "mossPrefab", visualPrefab);
        SetField(animalBinder, "frogPrefab", visualPrefab);
        SetField(animalBinder, "milkFrogPrefab", visualPrefab);

        camera = Child("Camera", root.transform).AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 0f, -10f);
        camera.orthographic = true;
        camera.orthographicSize = 4f;
        camera.pixelRect = new Rect(0f, 0f, 256f, 256f);
        card = Child("Card", root.transform).AddComponent<SpeciesCard>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
        Object.DestroyImmediate(tile);
        if (placementSound != null)
            Object.DestroyImmediate(placementSound);
    }

    [Test]
    public void PlantDropRegistersOnePlantAndParticipatesInSettlement()
    {
        Configure(Plant(E_PlantSpecies.Angiosperm));
        Vector2 screen = camera.WorldToScreenPoint(center.WorldPosition);
        Assert.IsTrue(card.TryPlaceAtScreenPosition(screen));
        Assert.IsInstanceOf<Angiosperm>(center.Subject);
        Assert.AreEqual(2, card.CurrentCount);
        Assert.AreEqual(9, time.RemainingActions);
        Assert.AreEqual(1, plantBinder.Visuals.Count);
        Assert.AreEqual(1, plantBinder.transform.childCount);
        PlantVisual visual = plantBinder.Visuals[center.Subject];
        Assert.AreSame(center.Subject, visual.Plant);
        Assert.AreEqual(visualPrefab.transform.localScale, visual.transform.localScale);
        Assert.AreEqual(center.WorldPosition, visual.transform.position);

        new PlantSettler().Settle(1, new PlantWeatherCondition(E_WeatherType.Rain, 1, E_WeatherChange.None), map.Cells);
        Assert.AreEqual(1, center.Subject.LastSettledDay);
        Assert.IsTrue(((Angiosperm)center.Subject).HasHydration);
    }

    [TestCase(E_AnimalSpecies.Frog)]
    [TestCase(E_AnimalSpecies.MilkFrog)]
    public void AnimalDropUsesExistingRegistryAndParticipatesInSettlement(E_AnimalSpecies species)
    {
        Configure(Animal(species));
        Assert.IsTrue(card.TryPlaceAtScreenPosition(camera.WorldToScreenPoint(center.WorldPosition)));
        Assert.AreEqual(1, animals.LiveCount);
        AnimalsBase frog = animals.Animals[0];
        Assert.AreSame(center, frog.CurrentCell);
        Assert.AreSame(frog, animalBinder.Visuals[frog].Animal);
        Assert.AreEqual(1, animalBinder.transform.childCount);

        new AnimalSettler().Settle(1, new PlantWeatherCondition(E_WeatherType.Rain, 1, E_WeatherChange.None), map.Cells, animals.Animals);
        Assert.AreEqual(1, frog.LastSettledDay);
        Assert.AreEqual(1, frog.Satiety, "无食物时，新投放动物也应正常消耗饱腹。");
        Assert.AreEqual(2, card.CurrentCount);
        Assert.AreEqual(9, time.RemainingActions);
    }

    [Test]
    public void OccupiedPlantSlotDoesNotConsumeStockOrActions()
    {
        Configure(Plant(E_PlantSpecies.Angiosperm));
        Assert.IsTrue(card.TryPlaceOnCell(center));
        PlantsBase original = center.Subject;
        Assert.IsFalse(card.TryPlaceOnCell(center));
        Assert.AreSame(original, center.Subject);
        Assert.AreEqual(2, card.CurrentCount);
        Assert.AreEqual(9, time.RemainingActions);
        Assert.AreEqual(1, plantBinder.Visuals.Count);
    }

    [Test]
    public void CoverPlantCanShareCellWithSubject()
    {
        Configure(Plant(E_PlantSpecies.Angiosperm));
        Assert.IsTrue(card.TryPlaceOnCell(center));
        Configure(Plant(E_PlantSpecies.Moss));
        Assert.IsTrue(card.TryPlaceOnCell(center));
        Assert.IsNotNull(center.Subject);
        Assert.AreEqual(1, center.Cover.Count);
        Assert.AreEqual(2, plantBinder.Visuals.Count);
        Assert.IsFalse(card.TryPlaceOnCell(center));
        Assert.AreEqual(2, card.CurrentCount);
        Assert.AreEqual(8, time.RemainingActions);
    }

    [Test]
    public void MissingPrefabRejectsPlacementWithoutInvisibleLogicalObject()
    {
        SetField(plantBinder, "angiospermPrefab", null);
        Configure(Plant(E_PlantSpecies.Angiosperm));
        Assert.IsFalse(card.TryPlaceOnCell(center));
        Assert.IsNull(center.Subject);
        Assert.AreEqual(0, plantBinder.transform.childCount);
        AssertUnspent();
    }

    [Test]
    public void UnconfiguredAndReservedSpeciesDoNotConsumeAnything()
    {
        Configure(new SpeciesInfo { DailyCount = 3 });
        Assert.IsFalse(card.TryPlaceOnCell(center));
        AssertUnspent();
        SetField(animalBinder, "fishPrefab", visualPrefab);
        Configure(Animal(E_AnimalSpecies.Fish));
        Assert.IsFalse(card.TryPlaceOnCell(center));
        Assert.AreEqual(0, animals.LiveCount);
        AssertUnspent();
    }

    [Test]
    public void InvalidDropAndExhaustedActionsDoNotCreateSpecies()
    {
        Configure(Plant(E_PlantSpecies.Angiosperm));
        Assert.IsFalse(card.TryPlaceAtScreenPosition(new Vector2(-100f, -100f)));
        Assert.IsFalse(card.TryPlaceAtScreenPosition(new Vector2(250f, 250f)));
        Assert.IsFalse(card.TryPlaceOnCell(Child("Foreign cell", root.transform).AddComponent<TileCell>()));
        AssertUnspent();
        time.ConfigureDailyActions(0);
        Assert.IsFalse(card.TryPlaceOnCell(center));
        Assert.IsNull(center.Subject);
        Assert.AreEqual(3, card.CurrentCount);
    }

    [Test]
    public void AnimalsOnDifferentCellsHaveNoGlobalPopulationOffset()
    {
        Configure(Animal(E_AnimalSpecies.Frog));
        Assert.IsTrue(card.TryPlaceOnCell(center));
        Assert.IsTrue(map.TryGetCell(Vector2Int.right, out TileCell other));
        Assert.IsTrue(card.TryPlaceOnCell(other));
        foreach (AnimalsBase animal in animals.Animals)
            Assert.AreEqual(((TileCell)animal.CurrentCell).WorldPosition, animalBinder.Visuals[animal].transform.position);
    }

    [TestCase(SpeciesKind.Plant)]
    [TestCase(SpeciesKind.Animal)]
    public void SuccessfulDropPlaysPlacedSpeciesSoundEvenOnLastAction(SpeciesKind kind)
    {
        SpeciesInfo info = kind == SpeciesKind.Plant
            ? Plant(E_PlantSpecies.Angiosperm) : Animal(E_AnimalSpecies.MilkFrog);
        info.DailyCount = 1;
        Configure(info);
        time.ConfigureDailyActions(1);
        CreatePlacementAudio(info, out AudioSource effects);
        // 已有青蛙的格子点击时会优先选中青蛙，投放音效必须使用本次投放的类型。
        Assert.IsTrue(animals.TrySpawn(E_AnimalSpecies.Frog, center, out _));
        int placements = 0;
        int clicks = 0;
        map.PlantPlaced += species => { Assert.AreEqual(info.PlantSpecies, species); placements++; };
        map.AnimalPlaced += species => { Assert.AreEqual(info.AnimalSpecies, species); placements++; };
        map.PlantClicked += _ => clicks++;
        map.AnimalClicked += _ => clicks++;

        Assert.IsTrue(card.TryPlaceAtScreenPosition(camera.WorldToScreenPoint(center.WorldPosition)));
        Assert.IsTrue(effects.isPlaying);
        Assert.AreEqual(1, placements);
        Assert.AreEqual(0, clicks);
        Assert.AreEqual(0, card.CurrentCount);
        Assert.AreEqual(0, time.RemainingActions);

        effects.Stop();
        Assert.IsFalse(card.TryPlaceOnCell(center));
        Assert.IsFalse(effects.isPlaying, "库存或行动耗尽后，失败投放不播放音效。");
        Assert.AreEqual(1, placements);
    }

    [Test]
    public void InvalidAndOccupiedDropsDoNotPlaySound()
    {
        SpeciesInfo info = Plant(E_PlantSpecies.Angiosperm);
        Configure(info);
        CreatePlacementAudio(info, out AudioSource effects);
        Assert.IsFalse(card.TryPlaceAtScreenPosition(new Vector2(-100f, -100f)));
        Assert.IsFalse(effects.isPlaying);
        Assert.IsTrue(card.TryPlaceOnCell(center));
        effects.Stop();
        Assert.IsFalse(card.TryPlaceOnCell(center));
        Assert.IsFalse(effects.isPlaying, "占位冲突不播放音效。");
        Assert.AreEqual(2, card.CurrentCount);
        Assert.AreEqual(9, time.RemainingActions);
    }

    [TestCase(SpeciesKind.Plant)]
    [TestCase(SpeciesKind.Animal)]
    public void PlacementSoundSubscriptionFollowsEnableState(SpeciesKind kind)
    {
        SpeciesInfo info = kind == SpeciesKind.Plant
            ? Plant(E_PlantSpecies.Angiosperm) : Animal(E_AnimalSpecies.MilkFrog);
        Configure(info);
        AudioPlayer player = CreatePlacementAudio(info, out AudioSource effects);
        player.enabled = false;
        Assert.IsTrue(card.TryPlaceOnCell(center));
        Assert.IsFalse(effects.isPlaying);
        player.enabled = true;
        Assert.IsTrue(map.TryGetCell(Vector2Int.right, out TileCell other));
        Assert.IsTrue(card.TryPlaceOnCell(other));
        Assert.IsTrue(effects.isPlaying);
    }

    private AudioPlayer CreatePlacementAudio(SpeciesInfo info, out AudioSource effects)
    {
        GameObject audio = Child("Placement audio", root.transform);
        audio.SetActive(false);
        AudioPlayer player = audio.AddComponent<AudioPlayer>();
        placementSound = AudioClip.Create("Placement sound", 44100, 1, 44100, false);
        SetField(player, "map", map);
        SetField(player, "plantSounds", info.Kind == SpeciesKind.Plant
            ? new[] { new AudioPlayer.PlantSound { species = info.PlantSpecies, clip = placementSound } }
            : new AudioPlayer.PlantSound[0]);
        SetField(player, "animalSounds", info.Kind == SpeciesKind.Animal
            ? new[] { new AudioPlayer.AnimalSound { species = info.AnimalSpecies, clip = placementSound } }
            : new AudioPlayer.AnimalSound[0]);
        audio.SetActive(true);
        effects = (AudioSource)typeof(AudioPlayer).GetField("soundEffectSource",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player);
        return player;
    }

    private void Configure(SpeciesInfo info)
    {
        card.Initialize(info, time, map, camera, plantBinder, animalBinder);
    }

    private void AssertUnspent()
    {
        Assert.AreEqual(3, card.CurrentCount);
        Assert.AreEqual(10, time.RemainingActions);
    }

    private static SpeciesInfo Plant(E_PlantSpecies species)
    {
        return new SpeciesInfo { Kind = SpeciesKind.Plant, PlantSpecies = species, DailyCount = 3 };
    }

    private static SpeciesInfo Animal(E_AnimalSpecies species)
    {
        return new SpeciesInfo { Kind = SpeciesKind.Animal, AnimalSpecies = species, DailyCount = 3 };
    }

    private static GameObject Child(string name, Transform parent)
    {
        var result = new GameObject(name);
        result.transform.SetParent(parent, false);
        return result;
    }

    private static void SetField(object target, string name, object value)
    {
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
