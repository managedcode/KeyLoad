namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Checks only the fresh bounded query wrapper; it never admits an original effect.</summary>
internal static class PartitionMovementOutcomeAdmission
{
    private const int MinimumCount = 1;
    private const int FirstOrdinal = 0;
    private const string InvalidProof = "The partition movement outcome query proof is invalid.";

    internal static void Require(PartitionMovementOutcomeTransportRequest query, DatabaseLimits limits,
        DateTimeOffset now, TimeSpan maximumLifetime)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Original is null || query.Original.Envelope is null
            || query.Original.CommandId == Guid.Empty || query.Nonce == Guid.Empty
            || query.ExpiresAt <= now || query.ExpiresAt > now + maximumLifetime
            || query.Original.HandleId != Guid.Empty || query.Original.Ordinal != FirstOrdinal
            || query.Original.Action != PartitionMovementTransportAction.Apply
            || query.MaximumReadBytes < MinimumCount
            || query.MaximumReadBytes > Math.Min(limits.MaxBatchBytes, limits.MaxQueryReadBytes)
            || query.MaximumExaminedRecords < MinimumCount
            || query.MaximumExaminedRecords > Math.Min(limits.MaxBatchMutations, limits.MaxScanRecords)
            || query.MaximumResultBytes < MinimumCount || query.MaximumResultBytes > limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.Unauthenticated, InvalidProof); }
    }
}
