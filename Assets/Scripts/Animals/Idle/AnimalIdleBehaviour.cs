using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>每只动物独立持有的闲逛策略。只选择下一格，不执行取食、搬运或每日结算。</summary>
public abstract class AnimalIdleBehaviour
{
    public abstract float SpeedMultiplier { get; }
    public abstract float RestMultiplier { get; }
    public virtual void Reset(ICellEnv cell) { }
    public abstract ICellEnv ChooseStep(AnimalsBase animal, IAnimalWorld world, ICellEnv previous);

    public static AnimalIdleBehaviour Create(E_AnimalSpecies species)
    {
        switch (species)
        {
            case E_AnimalSpecies.Frog: return new FrogIdleBehaviour();
            case E_AnimalSpecies.Earthworm: return new EarthwormIdleBehaviour();
            case E_AnimalSpecies.Snail: return new SnailIdleBehaviour();
            case E_AnimalSpecies.Ant: return new AntIdleBehaviour();
            case E_AnimalSpecies.Spider: return new SpiderIdleBehaviour();
            case E_AnimalSpecies.MilkFrog: return new MilkFrogIdleBehaviour();
            default: return new StationaryBehaviour();
        }
    }

    private sealed class StationaryBehaviour : AnimalIdleBehaviour
    {
        public override float SpeedMultiplier => 1f;
        public override float RestMultiplier => 1f;
        public override ICellEnv ChooseStep(AnimalsBase animal, IAnimalWorld world, ICellEnv previous)
            => animal.CurrentCell;
    }

    /// <summary>候选评分加入少量随机变化；有多个选择时减少立即折返，仍允许走出死路。</summary>
    protected static ICellEnv Wander(AnimalsBase animal, IAnimalWorld world, ICellEnv previous,
        Func<ICellEnv, float> preference, Predicate<ICellEnv> allowed = null)
    {
        ICellEnv best = null;
        float bestScore = float.NegativeInfinity;
        foreach (ICellEnv next in animal.CurrentCell.Neighbors)
        {
            if (next == null || !world.CanEnter(animal, next) || (allowed != null && !allowed(next))) continue;
            float score = preference(next) + UnityEngine.Random.value * 0.5f - (next == previous ? 0.75f : 0f);
            if (score <= bestScore) continue;
            best = next;
            bestScore = score;
        }
        return best ?? animal.CurrentCell;
    }

    /// <summary>通过可通行邻格搜索最近目标，返回路径第一步；已经到达则返回当前格。</summary>
    protected static ICellEnv FindStep(AnimalsBase animal, IAnimalWorld world, int radius,
        Predicate<ICellEnv> matches, out ICellEnv destination)
    {
        destination = null;
        var queue = new Queue<ICellEnv>();
        var distance = new Dictionary<ICellEnv, int> { { animal.CurrentCell, 0 } };
        var first = new Dictionary<ICellEnv, ICellEnv> { { animal.CurrentCell, animal.CurrentCell } };
        queue.Enqueue(animal.CurrentCell);
        while (queue.Count > 0)
        {
            ICellEnv cell = queue.Dequeue();
            if (matches(cell))
            {
                destination = cell;
                return first[cell];
            }
            if (distance[cell] >= radius) continue;
            foreach (ICellEnv next in cell.Neighbors)
            {
                if (next == null || distance.ContainsKey(next) || !world.CanEnter(animal, next)) continue;
                distance[next] = distance[cell] + 1;
                first[next] = cell == animal.CurrentCell ? next : first[cell];
                queue.Enqueue(next);
            }
        }
        return null;
    }

    protected static ICellEnv PreyStep(AnimalsBase animal, IAnimalWorld world, int radius)
    {
        if (animal.Satiety > 0) return null;
        return FindStep(animal, world, radius, cell =>
        {
            if (AnimalFoodWeb.HasShelter(cell)) return false;
            foreach (AnimalsBase prey in world.AnimalsAt(cell))
                if (prey != null && !prey.IsDead && prey.CurrentCell == cell
                    && AnimalFoodWeb.CanPrey(animal.Species, prey.Species)
                    && (animal.HungerState != E_HungerState.Weak || prey.BodySize != E_BodySize.Large)) return true;
            return false;
        }, out _);
    }

    protected static bool HasPlant(ICellEnv cell, Predicate<PlantsBase> matches)
    {
        if (cell.Subject != null && !cell.Subject.IsDead && matches(cell.Subject)) return true;
        foreach (PlantsBase plant in cell.Cover)
            if (plant != null && !plant.IsDead && matches(plant)) return true;
        return false;
    }

    protected static bool TryEscape(AnimalsBase animal, IAnimalWorld world, out ICellEnv step)
    {
        step = null;
        if (!AnimalFoodWeb.AnyPredatorNear(world, animal, 1)) return false;
        step = animal.CurrentCell;
        if (AnimalFoodWeb.HasShelter(step)) return true;
        float best = Safety(step, animal, world);
        foreach (ICellEnv next in animal.CurrentCell.Neighbors)
        {
            if (next == null || !world.CanEnter(animal, next)) continue;
            float score = Safety(next, animal, world);
            if (score <= best) continue;
            best = score;
            step = next;
        }
        return true;
    }

    private static float Safety(ICellEnv cell, AnimalsBase animal, IAnimalWorld world)
    {
        float danger = 0f;
        foreach (ICellEnv otherCell in world.Cells)
        {
            int distance = world.StepDistance(cell, otherCell);
            if (distance < 0 || distance > 2) continue;
            foreach (AnimalsBase other in world.AnimalsAt(otherCell))
                if (!other.IsDead && AnimalFoodWeb.CanPrey(other.Species, animal.Species)) danger += 3 - distance;
        }
        return (AnimalFoodWeb.HasShelter(cell) ? 20f : 0f) - danger;
    }
}
