using System.Text;

/// <summary>从逻辑对象读取点击时的状态，不依赖外观名称或上一次结算日志。</summary>
public static class SpeciesStatusText
{
    public static string For(PlantsBase plant)
    {
        if (plant == null)
            return string.Empty;

        var text = new StringBuilder();
        text.Append("【植物观察】").AppendLine(plant.DisplayName);
        text.Append("位置：(").Append(plant.Coordinates.x).Append(", ").Append(plant.Coordinates.y).AppendLine(")");
        text.Append("状态：").AppendLine(plant.IsDead ? "已回收" : plant.IsWithering ? "枯萎" : "正常");
        text.Append("占位：").AppendLine(plant.OccupancySlot == E_OccupancySlot.Cover ? "地表覆盖" : "主体植物");
        if (plant is SporePlantsBase spore)
        {
            text.Append("活力：").Append(spore.Vitality).AppendLine();
            text.Append("扩张值：").Append(spore.Expansion).AppendLine();
            text.Append("产出冷却：").Append(spore.ProduceCooldown).AppendLine();
        }
        else if (plant is SeedPlantsBase seed)
        {
            text.Append("生长阶段：").AppendLine(seed.StageName);
            text.Append("成长点：").Append(seed.GrowthPoints).AppendLine();
            text.Append("水合标记：").AppendLine(seed.HasHydration ? "有" : "无");
            text.Append("光合标记：").AppendLine(seed.HasPhotosynthesis ? "有" : "无");
            text.Append("地面种子：").AppendLine(seed.HasSeedOnGround ? "有" : "无");
            text.Append("疯长药水：").AppendLine(seed.PotionUsed ? "已使用" : "未使用");
        }
        text.Append("最近结算：").Append(SettlementDay(plant.LastSettledDay));
        return text.ToString();
    }

    public static string For(AnimalsBase animal)
    {
        if (animal == null)
            return string.Empty;

        var text = new StringBuilder();
        text.Append("【动物观察】").AppendLine(animal.DisplayName);
        text.Append("位置：(").Append(animal.Coordinates.x).Append(", ").Append(animal.Coordinates.y).AppendLine(")");
        text.Append("状态：").AppendLine(Hunger(animal.HungerState));
        text.Append("饱腹：").Append(animal.Satiety).Append('/').Append(AnimalsBase.MaxSatiety).AppendLine();
        text.Append("饥饿计数：").Append(animal.Starvation).Append('/').Append(AnimalsBase.MaxStarvation).AppendLine();
        text.Append("当前目标：").AppendLine(Goal(animal.Goal));
        text.Append("携带物：").AppendLine(Resource(animal.Carried));
        text.Append("庇护状态：").AppendLine(animal.IsSheltered ? "受到庇护" : "未受庇护");
        text.Append("今晚移动上限：").Append(animal.MaxStepsForTonight).AppendLine(" 格");
        text.Append("连续受阻：").Append(animal.FailedNights).AppendLine(" 晚");
        text.Append("最近结算：").Append(SettlementDay(animal.LastSettledDay));
        return text.ToString();
    }

    private static string SettlementDay(int day) => day > 0 ? "第 " + day + " 天" : "尚未结算";

    private static string Hunger(E_HungerState state)
    {
        switch (state)
        {
            case E_HungerState.Normal: return "正常";
            case E_HungerState.Hungry: return "饥饿";
            case E_HungerState.Weak: return "衰弱";
            case E_HungerState.Dead: return "死亡";
            default: return "未知";
        }
    }

    private static string Goal(E_AnimalGoal goal)
    {
        switch (goal)
        {
            case E_AnimalGoal.Food: return "寻找食物";
            case E_AnimalGoal.Shelter: return "寻找庇护";
            case E_AnimalGoal.Delivery: return "运送携带物";
            case E_AnimalGoal.Escape: return "躲避捕食者";
            default: return "无";
        }
    }

    private static string Resource(E_ResourceKind resource)
    {
        switch (resource)
        {
            case E_ResourceKind.Detritus: return "余屑";
            case E_ResourceKind.Light: return "光粒";
            case E_ResourceKind.Spore: return "孢子";
            case E_ResourceKind.Seed: return "种子";
            case E_ResourceKind.Fibre: return "织材";
            default: return "无";
        }
    }
}
