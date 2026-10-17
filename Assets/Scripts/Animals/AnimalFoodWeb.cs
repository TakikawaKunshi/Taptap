using System.Collections.Generic;

/// <summary>
/// 食物网关系表。策划案第六章的"候选生物捕食关系表"集中落在这里，
/// 让每种动物的代码只描述自己的行为，而不用各自维护谁能吃谁。
///
/// 当前实现 MVP 五种与奶蛙；鱼类、沙蚕、麻雀的关系保留供后续扩展。
/// 奶蛙不配置更高捕食者。
/// </summary>
public static class AnimalFoodWeb
{
    // 捕食关系：键是捕食者，值是它能吃的猎物集合。
    private static readonly Dictionary<E_AnimalSpecies, HashSet<E_AnimalSpecies>> Prey =
        new Dictionary<E_AnimalSpecies, HashSet<E_AnimalSpecies>>
        {
            // 策划案：青蛙捕食蚂蚁、蜘蛛、蚯蚓、蜗牛。
            {
                E_AnimalSpecies.Frog, new HashSet<E_AnimalSpecies>
                {
                    E_AnimalSpecies.Ant,
                    E_AnimalSpecies.Spider,
                    E_AnimalSpecies.Earthworm,
                    E_AnimalSpecies.Snail
                }
            },
            // 策划案：蜘蛛捕食蚂蚁、幼虫、弱小小型动物；MVP 里只落到蚂蚁。
            {
                E_AnimalSpecies.Spider, new HashSet<E_AnimalSpecies>
                {
                    E_AnimalSpecies.Ant
                }
            },
            // 扩展捕食者的食谱，首版不实例化这些物种，保留以便后续直接启用。
            {
                E_AnimalSpecies.Fish, new HashSet<E_AnimalSpecies>
                {
                    E_AnimalSpecies.Sandworm,
                    E_AnimalSpecies.Earthworm
                }
            },
            {
                E_AnimalSpecies.Sparrow, new HashSet<E_AnimalSpecies>
                {
                    E_AnimalSpecies.Ant,
                    E_AnimalSpecies.Spider,
                    E_AnimalSpecies.Snail
                }
            },
            {
                E_AnimalSpecies.MilkFrog, new HashSet<E_AnimalSpecies>
                {
                    E_AnimalSpecies.Frog,
                    E_AnimalSpecies.Fish,
                    E_AnimalSpecies.Sandworm,
                    E_AnimalSpecies.Sparrow,
                    // 食物网表也在这些猎物的“谁会捕食它”栏列出了奶蛙。
                    E_AnimalSpecies.Earthworm,
                    E_AnimalSpecies.Snail,
                    E_AnimalSpecies.Spider
                }
            }
        };

    // 谁会捕食它：键是猎物，值是捕食者集合。由上面的表反转得到，避免两处维护。
    private static readonly Dictionary<E_AnimalSpecies, HashSet<E_AnimalSpecies>> Predators =
        BuildPredators();

    /// <summary>predator 是否把 prey 列为猎物。</summary>
    public static bool CanPrey(E_AnimalSpecies predator, E_AnimalSpecies prey)
    {
        HashSet<E_AnimalSpecies> targets;
        return Prey.TryGetValue(predator, out targets) && targets.Contains(prey);
    }

    /// <summary>prey 是否被 predator 捕食；用于"感知危险"的判断。</summary>
    public static bool IsThreatenedBy(E_AnimalSpecies prey, E_AnimalSpecies predator)
    {
        return CanPrey(predator, prey);
    }

    /// <summary>该格子是否提供有效庇护：任一存活植物声明自己可作庇护。</summary>
    public static bool HasShelter(ICellEnv cell)
    {
        if (cell == null)
            return false;

        PlantsBase subject = cell.Subject;
        if (subject != null && !subject.IsDead && subject.IsShelter)
            return true;

        IReadOnlyList<PlantsBase> cover = cell.Cover;
        for (int i = 0; i < cover.Count; i++)
        {
            if (cover[i] != null && !cover[i].IsDead && cover[i].IsShelter)
                return true;
        }

        return false;
    }

    /// <summary>该格是否存在能捕食 target 的动物；用于决定要不要逃。</summary>
    public static bool AnyPredatorNear(IAnimalWorld world, AnimalsBase target, int radius)
    {
        if (world == null || target == null || target.CurrentCell == null)
            return false;

        // 以目标所在格为中心，在半径内的所有格子里找捕食者。
        IReadOnlyList<ICellEnv> cells = world.Cells;
        for (int i = 0; i < cells.Count; i++)
        {
            ICellEnv cell = cells[i];
            if (cell == null)
                continue;

            int distance = world.StepDistance(target.CurrentCell, cell);
            if (distance < 0 || distance > radius)
                continue;

            IReadOnlyList<AnimalsBase> here = world.AnimalsAt(cell);
            for (int a = 0; a < here.Count; a++)
            {
                AnimalsBase other = here[a];
                if (other == null || other.IsDead || other == target)
                    continue;
                if (IsThreatenedBy(target.Species, other.Species))
                    return true;
            }
        }

        return false;
    }

    private static Dictionary<E_AnimalSpecies, HashSet<E_AnimalSpecies>> BuildPredators()
    {
        var result = new Dictionary<E_AnimalSpecies, HashSet<E_AnimalSpecies>>();
        foreach (KeyValuePair<E_AnimalSpecies, HashSet<E_AnimalSpecies>> pair in Prey)
        {
            foreach (E_AnimalSpecies victim in pair.Value)
            {
                HashSet<E_AnimalSpecies> list;
                if (!result.TryGetValue(victim, out list))
                {
                    list = new HashSet<E_AnimalSpecies>();
                    result[victim] = list;
                }

                list.Add(pair.Key);
            }
        }

        return result;
    }
}
