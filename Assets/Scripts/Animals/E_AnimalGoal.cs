/// <summary>
/// 动物每晚选择的主要目标。
/// 策划案要求每晚按优先级只选 1 个主要目标：先危险、再携带物、再饥饿、最后常规行为。
/// </summary>
public enum E_AnimalGoal
{
    /// <summary>没有合法目标，本晚休眠，不凭空生成行动。</summary>
    None,

    /// <summary>前往食物：植物、余屑或猎物。</summary>
    Food,

    /// <summary>前往庇护点：成熟植物、巢穴或遮蔽格。</summary>
    Shelter,

    /// <summary>携带物的送达点，例如蚂蚁要把种子送到空地。</summary>
    Delivery,

    /// <summary>逃离捕食者所在的区域。</summary>
    Escape
}
