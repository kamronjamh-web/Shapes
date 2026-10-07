using System;
using System.Collections.Generic;
using Godot;

namespace SuperGatchaRebuild.Equipment;

public static class WeightedRandom
{
    public static T Pick<T>(IReadOnlyList<T> items, Func<T, float> getWeight, RandomNumberGenerator rng)
    {
        int idx = PickIndex(items, getWeight, rng);
        return items[idx];
    }

    public static int PickIndex<T>(IReadOnlyList<T> items, Func<T, float> getWeight, RandomNumberGenerator rng)
    {
        if (items == null || items.Count == 0)
        {
            throw new ArgumentException("WeightedRandom requires at least one item.");
        }

        float total = 0f;
        for (int i = 0; i < items.Count; i++)
        {
            total += Mathf.Max(0f, getWeight(items[i]));
        }

        if (total <= 0f)
        {
            // Fallback to uniform random if all weights are zero/negative.
            return rng.RandiRange(0, items.Count - 1);
        }

        float roll = rng.RandfRange(0f, total);
        float accum = 0f;
        for (int i = 0; i < items.Count; i++)
        {
            accum += Mathf.Max(0f, getWeight(items[i]));
            if (roll <= accum)
            {
                return i;
            }
        }

        return items.Count - 1;
    }
}
