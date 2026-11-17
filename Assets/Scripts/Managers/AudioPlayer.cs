using System;
using UnityEngine;

/// <summary>持续播放背景音乐，雨天叠加循环雨声，并响应地图中的物种点击与成功投放。</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Audio/Audio Player")]
public class AudioPlayer : MonoBehaviour
{
    [Serializable]
    public struct PlantSound
    {
        [InspectorName("植物类型")] public E_PlantSpecies species;
        [InspectorName("点击 / 投放音效")] public AudioClip clip;
    }

    [Serializable]
    public struct AnimalSound
    {
        [InspectorName("动物类型")] public E_AnimalSpecies species;
        [InspectorName("点击 / 投放音效")] public AudioClip clip;
    }

    [Header("场景引用")]
    [SerializeField, Tooltip("接收此地图的物种点击与投放；留空时在启用组件时自动查找本场景的地图。")]
    private TileMapManager map;
    [SerializeField, Tooltip("与天气下拉框使用同一个 WeatherBase；留空时优先使用地图的天气引用。")]
    private WeatherBase weather;

    [Header("背景音乐与雨声（拖入 AudioClip，自动循环播放）")]
    [SerializeField, InspectorName("背景音乐"), Tooltip("原 Sunny Music，晴天与雨天都会持续播放。")]
    private AudioClip sunnyMusic;
    [SerializeField, InspectorName("下雨音效"), Tooltip("原 Rainy Music，仅下雨时叠加播放，受音效音量控制。")]
    private AudioClip rainyMusic;

    [Header("物种音效（点击与投放共用，同一类型只配置一项，留空则不播放）")]
    [SerializeField, InspectorName("植物音效")]
    private PlantSound[] plantSounds =
    {
        new PlantSound { species = E_PlantSpecies.Algae },
        new PlantSound { species = E_PlantSpecies.Moss },
        new PlantSound { species = E_PlantSpecies.Mushroom },
        new PlantSound { species = E_PlantSpecies.Gymnosperm },
        new PlantSound { species = E_PlantSpecies.Angiosperm }
    };
    [SerializeField, InspectorName("动物音效")]
    private AnimalSound[] animalSounds =
    {
        new AnimalSound { species = E_AnimalSpecies.Frog },
        new AnimalSound { species = E_AnimalSpecies.Earthworm },
        new AnimalSound { species = E_AnimalSpecies.Snail },
        new AnimalSound { species = E_AnimalSpecies.Ant },
        new AnimalSound { species = E_AnimalSpecies.Spider },
        new AnimalSound { species = E_AnimalSpecies.MilkFrog },
        new AnimalSound { species = E_AnimalSpecies.Fish },
        new AnimalSound { species = E_AnimalSpecies.Sandworm },
        new AnimalSound { species = E_AnimalSpecies.Sparrow }
    };

    [Header("音量（运行时调整立即生效）")]
    [SerializeField, Range(0f, 1f), InspectorName("总音量")]
    private float masterVolume = 1f;
    [SerializeField, Range(0f, 1f), InspectorName("音乐音量")]
    private float musicVolume = 0.5f;
    [SerializeField, Range(0f, 1f), InspectorName("音效音量"), Tooltip("控制雨声与物种点击、投放音效。")]
    private float soundEffectVolume = 1f;

    // 背景音乐、循环雨声和物种音效互不打断；序列化引用在脚本重载后保留。
    [SerializeField, HideInInspector] private AudioSource musicSource;
    [SerializeField, HideInInspector] private AudioSource rainSource;
    [SerializeField, HideInInspector] private AudioSource soundEffectSource;
    private TileMapManager subscribedMap;
    private WeatherBase fallbackWeather;

    private void OnEnable()
    {
        if (musicSource == null)
            musicSource = CreateSource(true);
        if (rainSource == null)
            rainSource = CreateSource(true);
        if (soundEffectSource == null)
            soundEffectSource = CreateSource(false);
        if (map == null)
            map = FindInScene<TileMapManager>();
        fallbackWeather = FindInScene<WeatherBase>();
        UpdateMapSubscription();
        ApplyVolumes();
        RefreshLoops();
    }

