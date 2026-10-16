using KeyLoad.Storage;

namespace KeyLoad;

/// <summary>Stages conserved provenance only inside the existing genuinely fresh offline restore transaction.</summary>
internal static class ResourceRetirementConservationOriginImport
{
    private const int Version = 1;
    private const long Empty = 0;
    private const string Invalid = "The fresh native retirement source origin does not match its verified image.";
    private const string Capacity = "The fresh native retirement source origin exceeds its admitted bounds.";

    internal static ResourceRetirementConservationSourceOriginV1 SourceCapture(IKeyValueView view,
        PhysicalShardRecord sourceOwner, StoreIdentity sourceIdentity, long sourcePosition, long sourceApplied,
        PhysicalShardRecord targetOwner, int maximumRecords, int maximumRecordBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceIdentity);
        cancellationToken.ThrowIfCancellationRequested();
        var floor = ResourceRetirementConservationRead.ReadFloor(view, maximumRecordBytes);
        var summary = ResourceRetirementAuthorityCapture.Capture(view, maximumRecords,
            maximumRecordBytes, cancellationToken);
        var origin = new ResourceRetirementConservationSourceOriginV1(Version, sourceOwner,
            sourceIdentity.NodeId, sourceIdentity.Incarnation, sourcePosition, sourceApplied, floor,
            summary.Records, summary.Digest.ToArray(), targetOwner);
        ResourceRetirementConservationOriginValidation.Require(origin);
        RequireBound(origin, maximumRecordBytes);
        return origin;
    }

    internal static void StageFresh(IAtomicTransaction transaction,
        ResourceRetirementConservationSourceOriginV1 origin, PhysicalShardRecord targetOwner,
        StoreIdentity targetIdentity, int maximumRecords, int maximumRecordBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(targetIdentity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRecords);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRecordBytes);
        ResourceRetirementConservationOriginValidation.Require(origin);
        RequireBound(origin, maximumRecordBytes);
        if (!ResourceRetirementConservation.SameOwner(origin.TargetOwner, targetOwner)
            || targetIdentity.Incarnation != targetOwner.Incarnation
            || targetIdentity.NodeId == Guid.Empty || targetIdentity.NodeId == origin.SourceNodeId)
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        var currentFloor = ResourceRetirementConservationRead.ReadFloor(transaction, maximumRecordBytes);
        var sourceSummary = ResourceRetirementConservationOriginValidation.Summary(origin);
        var currentSummary = ResourceRetirementAuthorityCapture.Capture(transaction, maximumRecords,
            maximumRecordBytes, cancellationToken);
        if (!SameFloor(origin.SourceFloor, currentFloor) || !sourceSummary.Same(currentSummary))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        var key = ResourceRetirementConservationOriginKeys.Row(origin.SourceOwner, targetOwner);
        if (transaction.ReadValue(key, static _ => { }))
        {
            throw Errors.Fail(ErrorCode.Conflict, Invalid);
        }
        var bytes = NativeSerialization.Serialize(origin);
        var accumulator = new ResourceRetirementAuthorityAccumulator();
        accumulator.Add(sourceSummary);
        accumulator.Add(key, bytes);
        var summary = accumulator.Complete();
        if (summary.Records > maximumRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, Capacity);
        }
        var targetFloor = new ResourceRetirementConservationFloorV1(Version, targetOwner, Empty, Empty,
            new byte[ResourceRetirementAuthoritySummary.DigestBytes])
        {
            AuthorityRecordCount = summary.Records,
            AuthorityDigest = summary.Digest,
            BaselineOriginKey = key
        };
        ResourceRetirementConservation.Validate(targetFloor);
        var floorBytes = NativeSerialization.Serialize(targetFloor);
        if (floorBytes.Length > maximumRecordBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, Capacity);
        }
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Put(key, bytes);
        transaction.Put(ResourceRetirementConservationKeys.Floor(), floorBytes);
    }

    private static bool SameFloor(ResourceRetirementConservationFloorV1? expected,
        ResourceRetirementConservationFloorV1? actual)
        => expected is null ? actual is null : actual is not null && ResourceRetirementConservation.Same(expected, actual);

    private static void RequireBound(ResourceRetirementConservationSourceOriginV1 origin, int maximumRecordBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRecordBytes);
        if (NativeSerialization.Measure(origin) > maximumRecordBytes
            || ResourceRetirementConservationOriginKeys.Row(origin.SourceOwner, origin.TargetOwner).Length > maximumRecordBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, Capacity);
        }
    }
}
