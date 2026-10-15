/// <summary>
/// 体型分级。
/// 策划案规定"衰弱（饥饿计数 2）时无法主动捕食大型目标"，因此需要一个粗粒度的体型，
/// 不引入写实体重：谁能吃什么由食物网表决定，体型只参与衰弱时的限制判断。
/// </summary>
public enum E_BodySize
{
    /// <summary>小型：蚂蚁、蜗牛、蚯蚓、蜘蛛等无脊椎动物。</summary>
    Small,

    /// <summary>中型：青蛙这类捕食者。</summary>
    Medium,

    /// <summary>大型：扩展的奶蛙等顶级捕食者。</summary>
    Large
}
