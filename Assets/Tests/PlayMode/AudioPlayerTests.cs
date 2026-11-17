using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

public class AudioPlayerTests
{
    private GameObject root;
    private AudioPlayer player;
    private WeatherBase weather;
    private AudioClip sun;
    private AudioClip rain;
    private AudioSource music;
    private AudioSource rainfall;
    private AudioSource effects;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("Audio player test");
        root.SetActive(false);
        weather = root.AddComponent<WeatherBase>();
        sun = AudioClip.Create("Sun", 44100, 1, 44100, false);
        rain = AudioClip.Create("Rain", 44100, 1, 44100, false);
        player = root.AddComponent<AudioPlayer>();
        Set(player, "weather", weather);
        Set(player, "sunnyMusic", sun);
        Set(player, "rainyMusic", rain);
        root.SetActive(true);
        music = Get<AudioSource>(player, "musicSource");
        rainfall = Get<AudioSource>(player, "rainSource");
        effects = Get<AudioSource>(player, "soundEffectSource");
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
        Object.DestroyImmediate(sun);
        Object.DestroyImmediate(rain);
    }

    [Test]
    public void BackgroundMusicContinuesWhileRainSoundFollowsWeather()
    {
        Assert.AreSame(sun, music.clip);
        Assert.IsTrue(music.loop);
        Assert.IsNull(rainfall.clip);
        Assert.IsFalse(rainfall.isPlaying);
        Assert.IsTrue(rainfall.loop);
        Assert.AreNotSame(music, rainfall);
        Assert.AreNotSame(rainfall, effects);
        Assert.AreNotSame(music, effects);
        weather.weatherType = E_WeatherType.Rain;
        Tick();
        Assert.AreSame(sun, music.clip);
        Assert.IsTrue(music.isPlaying);
        Assert.AreSame(rain, rainfall.clip);
        Assert.IsTrue(rainfall.isPlaying);
        weather.weatherType = E_WeatherType.Sun;
        Tick();
        Assert.AreSame(sun, music.clip);
        Assert.IsNull(rainfall.clip);
        Assert.IsFalse(rainfall.isPlaying);

        weather.weatherType = E_WeatherType.Rain;
        Tick();
        Assert.IsTrue(rainfall.isPlaying);
        weather.weatherType = (E_WeatherType)999;
        Tick();
        Assert.AreSame(sun, music.clip);
        Assert.IsNull(rainfall.clip);
        Assert.IsFalse(rainfall.isPlaying);
    }

    [Test]
    public void SameTrackKeepsPlaybackPositionAcrossRefreshAndWeatherChange()
    {
        music.timeSamples = 11025;
        Tick();
        Assert.That(music.timeSamples, Is.GreaterThanOrEqualTo(11025));
        weather.weatherType = E_WeatherType.Rain;
        Tick();
        Assert.That(music.timeSamples, Is.GreaterThanOrEqualTo(11025));
        rainfall.timeSamples = 11025;
        Tick();
        Assert.That(rainfall.timeSamples, Is.GreaterThanOrEqualTo(11025));
        weather.weatherType = E_WeatherType.Sun;
        Tick();
        Assert.That(music.timeSamples, Is.GreaterThanOrEqualTo(11025));
        Assert.IsFalse(rainfall.isPlaying);
    }

    [Test]
    public void MissingRainClipOrWeatherOnlyStopsRainSound()
    {
        weather.weatherType = E_WeatherType.Rain;
        Tick();
        Assert.IsTrue(rainfall.isPlaying);
        Set(player, "rainyMusic", null);
        Tick();
        Assert.IsNull(rainfall.clip);
        Assert.IsFalse(rainfall.isPlaying);
        Assert.AreSame(sun, music.clip);
        Assert.IsTrue(music.isPlaying);

        Set(player, "rainyMusic", rain);
        Tick();
        Assert.IsTrue(rainfall.isPlaying);
        Object.DestroyImmediate(weather);
        Tick();
        Assert.IsFalse(rainfall.isPlaying);
        Assert.AreSame(sun, music.clip);
        Assert.IsTrue(music.isPlaying);
    }

    [Test]
    public void RainSoundCanPlayWithoutBackgroundMusic()
    {
        Set(player, "sunnyMusic", null);
        weather.weatherType = E_WeatherType.Rain;
        Tick();
        Assert.IsNull(music.clip);
        Assert.IsFalse(music.isPlaying);
        Assert.AreSame(rain, rainfall.clip);
        Assert.IsTrue(rainfall.isPlaying);
    }

    [Test]
    public void InspectorVolumesApplyToIndependentChannelsAndClamp()
    {
        Set(player, "masterVolume", 0.5f);
        Set(player, "musicVolume", 0.4f);
        Set(player, "soundEffectVolume", 0.8f);
        Tick();
        Assert.That(music.volume, Is.EqualTo(0.2f).Within(0.001f));
        Assert.That(rainfall.volume, Is.EqualTo(0.4f).Within(0.001f));
        Assert.That(effects.volume, Is.EqualTo(0.4f).Within(0.001f));
        Set(player, "masterVolume", 2f);
        Set(player, "musicVolume", -1f);
        Tick();
        Assert.AreEqual(0f, music.volume);
        Assert.That(rainfall.volume, Is.EqualTo(0.8f).Within(0.001f));
        Assert.That(effects.volume, Is.EqualTo(0.8f).Within(0.001f));
    }

    [Test]
    public void SpeciesEffectsDoNotInterruptMusicOrRainAndMissingClipsAreSilent()
    {
        weather.weatherType = E_WeatherType.Rain;
        Tick();
        music.timeSamples = 11025;
        rainfall.timeSamples = 11025;
        Set(player, "plantSounds", new[]
        {
            new AudioPlayer.PlantSound { species = E_PlantSpecies.Moss, clip = rain }
        });
        Set(player, "animalSounds", new[]
        {
            new AudioPlayer.AnimalSound { species = E_AnimalSpecies.Frog, clip = rain }
        });
        player.PlayPlantSound(E_PlantSpecies.Moss);
        Assert.IsTrue(effects.isPlaying);
        Assert.AreSame(sun, music.clip);
        effects.Stop();
        player.PlayAnimalSound(E_AnimalSpecies.Frog);
        Assert.IsTrue(effects.isPlaying);
        effects.Stop();
        player.PlayAnimalSound(E_AnimalSpecies.MilkFrog);
        player.PlayPlantSound(E_PlantSpecies.Algae);
        Assert.IsFalse(effects.isPlaying);
        Assert.AreSame(sun, music.clip);
        Assert.AreSame(rain, rainfall.clip);
        Assert.IsTrue(rainfall.isPlaying);
        Assert.That(music.timeSamples, Is.GreaterThanOrEqualTo(11025));
        Assert.That(rainfall.timeSamples, Is.GreaterThanOrEqualTo(11025));
    }

    [Test]
    public void DisableStopsAllChannelsAndReenableRestoresRainWithoutDuplicatingSources()
    {
        weather.weatherType = E_WeatherType.Rain;
        Tick();
        player.enabled = false;
        Assert.IsNull(music.clip);
        Assert.IsNull(rainfall.clip);
        Assert.IsFalse(music.isPlaying);
        Assert.IsFalse(rainfall.isPlaying);
        Assert.IsFalse(effects.isPlaying);
        player.PlayAnimalSound(E_AnimalSpecies.Frog);
        Assert.IsFalse(effects.isPlaying);
        player.enabled = true;
        Assert.AreSame(sun, music.clip);
        Assert.AreSame(rain, rainfall.clip);
        Assert.IsTrue(rainfall.isPlaying);
        Assert.AreEqual(3, root.GetComponents<AudioSource>().Length);
    }

    [Test]
    public void MapClickUsesSelectedSpeciesAndSubscriptionsFollowEnableState()
    {
        var grid = new GameObject("Grid", typeof(Grid));
        grid.transform.SetParent(root.transform);
        var ground = new GameObject("Map", typeof(Tilemap));
        ground.transform.SetParent(grid.transform);
        Tile tile = ScriptableObject.CreateInstance<Tile>();
        try
        {
            ground.GetComponent<Tilemap>().SetTile(Vector3Int.zero, tile);
            TileMapManager map = ground.AddComponent<TileMapManager>();
            map.InitializeCells();
            Assert.IsTrue(map.TryGetCell(Vector2Int.zero, out TileCell cell));
            var cameraObject = new GameObject("Camera", typeof(Camera));
            cameraObject.transform.SetParent(root.transform);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.transform.position = cell.WorldPosition + Vector3.back * 10f;
            camera.orthographic = true;
            camera.pixelRect = new Rect(0, 0, 256, 256);
            Set(map, "inputCamera", camera);
            Set(player, "map", map);
            Set(player, "plantSounds", new[]
            {
                new AudioPlayer.PlantSound { species = E_PlantSpecies.Moss, clip = rain }
            });
            Set(player, "animalSounds", new[]
            {
                new AudioPlayer.AnimalSound { species = E_AnimalSpecies.Frog, clip = sun }
            });
            Tick();
            Vector2 point = camera.WorldToScreenPoint(cell.WorldPosition);
            Assert.IsTrue(map.TryInspectAtScreenPosition(point));
            Assert.IsFalse(effects.isPlaying, "空格不触发物种音效。");
            cell.TryAddCover(new Moss());
            Assert.IsTrue(map.TryInspectAtScreenPosition(point));
            Assert.IsTrue(effects.isPlaying);
            effects.Stop();

            AnimalSpawner spawner = root.AddComponent<AnimalSpawner>();
            Set(spawner, "map", map);
            Set(spawner, "spawnOnStart", false);
            Assert.IsTrue(spawner.TrySpawn(E_AnimalSpecies.Frog, cell, out _));
            int plants = 0;
            int animals = 0;
            map.PlantClicked += _ => plants++;
            map.AnimalClicked += species => { Assert.AreEqual(E_AnimalSpecies.Frog, species); animals++; };
            Assert.IsTrue(map.TryInspectAtScreenPosition(point));
            Assert.AreEqual(0, plants);
            Assert.AreEqual(1, animals);
            Assert.IsTrue(effects.isPlaying);

            player.enabled = false;
            Assert.AreEqual(1, Get<System.Action<E_AnimalSpecies>>(map, "AnimalClicked").GetInvocationList().Length);
            map.TryInspectAtScreenPosition(point);
            Assert.IsFalse(effects.isPlaying);
            player.enabled = true;
            Assert.AreEqual(2, Get<System.Action<E_AnimalSpecies>>(map, "AnimalClicked").GetInvocationList().Length);
            Assert.IsFalse(map.TryInspectAtScreenPosition(new Vector2(-10f, -10f)));
            Assert.IsFalse(effects.isPlaying, "地图外的点击不播放音效。");
            map.TryInspectAtScreenPosition(point);
            Assert.IsTrue(effects.isPlaying);
        }
        finally
        {
            Object.DestroyImmediate(tile);
        }
    }

    private void Tick() => player.SendMessage("LateUpdate");

    private static void Set(object target, string name, object value)
    {
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }

    private static T Get<T>(object target, string name)
    {
        return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    }
}
