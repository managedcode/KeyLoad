namespace KeyLoad.Query.Features.Search;

internal static class PackedAnnLevels
{
    private const ulong SplitMixIncrement = 0x9E3779B97F4A7C15UL;

    internal static int For(ulong seed, int ordinal, int connections, int maximum, AnnWorkBudget budget)
    {
        var state = unchecked(seed + ((ulong)ordinal + 1) * SplitMixIncrement);
        var level = 0;
        while (level < maximum)
        {
            budget.Charge(1);
            state += SplitMixIncrement;
            if (Mix(state) % (ulong)connections != 0)
            {
                break;
            }
            level++;
        }
        return level;
    }

    private static ulong Mix(ulong value)
    {
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
