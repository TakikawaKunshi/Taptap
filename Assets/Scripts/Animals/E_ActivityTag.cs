/// <summary>
/// 活动标签。策划案要求它"受晴雨和地形影响，改变目标评分"，
/// 因此标签本身不产生行为，只在评分和参与判断时被读取。
/// 用位掩码组合：一只动物可以同时具备多个标签，例如穴居且雨行。
/// </summary>
[System.Flags]
public enum E_ActivityTag
{
    None = 0,

    /// <summary>昼行：晴天活跃度更高，雨天评分打折。</summary>
    Diurnal = 1 << 0,

    /// <summary>雨行：雨天活跃度更高，移动更远（青蛙沿雨印移动）。</summary>
    RainActive = 1 << 1,

    /// <summary>水生：需要湿润或积水才能正常活动，干燥时移动受限。</summary>
    Aquatic = 1 << 2,

    /// <summary>穴居：可从地下通过，受地表阻挡影响小。</summary>
    Burrowing = 1 << 3,

    /// <summary>攀爬：可通过主体植物所占格，不觉其障碍。</summary>
    Climbing = 1 << 4
}
