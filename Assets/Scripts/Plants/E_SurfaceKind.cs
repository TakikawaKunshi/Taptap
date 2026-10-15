/// <summary>
/// 地表类型。策划案里苔藓可以生长在石面和木面上，藻类需要积水或湿润地表，
/// 这些不是天气标签而是地图本身的属性，所以由格子层提供，不参与天气结算。
/// </summary>
public enum E_SurfaceKind
{
    Ground, // 默认地面
    Stone,  // 石面
    Wood    // 木面
}