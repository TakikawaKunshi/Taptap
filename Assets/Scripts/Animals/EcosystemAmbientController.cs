using System.Collections.Generic;
using UnityEngine;

/// <summary>控制动物随机游走，过夜前将显示位置落实为结算起点，并清理死亡显示。</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public sealed class EcosystemAmbientController : MonoBehaviour
{
    [Header("引用（留空自动查找）")]
    [SerializeField] private AnimalVisualBinder animalBinder;
    [SerializeField] private PlantVisualBinder plantBinder;
    [SerializeField] private AnimalSettlementBridge settlementBridge;
    [SerializeField] private TimeManager timeManager;

    [Header("随机游走")]
    [SerializeField, Tooltip("所有动物随机选择可通行邻格；关闭后使用各物种原有的觅食、搬运和庇护移动策略。")]
    private bool randomWandering = true;
    [SerializeField, Tooltip("勾选时，玩家操作会暂停游走；默认关闭，动物可在玩家操作期间继续移动。")]
    private bool pauseOnUserInput;
    [SerializeField, Min(0f), Tooltip("开局、过夜后或操作暂停结束后，等待多久开始游走，单位：秒。")]
    private float idleDelay = 1.5f;
    [SerializeField, Min(0.05f), Tooltip("基础移动速度；各物种使用自己的速度倍率。")]
    private float moveSpeed = 0.45f;
    [SerializeField, Tooltip("基础随机停留时间；各物种使用自己的停留倍率，每只动物独立计时。")]
    private Vector2 restSeconds = new Vector2(0.4f, 2f);

    private sealed class Motion
    {
        public TileCell Cell;
        public TileCell Target;
        public TileCell PreviousCell;
        public AnimalIdleBehaviour Behaviour;
        public AnimalVisual Visual;
        public Vector3 Position;
        public Vector3 Offset;
        public Vector3 Start;
        public float Progress;
        public float Duration;
        public float Rest;
        public int SettledDay;
    }

    private readonly Dictionary<AnimalsBase, Motion> motions = new Dictionary<AnimalsBase, Motion>();
    private readonly HashSet<AnimalsBase> registered = new HashSet<AnimalsBase>();
    private readonly List<AnimalsBase> removed = new List<AnimalsBase>();
    // CanEnter 只读取传入动物和格子，不使用占位快照。
    private static readonly PlantWeatherCondition MovementWeather = new PlantWeatherCondition(E_WeatherType.Sun);
    private readonly AnimalWorldView passage = new AnimalWorldView(MovementWeather, null, null);
    private AnimalSettlementBridge subscribedBridge;
    private TimeManager subscribedTimeManager;
    private bool movementLocked;
    private float idleTime;
    private Vector3 previousMouse;

    private void OnEnable()
    {
        ResolveReferences();
        idleTime = 0f;
        movementLocked = false;
        previousMouse = Input.mousePosition;
    }

    private void OnDisable()
    {
        if (subscribedBridge != null)
        {
            subscribedBridge.Settling -= FreezeAtCurrentPositions;
            subscribedBridge.Settled -= OnSettled;
        }
        subscribedBridge = null;
        if (subscribedTimeManager != null)
            subscribedTimeManager.PhaseChanged -= OnPhaseChanged;
        subscribedTimeManager = null;
        motions.Clear();
        if (animalBinder != null && animalBinder.isActiveAndEnabled)
            animalBinder.SyncNow();
    }

    private void ResolveReferences()
    {
        if (animalBinder == null) animalBinder = GetComponent<AnimalVisualBinder>();
        if (animalBinder == null) animalBinder = FindObjectOfType<AnimalVisualBinder>();
        if (plantBinder == null) plantBinder = GetComponent<PlantVisualBinder>();
        if (plantBinder == null) plantBinder = FindObjectOfType<PlantVisualBinder>();
        if (timeManager == null) timeManager = FindObjectOfType<TimeManager>();
        if (settlementBridge == null) settlementBridge = FindObjectOfType<AnimalSettlementBridge>();
        if (subscribedBridge != settlementBridge)
        {
            if (subscribedBridge != null)
            {
                subscribedBridge.Settling -= FreezeAtCurrentPositions;
                subscribedBridge.Settled -= OnSettled;
            }
            subscribedBridge = settlementBridge;
            if (subscribedBridge != null)
            {
                subscribedBridge.Settling += FreezeAtCurrentPositions;
                subscribedBridge.Settled += OnSettled;
            }
        }
        if (subscribedTimeManager != timeManager)
        {
            if (subscribedTimeManager != null)
                subscribedTimeManager.PhaseChanged -= OnPhaseChanged;
            subscribedTimeManager = timeManager;
            if (subscribedTimeManager != null)
                subscribedTimeManager.PhaseChanged += OnPhaseChanged;
        }
    }

    private void Update()
    {
        ResolveReferences();
        // 在原 Binder 和点击检测之前同步，然后立即恢复显示位置。
        // SyncNow 同时延后 Binder 自己的定时同步，避免点击检测读到一瞬间的格心位置。
        if (animalBinder != null && animalBinder.isActiveAndEnabled)
        {
            animalBinder.SyncNow();
            RestorePositions();
        }
    }

    private void LateUpdate()
    {
        Tick(Time.deltaTime, ReadUserInput());
    }

    private bool ReadUserInput()
    {
        Vector3 mouse = Input.mousePosition;
        bool moved = (mouse - previousMouse).sqrMagnitude > 1f;
        previousMouse = mouse;
        return moved || Input.anyKey || Input.touchCount > 0
            || Input.mouseScrollDelta.sqrMagnitude > 0f;
    }

    /// <summary>开启操作暂停时，可由自定义输入系统通知操作。</summary>
    public void NotifyUserInteraction()
    {
        if (pauseOnUserInput) idleTime = 0f;
    }

    /// <summary>推进显示和闲逛，可用于回放验证；deltaTime 为本帧游戏时间。</summary>
    public void Tick(float deltaTime, bool userInteracting)
    {
        if (!isActiveAndEnabled) return;
        ResolveReferences();
        AnimalSpawner spawner = animalBinder != null ? animalBinder.Spawner : null;
        if (spawner == null) return;
        CleanupDead(spawner);
        deltaTime = Mathf.Max(0f, deltaTime);
        bool night = timeManager != null && (timeManager.IsNightSettlement
            || timeManager.IsSettlementRunning || timeManager.IsGameSettlement);
        if (night && !movementLocked) FreezeAtCurrentPositions();
        movementLocked = night;
        bool inputPaused = pauseOnUserInput && userInteracting;
        if (inputPaused || night) idleTime = 0f;
        else idleTime += deltaTime;
        bool canMove = !inputPaused && !night && Time.timeScale > 0f && idleTime >= idleDelay;

        registered.Clear();
        foreach (AnimalsBase animal in spawner.Animals)
        {
            if (animal == null || animal.IsDead) continue;
            TileCell cell = animal.CurrentCell as TileCell;
            if (!ValidCell(cell, spawner.Map)) continue;
            registered.Add(animal);
            animalBinder.Visuals.TryGetValue(animal, out AnimalVisual visual);
            if (!motions.TryGetValue(animal, out Motion motion))
            {
                motion = new Motion { Cell = cell, Position = cell.WorldPosition,
                    SettledDay = animal.LastSettledDay, Behaviour = AnimalIdleBehaviour.Create(animal.Species) };
                motion.Behaviour.Reset(cell);
                motion.Rest = RandomRest(motion);
                motions.Add(animal, motion);
            }
            if (visual != motion.Visual)
            {
                if (motion.Visual == null && visual != null)
                {
                    motion.Offset = visual.transform.position - cell.WorldPosition;
                    motion.Position = cell.WorldPosition + motion.Offset;
                }
                motion.Visual = visual;
            }
            // 每晚结算具有优先权；闲逛绝不能在结算后把逻辑格写回旧目标。
            if (motion.Cell != cell || motion.SettledDay != animal.LastSettledDay || night)
            {
                motion.Cell = cell;
                motion.SettledDay = animal.LastSettledDay;
                motion.Target = null;
                motion.PreviousCell = null;
                motion.Behaviour.Reset(cell);
                motion.Position = cell.WorldPosition + motion.Offset;
                motion.Rest = RandomRest(motion);
            }
            if (canMove) Advance(animal, motion, spawner, deltaTime);
            if (visual != null) visual.transform.position = motion.Position;
        }

        removed.Clear();
        foreach (var pair in motions)
            if (!registered.Contains(pair.Key)) removed.Add(pair.Key);
        foreach (AnimalsBase animal in removed)
        {
            if (motions[animal].Visual != null)
                motions[animal].Visual.gameObject.SetActive(false);
            motions.Remove(animal);
        }
    }

    private void Advance(AnimalsBase animal, Motion motion, AnimalSpawner spawner, float deltaTime)
    {
        TileMapManager map = spawner.Map;
        if (motion.Target != null && motion.Target != motion.Cell && (!ValidCell(motion.Target, map)
            || !passage.CanEnter(animal, motion.Target)))
        {
            // 行走途中出现新障碍时沿原路平滑退回，逻辑格保持原值。
            motion.Target = motion.Cell;
            BeginStep(motion);
        }
        if (motion.Target == null)
        {
            motion.Rest -= deltaTime;
            if (motion.Rest > 0f) return;
            // 每只动物独立停留、选路；默认随机游走，食物与庇护不会让它一直停在原地。
            // 随机移动只改变位置，取食、搬运、饥饿仍由夜间结算处理。
            IWeatherQuery weather = map.Weather != null
                ? new PlantWeatherCondition(map.Weather.weatherType) : MovementWeather;
            var world = new AnimalWorldView(weather, map.Cells, spawner.Animals);
            TileCell next = (randomWandering
                ? motion.Behaviour.ChooseRandomStep(animal, world, motion.PreviousCell)
                : motion.Behaviour.ChooseStep(animal, world, motion.PreviousCell)) as TileCell;
            if (next == motion.Cell || !ValidCell(next, map) || !passage.CanEnter(animal, next)
                || !IsNeighbor(motion.Cell, next))
            {
                motion.Rest = RandomRest(motion);
                return;
            }
            motion.Target = next;
            BeginStep(motion);
        }
        motion.Progress = Mathf.Min(1f, motion.Progress + deltaTime / motion.Duration);
        float eased = Mathf.SmoothStep(0f, 1f, motion.Progress);
        motion.Position = Vector3.Lerp(motion.Start, motion.Target.WorldPosition + motion.Offset, eased);
        if (motion.Progress < 1f) return;
        if (motion.Target != motion.Cell)
        {
            motion.PreviousCell = motion.Cell;
            animal.MoveTo(motion.Target);
        }
        motion.Cell = motion.Target;
        motion.Target = null;
        motion.Rest = RandomRest(motion);
    }

    private void BeginStep(Motion motion)
    {
        motion.Start = motion.Position;
        motion.Progress = 0f;
        // SmoothStep 的峰值速度为平均速度的 1.5 倍。
        motion.Duration = Mathf.Max(0.05f, 1.5f * Vector3.Distance(motion.Start,
            motion.Target.WorldPosition + motion.Offset) / Mathf.Max(0.05f, moveSpeed * motion.Behaviour.SpeedMultiplier));
    }

    private float RandomRest(Motion motion) => Random.Range(Mathf.Max(0f, restSeconds.x),
        Mathf.Max(restSeconds.x, restSeconds.y, 0f)) * motion.Behaviour.RestMultiplier;

    private void OnPhaseChanged(DayPhase phase)
    {
        movementLocked = phase == DayPhase.NightSettlement || phase == DayPhase.GameSettlement;
        if (movementLocked) FreezeAtCurrentPositions();
    }

    /// <summary>
    /// 在建立结算世界快照前固定当前位置。走到两格之间时，以显示位置更接近的合法格为起点；
    /// 不回到投放位置，也不强行完成尚未走到的路程。重复调用不会再次移动。
    /// </summary>
    private void FreezeAtCurrentPositions()
    {
        AnimalSpawner spawner = animalBinder != null ? animalBinder.Spawner : null;
        if (spawner == null) return;
        idleTime = 0f;
        foreach (var pair in motions)
        {
            AnimalsBase animal = pair.Key;
            Motion motion = pair.Value;
            TileCell cell = animal.CurrentCell as TileCell;
            if (animal.IsDead || !ValidCell(cell, spawner.Map)) continue;

            // 已经被其他结算或外部逻辑移动过时，不能用旧动画覆盖新的逻辑位置。
            if (cell == motion.Cell && animal.LastSettledDay == motion.SettledDay
                && motion.Target != null && motion.Target != cell
                && ValidCell(motion.Target, spawner.Map) && IsNeighbor(cell, motion.Target)
                && passage.CanEnter(animal, motion.Target))
            {
                Vector3 position = motion.Position - motion.Offset;
                if ((position - motion.Target.WorldPosition).sqrMagnitude
                    < (position - cell.WorldPosition).sqrMagnitude)
                {
                    cell = motion.Target;
                    animal.MoveTo(cell);
                }
            }

            motion.Cell = cell;
            motion.Target = null;
            motion.PreviousCell = null;
            motion.SettledDay = animal.LastSettledDay;
            motion.Behaviour.Reset(cell);
            motion.Position = cell.WorldPosition + motion.Offset;
            motion.Rest = RandomRest(motion);
            if (motion.Visual != null) motion.Visual.transform.position = motion.Position;
        }
    }

    private static bool IsNeighbor(ICellEnv cell, ICellEnv target)
    {
        foreach (ICellEnv neighbor in cell.Neighbors)
            if (ReferenceEquals(neighbor, target)) return true;
        return false;
    }

    private static bool ValidCell(TileCell cell, TileMapManager map) => cell != null && map != null
        && cell.Manager == map && map.TryGetCellAtTilemapPosition(cell.TilemapPosition, out TileCell current)
        && current == cell;

    private void RestorePositions()
    {
        foreach (var pair in motions)
        {
            Motion motion = pair.Value;
            if (!pair.Key.IsDead && pair.Key.CurrentCell == motion.Cell
                && pair.Key.LastSettledDay == motion.SettledDay && motion.Visual != null)
                motion.Visual.transform.position = motion.Position;
        }
    }

    private void CleanupDead(AnimalSpawner spawner)
    {
        bool dirty = false;
        foreach (AnimalsBase animal in spawner.Animals)
            if (animal != null && animal.IsDead) { dirty = true; break; }
        if (!dirty)
            foreach (var pair in animalBinder.Visuals)
                if (pair.Key.IsDead) { dirty = true; break; }
        if (dirty)
        {
            spawner.RemoveDead();
            animalBinder.SyncNow();
        }
        if (plantBinder != null)
        {
            foreach (var pair in plantBinder.Visuals)
                if (pair.Key.IsDead) { dirty = true; break; }
            if (dirty) plantBinder.SyncNow();
        }
    }

    private void OnSettled(AnimalSettleReport report)
    {
        // 只同步外观和登记表，余屑数量由原结算器负责，避免重复回收。
        if (animalBinder != null)
        {
            if (animalBinder.Spawner != null) animalBinder.Spawner.RemoveDead();
            animalBinder.SyncNow();
        }
        if (plantBinder != null) plantBinder.SyncNow();
        idleTime = 0f;
    }
}
