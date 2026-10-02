using System;
using System.Collections.Generic;

namespace PKHeX.Core.Injection;

/// <summary>
/// Array reusable logic
/// </summary>
public static class ArrayUtil
{
    public static bool Contains<T>(this ReadOnlySpan<T> arr, T value) where T : Enum
    {
        foreach (var v in arr)
        {
            if (v.Equals(value))
                return true;
        }
        return false;
    }

    internal static T[] ConcatAll<T>(params T[][] arr)
    {
        int len = 0;
        foreach (var a in arr)
            len += a.Length;

        var result = new T[len];

        int ctr = 0;
        foreach (var a in arr)
        {
            a.CopyTo(result, ctr);
            ctr += a.Length;
        }

        return result;
    }

    public static IEnumerable<T[]> EnumerateSplit<T>(T[] bin, int size, int start = 0)
    {
        for (int i = start; i < bin.Length; i += size)
            yield return bin.AsSpan(i, size).ToArray();
    }
}
