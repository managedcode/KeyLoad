using KeyLoad.Storage;

namespace KeyLoad.Core;

/// <summary>Admits only the exact source-to-fresh-target authority transformation before excluding its changed keys.</summary>
internal static class ClusterRestoreImageConservation
{
    private const string Invalid = "The recovered retirement conservation does not match its verified source and target mapping.";

    internal static List<byte[]> Require(ClusterRestoreImageState state, StoreIdentity sourceIdentity)
    {
        state.Work.Check();
        var policy = state.Limits.Value;
        var reconciled = state.Target.GetRecord<ClusterRestoreSlotCommit>(ClusterRestoreSlotKeys.Reconciled())
            ?? throw Errors.Fail(ErrorCode.Corruption, Invalid);
        var reset = state.Target.GetRecord<ClusterRestoreSlotCommit>(ClusterRestoreSlotKeys.AuthorityReset());
        var retained = reconciled.SourceOrigin ?? throw Errors.Fail(ErrorCode.Corruption, Invalid);
        var expected = ResourceRetirementConservationOriginImport.SourceCapture(state.Source,
            state.Local.Source, sourceIdentity, state.Original.StorePosition, state.Original.AppliedIndex,
            state.Local.Target, policy.MaxScanRecords, policy.MaxBatchBytes, state.Work.Cancellation);
        var sourceFloor = expected.SourceFloor;
        var sourceSummary = new ResourceRetirementAuthoritySummary(expected.SourceAuthorityRecordCount, expected.SourceAuthorityDigest);
        var expectedBytes = NativeSerialization.Serialize(expected);
        if (!NativeSerialization.Serialize(retained).AsSpan().SequenceEqual(expectedBytes)
            || state.Reset && (reset?.SourceOrigin is null
                || !NativeSerialization.Serialize(reset.SourceOrigin).AsSpan().SequenceEqual(expectedBytes)))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        if (!state.Reset)
        {
            var targetFloor = ResourceRetirementConservationRead.ReadFloor(state.Target, policy.MaxBatchBytes);
            if (sourceFloor is null ? targetFloor is not null
                : targetFloor is null || !ResourceRetirementConservation.Same(sourceFloor, targetFloor))
            {
                throw Errors.Fail(ErrorCode.Corruption, Invalid);
            }
            return [];
        }
        var key = ResourceRetirementConservationOriginKeys.Row(expected.SourceOwner, expected.TargetOwner);
        var found = false;
        state.Target.ReadValue(key, value => found = value.SequenceEqual(expectedBytes));
        if (!found)
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        var authority = new ResourceRetirementAuthorityAccumulator();
        authority.Add(sourceSummary);
        authority.Add(key, expectedBytes);
        var summary = authority.Complete();
        var expectedFloor = ResourceRetirementConservation.Initial(state.Local.Target) with
        {
            AuthorityRecordCount = summary.Records,
            AuthorityDigest = summary.Digest,
            BaselineOriginKey = key
        };
        var actual = ResourceRetirementConservationRead.ReadFloor(state.Target, policy.MaxBatchBytes);
        if (actual is null || !ResourceRetirementConservation.Same(expectedFloor, actual))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        state.Work.Check();
        return [ResourceRetirementConservationKeys.Floor(), key];
    }
}
