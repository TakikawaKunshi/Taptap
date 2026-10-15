using System.Collections.Generic;

/// <summary>
/// 一株植物在一晚结算中提出的变更提案。
/// 分成"只读地算"和"统一地写"两步，是为了让资源争用与空间竞争可以插在中间裁决，
/// 而且裁决结果不依赖对象的遍历顺序，满足策划案的可复现性要求。
/// 日志在 BuildPlan 与 ApplyPlan 两步都会追加，结算方应在两步都结束后再读取 Logs。
///
/// 本类型只描述"打算怎么改"，不持有植物实例的引用：
/// BuildPlan 必须是纯读的，任何状态变化都要落成这里的字段，由 ApplyPlan 统一写回。
/// 否则"预览今晚会发生什么"这类只读调用会污染植物自身状态。
/// </summary>
public sealed class PlantSettlePlan
{
    /// <summary>活力增减。写入时会套用策划案的每晚上限，见 SporePlantsBase。</summary>
    public int VitalityDelta { get; set; }

    /// <summary>是否申请向一个相邻空格扩张；由结算方裁决目标并真正放置新植物。</summary>
    public bool RequestsExpansion { get; set; }

    /// <summary>
    /// 扩张申请是否被批准。由结算方在裁决后置为 true，
    /// 植物在写回阶段据此累计扩张值，区分"申请过"和"真的扩到了"。
    /// </summary>
    public bool ExpansionGranted { get; set; }

    /// <summary>产出的资源种类与数量。</summary>
    public E_ResourceKind ProduceKind { get; set; } = E_ResourceKind.None;

    public int ProduceAmount { get; set; }

    /// <summary>对该格余屑的增减，可以为负。</summary>
    public int DetritusDelta { get; set; }

    /// <summary>枯萎回收时产出的余屑。大于 0 表示本晚直接从地图移除。</summary>
    public int RecycleToDetritus { get; set; }

    /// <summary>获得的成长点数量。</summary>
    public int GrowthPointsDelta { get; set; }

    /// <summary>本晚是否获得水合标记；写回阶段才落到植物上。</summary>
    public bool GainHydration { get; set; }

    /// <summary>本晚是否获得光合标记；写回阶段才落到植物上。</summary>
    public bool GainPhotosynthesis { get; set; }

    /// <summary>本晚是否累计一次成熟后的雨夜；写回阶段才落到植物上。</summary>
    public bool CountRainNight { get; set; }

    /// <summary>是否把成熟后的雨夜计数清零；与 CountRainNight 互斥，由写回阶段处理。</summary>
    public bool ResetRainNightCounter { get; set; }

    /// <summary>产出冷却的变化量，通常为 -1。</summary>
    public int ProduceCooldownDelta { get; set; }

    /// <summary>是否发生了阶段变化。</summary>
    public bool StageChanged { get; set; }

    /// <summary>变化后的阶段，仅在 StageChanged 为真时有意义。</summary>
    public E_PlantStage? NewStage { get; set; }

    /// <summary>本晚的可解释记录。</summary>
    public List<PlantLogEntry> Logs { get; } = new List<PlantLogEntry>();

    public void Log(string ruleId, string text)
    {
        Logs.Add(new PlantLogEntry(ruleId, text));
    }

    /// <summary>是否什么也没发生；用于让结算方跳过这株植物。</summary>
    public bool IsEmpty => VitalityDelta == 0
        && !RequestsExpansion
        && !ExpansionGranted
        && ProduceAmount == 0
        && DetritusDelta == 0
        && RecycleToDetritus == 0
        && GrowthPointsDelta == 0
        && ProduceCooldownDelta == 0
        && !GainHydration
        && !GainPhotosynthesis
        && !CountRainNight
        && !ResetRainNightCounter
        && !StageChanged;
}