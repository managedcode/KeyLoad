namespace KeyLoad.Storage.ZoneTree;

/// <summary>Conserves the verified original source across the existing resumable native slot commits.</summary>
internal sealed class ZoneTreeRestoreSlotConservation(ZoneTreeStoreOptions options, TimeProvider clock,
    Func<IKeyValueView, StoreIdentity, long, ClusterRestoreSlotContext, ClusterRestoreOwnerMapping>? resolveOwnerMapping)
{
    private const long Empty = 0;
    private const string Invalid = "The native restore slot conservation differs from its original admitted source.";

    internal ZoneTreeRetirementConservationProof Capture(IKeyValueView view, CancellationToken token)
        => ZoneTreeRetirementConservation.Capture(view, options, clock, token: token);

    internal void StageSource(IAtomicTransaction transaction, StoreIdentity original,
        ClusterRestoreSlotContext context, ClusterRestoreSlotCommit reconciled,
        ZoneTreeRetirementConservationProof proof, long applied, CancellationToken token)
    {
        var mapping = resolveOwnerMapping?.Invoke(transaction, original, context.SourcePosition, context);
        var origin = ZoneTreeRestoreConservation.Capture(transaction, options, original,
            context.SourcePosition, applied, proof, mapping, explicitTarget: null, token, clock);
        var row = reconciled with
        {
            SourceOrigin = origin
        };
        RequireOrigin(row, context);
        transaction.PutRecord(ClusterRestoreSlotKeys.Reconciled(), row);
    }

    internal void StageReset(IAtomicTransaction transaction, StoreIdentity identity,
        ClusterRestoreSlotContext context, ClusterRestoreSlotCommit reconciled, CancellationToken token)
    {
        Require(transaction, identity, context, reconciled, reset: false, token);
        if (reconciled.SourceOrigin is { } origin)
        {
            ResourceRetirementConservationOriginImport.StageFresh(transaction, origin, origin.TargetOwner,
                identity, options.MaximumReadCutRecords, options.MaxFrameBytes, token);
        }
    }

    internal void Require(IKeyValueView view, StoreIdentity identity, ClusterRestoreSlotContext context,
        ClusterRestoreSlotCommit reconciled, bool reset, CancellationToken token)
    {
        RequireOrigin(reconciled, context);
        var origin = reconciled.SourceOrigin;
        var sourceIdentity = identity.NodeId == context.SourceNodeId && identity.Incarnation == context.SourceIncarnation;
        var targetIdentity = identity.NodeId == context.TargetNodeId && identity.Incarnation == context.TargetIncarnation
            && identity.DispatchPaused;
        if (!sourceIdentity && !targetIdentity || reset && !targetIdentity)
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        var proof = ZoneTreeRetirementConservation.Capture(view, options, clock,
            admittedOrigin: reset ? null : origin, token: token);
        if (origin is null)
        {
            if (proof.Floor is not null || proof.AuthorityRecords != Empty)
            {
                throw Errors.Fail(ErrorCode.Corruption, Invalid);
            }
            return;
        }
        if (reset)
        {
            RequirePublishedOrigin(view, identity, origin, proof);
            return;
        }
        var sameFloor = origin.SourceFloor is null ? proof.Floor is null
            : proof.Floor is not null && ResourceRetirementConservation.Same(origin.SourceFloor, proof.Floor);
        if (!sameFloor || proof.AuthorityRecords != origin.SourceAuthorityRecordCount
            || !proof.Digest.Span.SequenceEqual(origin.SourceAuthorityDigest.Span))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
    }

    internal static void RequireSameOrigin(ClusterRestoreSlotCommit reconciled, ClusterRestoreSlotCommit reset)
    {
        var expected = reconciled.SourceOrigin;
        var actual = reset.SourceOrigin;
        if (expected is null ? actual is not null : actual is null
            || !NativeSerialization.Serialize(expected).AsSpan().SequenceEqual(NativeSerialization.Serialize(actual)))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
    }

    private static void RequireOrigin(ClusterRestoreSlotCommit row, ClusterRestoreSlotContext context)
    {
        if (row.SourceOrigin is not { } origin)
        {
            return;
        }
        ResourceRetirementConservationOriginValidation.Require(origin);
        if (origin.SourceNodeId != context.SourceNodeId || origin.SourceIncarnation != context.SourceIncarnation
            || origin.SourcePosition != context.SourcePosition || origin.SourceApplied != row.OriginalCut.AppliedIndex
            || origin.TargetOwner.Incarnation != context.TargetIncarnation
            || !ResourceRetirementConservation.SameOwner(origin.SourceOwner, row.OriginalCut.Owner))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
    }

    private static void RequirePublishedOrigin(IKeyValueView view, StoreIdentity identity,
        ResourceRetirementConservationSourceOriginV1 origin, ZoneTreeRetirementConservationProof proof)
    {
        var key = ResourceRetirementConservationOriginKeys.Row(origin.SourceOwner, origin.TargetOwner);
        var actual = view.ReadOwnedValue(key);
        if (identity.Incarnation != origin.TargetOwner.Incarnation || identity.NodeId == origin.SourceNodeId
            || proof.Floor is null || proof.Floor.Revision != Empty || proof.Floor.RetiredLifetimeCount != Empty
            || !proof.Floor.BaselineOriginKey.Span.SequenceEqual(key)
            || !ResourceRetirementConservation.SameOwner(proof.Floor.PhysicalOwner, origin.TargetOwner)
            || actual is null || !actual.AsSpan().SequenceEqual(NativeSerialization.Serialize(origin)))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
    }
}
