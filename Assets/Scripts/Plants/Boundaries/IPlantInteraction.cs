/// <summary>
/// 动物对植物做的事。植物实现它，动物层调用。
/// 首版全部是安全的默认实现，等动物系统接入时再由具体物种覆写，
/// 这样植物不会因为动物尚未完成而阻塞。
/// </summary>
public interface IPlantInteraction
{
    /// <summary>被取食；返回是否真的吃到了东西。</summary>
    bool TakeBite(int amount);

    /// <summary>获得额外成长点，例如蚯蚓松土或授粉。</summary>
    void GrantGrowth(int points);

    /// <summary>种子被搬走或吃掉。</summary>
    void OnSeedTaken();

    /// <summary>能否作为动物的庇护所。</summary>
    bool IsShelter { get; }

    /// <summary>作为食物的份量；返回 0 表示不可食用。</summary>
    int FoodAmount { get; }
}