/// <summary>
/// 资源种类。与策划案的资源状态表一致：光粒、余屑、织材、种子和食物库存。
/// 余屑同时也是格子状态，见 ICellEnv.Detritus；本枚举用于跨格子的产出与消耗记账。
/// </summary>
public enum E_ResourceKind
{
    None,     // 无产出
    Detritus, // 余屑，蘑菇与藻类的生长条件，枯萎回收的产物
    Light,    // 光粒，藻类在雨天消耗余屑时附带产出
    Spore,    // 孢子，成熟蘑菇的产出
    Seed,     // 种子，成熟种子植物的产出，可被蚂蚁搬运
    Fibre     // 织材，留给后续的搬运与巢穴行为
}