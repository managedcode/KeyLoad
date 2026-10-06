namespace KeyLoad.Query.Features.Search;

internal static class PackedAnnLevels
{
    private const int AdjacentElementOffset = 1;
    private const int EmptyElementCount = 0;
    private const int SingleWorkUnit = 1;
    private const int BudgetCheckRemainder = 0;
    private const int FirstMixShift = 30;
    private const ulong FirstMixMultiplier = 0xBF58476D1CE4E5B9UL;
    private const int SecondMixShift = 27;
    private const ulong SecondMixMultiplier = 0x94D049BB133111EBUL;
    private const int FinalMixShift = 31;

    private const ulong SplitMixIncrement = 0x9E3779B97F4A7C15UL;

    internal static int For(ulong seed, int ordinal, int connections, int maximum, AnnWorkBudget budget)
    {
        var state = unchecked(seed + ((ulong)ordinal + AdjacentElementOffset) * SplitMixIncrement);
        var level = EmptyElementCount;
        while (level < maximum)
        {
            budget.Charge(SingleWorkUnit);
            state += SplitMixIncrement;
            if (Mix(state) % (ulong)connections != BudgetCheckRemainder)
            {
                break;
            }
            level++;
        }
        return level;
    }

    private static ulong Mix(ulong value)
    {
        value = (value ^ (value >> FirstMixShift)) * FirstMixMultiplier;
        value = (value ^ (value >> SecondMixShift)) * SecondMixMultiplier;
        return value ^ (value >> FinalMixShift);
    }
}
