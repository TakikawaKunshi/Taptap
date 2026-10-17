using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>格子可选的余屑老化能力；结算不依赖具体地图组件。</summary>
public interface IDetritusLifecycle
{
    int AdvanceDetritusDay(int day);
}

/// <summary>按产生时的结算日期保存余屑；两次后续过夜后到期，消耗时先取最旧的一批。</summary>
[Serializable]
public sealed class DetritusLifecycle
{
    public const int LifetimeDays = 2;

    [Serializable]
    private sealed class Batch
    {
        public int day;
        public int amount;
    }

    [SerializeField] private int currentDay;
    [SerializeField] private int amount;
    [SerializeField] private List<Batch> batches = new List<Batch>();

    public DetritusLifecycle(int startingDay = 0)
    {
        currentDay = Math.Max(0, startingDay);
    }

    public int Amount => amount;

    /// <summary>兼容原有 Detritus += / -= 写法；新增部分单独计时，扣减不刷新剩余批次寿命。</summary>
    public void SetAmount(int value)
    {
        value = Math.Max(0, value);
        int delta = value - amount;
        if (delta > 0)
        {
            if (batches.Count > 0 && batches[batches.Count - 1].day == currentDay)
                batches[batches.Count - 1].amount += delta;
            else
                batches.Add(new Batch { day = currentDay, amount = delta });
        }
        else if (delta < 0)
        {
            int remaining = -delta;
            while (remaining > 0 && batches.Count > 0)
            {
                Batch first = batches[0];
                int consumed = Math.Min(remaining, first.amount);
                first.amount -= consumed;
                remaining -= consumed;
                if (first.amount == 0) batches.RemoveAt(0);
            }
        }
        amount = value;
    }

    /// <summary>在本晚物种读取资源前执行；重复或回放较早日期均不会再次老化。</summary>
    public int AdvanceDay(int day)
    {
        if (day <= currentDay) return 0;
        currentDay = day;
        int expired = 0;
        while (batches.Count > 0 && (long)day - batches[0].day >= LifetimeDays)
        {
            expired += batches[0].amount;
            batches.RemoveAt(0);
        }
        amount -= expired;
        return expired;
    }

    /// <summary>植物、动物共享入口；同一天先后调用只会清理一次，也覆盖没有存活物种的格子。</summary>
    public static void SettleCells(int day, IEnumerable<ICellEnv> cells, ICollection<PlantLogEntry> logs)
    {
        foreach (ICellEnv cell in cells)
        {
            if (!(cell is IDetritusLifecycle lifetime)) continue;
            int expired = lifetime.AdvanceDetritusDay(day);
            if (expired > 0)
                logs.Add(new PlantLogEntry("RESOURCE.DETRITUS.EXPIRE",
                    "格子 " + cell.Coordinates + " 的 " + expired + " 份余屑已存留两天，自动消失。"));
        }
    }
}
