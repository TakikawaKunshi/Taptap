/// <summary>
/// 饥饿状态。策划案第六章的饥饿与死亡规则：
/// 正常（饱腹 1-3）、饥饿（饱腹 0 且计数 1，移动 +1 格）、
/// 衰弱（计数 2，移动 -1 格且不能捕食大型目标）、死亡或离箱（计数 3，转为余屑）。
/// </summary>
public enum E_HungerState
{
    /// <summary>饱腹 1 至 3，按物种偏好寻找食物、庇护或搬运目标。</summary>
    Normal,

    /// <summary>饱腹 0、饥饿计数 1：移动距离增加 1 格。</summary>
    Hungry,

    /// <summary>饥饿计数 2：移动距离减少 1 格，无法主动捕食大型目标，优先最近食物。</summary>
    Weak,

    /// <summary>饥饿计数 3：死亡或离箱，对象移除并生成余屑。</summary>
    Dead
}
