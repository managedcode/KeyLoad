namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Derives native transport and task retention downward inside each original leaf allocation.</summary>
internal static class PartitionQueryParallelRetention
{
    private const int RetentionFrames = 3;
    private const int ReplyFrames = 2;
    private const int MetadataFrames = 2;
    private const long FrameBytes = 32_768;
    private const string Bound = "The partition query plan exceeds its resource budget.";

    internal static PartitionQueryLeafPlanV1 Child(PartitionQueryLeafPlanV1 original, long requestBytes)
    {
        var retained = RetainedBytes(original.MaxRetainedBytes, requestBytes);
        if (retained < checked(PartitionQueryRetention.LeafHeapReserve(original.MaxCandidates)
            + PartitionQueryRetention.CandidateArrayBytes(original.MaxCandidates)))
        { throw Errors.Fail(ErrorCode.BudgetExceeded, Bound); }
        return original with { MaxRetainedBytes = retained };
    }

    internal static long ReplyBytes(PartitionQueryLeafPlanV1 child)
        => ReplyBytes(child.MaxRetainedBytes);

    internal static long ChildBytes(long originalBytes, long requestBytes, long minimumRetainedBytes)
    {
        var retained = RetainedBytes(originalBytes, requestBytes);
        if (retained < minimumRetainedBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, Bound); }
        return retained;
    }

    internal static long ReplyBytes(long retainedBytes)
        => checked(ReplyFrames * retainedBytes + FrameBytes);

    private static long RetainedBytes(long originalBytes, long requestBytes)
    {
        var available = checked(originalBytes - requestBytes - MetadataFrames * FrameBytes);
        return available / RetentionFrames;
    }
}
