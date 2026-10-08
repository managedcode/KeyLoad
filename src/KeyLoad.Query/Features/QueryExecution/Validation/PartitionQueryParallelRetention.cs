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
        var available = checked(original.MaxRetainedBytes - requestBytes - MetadataFrames * FrameBytes);
        var retained = available / RetentionFrames;
        if (retained < checked(PartitionQueryRetention.LeafHeapReserve(original.MaxCandidates)
            + PartitionQueryRetention.CandidateArrayBytes(original.MaxCandidates)))
        { throw Errors.Fail(ErrorCode.BudgetExceeded, Bound); }
        return original with { MaxRetainedBytes = retained };
    }

    internal static long ReplyBytes(PartitionQueryLeafPlanV1 child)
        => checked(ReplyFrames * child.MaxRetainedBytes + FrameBytes);
}
