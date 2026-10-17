namespace KeyLoad.Comparisons;

/// <summary>Common indexed lookup plus stable numeric predicates and complete-body oracle for every native adapter.</summary>
public static class BenchmarkSessionQueryContract
{
    private static readonly DocumentComparisonCorpus Corpus = new(BenchmarkSessionLimits.NoObservedItems, BenchmarkSessionLimits.Clients);
    /// <summary>Returns the common query's expected immutable stable record.</summary>
    public static BenchmarkDocument Expected(int ordinal, int stableStart, int stableCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentOutOfRangeException.ThrowIfNegative(stableStart);
        ArgumentOutOfRangeException.ThrowIfLessThan(stableCount, BenchmarkSessionLimits.SingleItemCount);
        return Corpus.CreateDocument(checked(stableStart + (int)(((long)ordinal * BenchmarkSessionLimits.ReadStride + BenchmarkSessionLimits.CorpusSeed) % stableCount)));
    }
    /// <summary>Checks exact caller-visible identity and complete semantic payload, including mixed-load queries.</summary>
    public static void Require(FoundDocument? actual, BenchmarkDocument expected)
    {
        if (!BenchmarkDataset.SameDocument(actual, expected))
        {
            throw new ComparisonFailureException(BenchmarkSessionProtocol.SessionNativeQueryResultMismatch);
        }
    }
}
