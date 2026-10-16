using KeyLoad.Storage;

namespace KeyLoad;

/// <summary>Bounded source-view capture for a verified fresh offline archive; live snapshots use their existing traversal.</summary>
internal static class ResourceRetirementAuthorityCapture
{
    private const int NoRecords = 0;
    private const int RecordStep = 1;
    private const string Exhausted = "The native retirement authority capture exceeds its admitted record limit.";
    private const string Changed = "The actual native retirement authority rows do not match their conserved floor.";

    internal static ResourceRetirementAuthoritySummary Capture(IKeyValueView view, int maximumRecords,
        int maximumRecordBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRecords);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRecordBytes);
        var accumulator = new ResourceRetirementAuthorityAccumulator();
        var examined = NoRecords;
        foreach (var prefix in ResourceRetirementAuthorityClassifier.Prefixes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = view.VisitRange(prefix, maximumRecords, (key, value) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (examined >= maximumRecords)
                {
                    throw Errors.Fail(ErrorCode.BudgetExceeded, Exhausted);
                }
                examined += RecordStep;
                if (ResourceRetirementAuthorityClassifier.TryRead(key, value, maximumRecordBytes, out var candidate)
                    && candidate is not null && IsMature(view, candidate, maximumRecordBytes))
                {
                    accumulator.Add(key, value);
                }
                return true;
            }, cancellationToken: cancellationToken);
            if (page.HasMore)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, Exhausted);
            }
        }
        return accumulator.Complete();
    }

    internal static void RequireMatches(ResourceRetirementConservationFloorV1? floor,
        ResourceRetirementAuthoritySummary summary)
    {
        summary.RequireValid();
        if (floor is null)
        {
            if (summary.Records != NoRecords)
            {
                throw Errors.Fail(ErrorCode.Corruption, Changed);
            }
            return;
        }
        ResourceRetirementConservation.Validate(floor);
        if (!new ResourceRetirementAuthoritySummary(floor.AuthorityRecordCount, floor.AuthorityDigest).Same(summary))
        {
            throw Errors.Fail(ErrorCode.Corruption, Changed);
        }
    }

    internal static bool IsMature(IKeyValueView view, ResourceRetirementAuthorityCandidate candidate,
        int maximumRecordBytes)
    {
        if (candidate.ReleaseKey is not { } releaseKey)
        {
            return true;
        }
        var mature = false;
        view.ReadValue(releaseKey, bytes => mature = candidate.IsReleasedBy(releaseKey, bytes, maximumRecordBytes));
        return mature;
    }
}
