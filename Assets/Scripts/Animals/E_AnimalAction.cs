/// <summary>
/// 每晚执行的那个主要动作。
/// 策划案限制"每只动物每晚最多移动 3 格并执行 1 个主要动作"，
/// 因此即便移动到多种资源附近，也只能挑一样做。
/// </summary>
public enum E_AnimalAction
{
    /// <summary>没有动作，原地休眠。</summary>
    None,

    /// <summary>捕食另一只动物。</summary>
    Hunt,

    /// <summary>取食植物，通过 TakeBite 扣减份量。</summary>
    Graze,

    /// <summary>取食本格余屑。</summary>
    Scavenge,

    /// <summary>松土：蚯蚓特有，给本格或与本格相邻的种子植物额外成长点。</summary>
    Till,

    /// <summary>搬运：蚂蚁特有，把种子从一处搬到另一处。</summary>
    Carry,

    /// <summary>结网：蜘蛛特有，改变通路与风险区。</summary>
    Weave,

    /// <summary>进入庇护点休息，本晚不再成为有效捕食目标。</summary>
    Hide
}
