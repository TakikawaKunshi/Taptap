using System;

/// <summary>
/// 一条可解释的结算记录。
/// 策划案要求玩家能回答"为什么它长了／死了／迁移了"，
/// 因此每条规则产出一条带编号和中文说明的记录，直接写入事件日志。
/// </summary>
[Serializable]
public struct PlantLogEntry
{
    /// <summary>规则编号，例如 SPORE.MOSS.RAIN，用于"为什么"按钮定位到具体规则。</summary>
    public string RuleId;

    /// <summary>中文说明，可直接显示。</summary>
    public string Text;

    public PlantLogEntry(string ruleId, string text)
    {
        RuleId = ruleId;
        Text = text;
    }

    public override string ToString() => RuleId + " " + Text;
}