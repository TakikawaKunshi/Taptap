using System.Collections.Generic;

/// <summary>
/// 一只动物在一晚结算中提出的变更提案。
/// 与 <see cref="PlantSettlePlan"/> 同一套思路：先只读地算，再由编排器统一裁决写回。
/// 这样"同一资源点每晚只允许一份资源被预留"这类竞争可以统一处理，
/// 而且裁决结果不依赖动物的遍历顺序，满足策划案的可复现性要求。
///
/// 本类型描述"打算去哪、打算做什么"，目标对象的引用是只读用途：
/// 真正修改植物或格子要落到字段上，由编排器写回。
/// </summary>
public sealed class AnimalSettlePlan
{
    /// <summary>本晚的主要目标类型。</summary>
    public E_AnimalGoal Goal { get; set; } = E_AnimalGoal.None;

    /// <summary>申请移动到的目标格；为 null 表示本晚原地不动。</summary>
    public ICellEnv MoveTarget { get; set; }

    /// <summary>移动是否被编排器批准；被地形或标签拒绝时保留在原格。</summary>
    public bool MoveGranted { get; set; }

    /// <summary>本晚要执行的那个主要动作。</summary>
    public E_AnimalAction Action { get; set; } = E_AnimalAction.None;

    /// <summary>取食或捕食的目标植物；仅在 Graze / Till 时有意义。</summary>
    public PlantsBase PlantTarget { get; set; }

    /// <summary>捕食的目标动物；仅在 Hunt 时有意义。</summary>
    public AnimalsBase AnimalTarget { get; set; }

    /// <summary>动作的索引目标格，例如蚂蚁要把种子送到这里。</summary>
    public ICellEnv ActionCell { get; set; }

    /// <summary>取食份量，写回时按目标实际剩余量扣减。</summary>
    public int BiteAmount { get; set; }

    /// <summary>本格余屑的增减，取食余屑时为负。</summary>
    public int DetritusDelta { get; set; }

    /// <summary>本晚恢复的饱腹。</summary>
    public int SatietyGain { get; set; }

    /// <summary>松土时给目标植物的额外成长点。</summary>
    public int GrowthPointsToGrant { get; set; }

    /// <summary>搬运的资源种类，例如蚂蚁搬的种子。</summary>
    public E_ResourceKind CarryKind { get; set; } = E_ResourceKind.None;

    /// <summary>拾起的是哪一株植物的种子；编排器据此播种同一种植物。</summary>
    public PlantsBase SeedSource { get; set; }

    /// <summary>是否拾起携带物；与 PutDownCarried 互斥。</summary>
    public bool PickUpCarried { get; set; }

    /// <summary>是否放下携带物。</summary>
    public bool PutDownCarried { get; set; }

    /// <summary>本晚是否进入庇护；进入庇护后不再成为有效捕食目标。</summary>
    public bool EntersShelter { get; set; }

    /// <summary>饥饿计数是否累加；本晚成功进食时不累加。</summary>
    public bool CountStarvation { get; set; }

    /// <summary>饥饿是否归零；成功进食后置真。</summary>
    public bool ResetStarvation { get; set; }

    /// <summary>本晚因饥饿达到上限而死亡。</summary>
    public bool StarvedToDeath { get; set; }

    /// <summary>死亡后回收的余屑数量。</summary>
    public int RecycleToDetritus { get; set; }

    /// <summary>目标无法实现时保留目标、累计失败夜数。</summary>
    public bool AdvanceFailedNights { get; set; }

    /// <summary>目标已达成，清空记忆中的失败计数。</summary>
    public bool ResetFailedNights { get; set; }

    /// <summary>本晚记住的食物位置。</summary>
    public ICellEnv RememberFood { get; set; }

    /// <summary>本晚记住的安全点。</summary>
    public ICellEnv RememberSafety { get; set; }

    public List<PlantLogEntry> Logs { get; } = new List<PlantLogEntry>();

    /// <summary>追加一条可解释记录；与植物共用同一类型，晨报才能合并渲染。</summary>
    public void Log(string ruleId, string text)
    {
        Logs.Add(new PlantLogEntry(ruleId, text));
    }

    /// <summary>是否没有任何实际变化，供编排器统计"未变化"数量。</summary>
    public bool IsEmpty
    {
        get
        {
            return MoveTarget == null
                   && Action == E_AnimalAction.None
                   && DetritusDelta == 0
                   && SatietyGain == 0
                   && GrowthPointsToGrant == 0
                   && !CountStarvation
                   && !ResetStarvation
                   && !StarvedToDeath
                   && !PickUpCarried
                   && !PutDownCarried;
        }
    }
}