    private void LateUpdate()
    {
        UpdateMapSubscription();
        ApplyVolumes();
        // weatherType 是公开字段；同时支持下拉框和 Inspector 修改天气。
        RefreshLoops();
    }

    private void OnDisable()
    {
        UnsubscribeMap();
        if (musicSource != null)
        {
            musicSource.Stop();
            musicSource.clip = null;
        }
        if (rainSource != null)
        {
            rainSource.Stop();
            rainSource.clip = null;
        }
        if (soundEffectSource != null)
            soundEffectSource.Stop();
    }

    private void OnDestroy()
    {
        // 只清理本组件创建的通道，不使用或修改场景里已有的 AudioSource。
        if (musicSource != null)
            Destroy(musicSource);
        if (rainSource != null)
            Destroy(rainSource);
        if (soundEffectSource != null)
            Destroy(soundEffectSource);
    }

    private AudioSource CreateSource(bool loop)
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;
        // AddComponent 时已启用，显式停止默认的 Play On Awake 状态。
        source.Stop();
        return source;
    }

    private T FindInScene<T>() where T : Component
    {
        foreach (T candidate in FindObjectsOfType<T>(true))
            if (candidate.gameObject.scene == gameObject.scene)
                return candidate;
        return null;
    }

    private void UpdateMapSubscription()
    {
        if (subscribedMap == map)
            return;
        UnsubscribeMap();
        subscribedMap = map;
        if (subscribedMap != null)
        {
            subscribedMap.PlantClicked += PlayPlantSound;
            subscribedMap.AnimalClicked += PlayAnimalSound;
            subscribedMap.PlantPlaced += PlayPlantSound;
            subscribedMap.AnimalPlaced += PlayAnimalSound;
        }
    }

    private void UnsubscribeMap()
    {
        if (subscribedMap != null)
        {
            subscribedMap.PlantClicked -= PlayPlantSound;
            subscribedMap.AnimalClicked -= PlayAnimalSound;
            subscribedMap.PlantPlaced -= PlayPlantSound;
            subscribedMap.AnimalPlaced -= PlayAnimalSound;
        }
        subscribedMap = null;
    }

    private void ApplyVolumes()
    {
        musicSource.volume = Mathf.Clamp01(masterVolume) * Mathf.Clamp01(musicVolume);
        rainSource.volume = Mathf.Clamp01(masterVolume) * Mathf.Clamp01(soundEffectVolume);
        soundEffectSource.volume = Mathf.Clamp01(masterVolume) * Mathf.Clamp01(soundEffectVolume);
    }

    private void RefreshLoops()
    {
        UpdateLoop(musicSource, sunnyMusic);
        WeatherBase currentWeather = weather != null ? weather
            : map != null && map.Weather != null ? map.Weather : fallbackWeather;
        UpdateLoop(rainSource, currentWeather != null && currentWeather.weatherType == E_WeatherType.Rain
            ? rainyMusic : null);
    }

    private static void UpdateLoop(AudioSource source, AudioClip clip)
    {
        // 天气变化不重启背景音乐；雨声只在雨天循环播放。
        if (source.clip == clip)
            return;
        source.Stop();
        source.clip = clip;
        if (clip != null)
            source.Play();
    }

    /// <summary>也可由其他植物交互脚本直接调用。</summary>
    public void PlayPlantSound(E_PlantSpecies species)
    {
        if (!isActiveAndEnabled || plantSounds == null)
            return;
        foreach (PlantSound sound in plantSounds)
        {
            if (sound.species != species)
                continue;
            PlaySoundEffect(sound.clip);
            return;
        }
    }

    /// <summary>也可由其他动物交互脚本直接调用。</summary>
    public void PlayAnimalSound(E_AnimalSpecies species)
    {
        if (!isActiveAndEnabled || animalSounds == null)
            return;
        foreach (AnimalSound sound in animalSounds)
        {
            if (sound.species != species)
                continue;
            PlaySoundEffect(sound.clip);
            return;
        }
    }

    private void PlaySoundEffect(AudioClip clip)
    {
        if (clip == null)
            return;
        ApplyVolumes();
        soundEffectSource.PlayOneShot(clip);
    }
}
