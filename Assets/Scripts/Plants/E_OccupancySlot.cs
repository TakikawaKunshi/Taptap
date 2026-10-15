/// <summary>
/// 植物在格子上占用的位置。策划案的规则是：成熟植物、石头和巢穴占主体格，
/// 苔藓与藻类作为地表覆盖可与主体共存，因此占位方式必须由物种自己声明，
/// 不能按"孢子植物／种子植物"的基类来二分。
/// </summary>
public enum E_OccupancySlot
{
    /// <summary>地表覆盖位；可以与主体植物共存于同一格。</summary>
    Cover,

    /// <summary>主体位；一格里至多一个，会为相邻格提供遮蔽。</summary>
    Subject
}
