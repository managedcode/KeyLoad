using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Retains verified original-source conservation across a genuinely fresh identity rewrite.</summary>
internal static class ZoneTreeRestoreConservation
{
    private const long Empty = 0;
    private const string MissingMapping = "A conserved archive requires an explicit original-to-target physical owner mapping.";
    private const string ChangedSource = "The verified original archive conservation changed during reconciliation.";

    internal static long ReadApplied(IKeyValueView view)
    {
        var bytes = view.ReadOwnedValue(KeyCodec.Encode(SystemNamespace, LastAppliedKey));
        var applied = bytes is null ? Empty : NativeSerialization.Deserialize<long>(bytes);
        if (applied < Empty)
        {
            throw Errors.Fail(ErrorCode.Corruption, ChangedSource);
        }
        return applied;
    }

    internal static ResourceRetirementConservationSourceOriginV1? Capture(IKeyValueView view,
        ZoneTreeStoreOptions options, StoreIdentity identity, long position, long applied,
        ZoneTreeRetirementConservationProof proof, ClusterRestoreOwnerMapping? mapping,
        PhysicalShardRecord? explicitTarget, CancellationToken cancellationToken, TimeProvider? clock = null)
    {
        if (proof.Floor is null && proof.AuthorityRecords == Empty && mapping is null && explicitTarget is null)
        {
            var current = ZoneTreeRetirementConservation.Capture(view, options, clock ?? TimeProvider.System, token: cancellationToken);
            if (current.Floor is not null || current.AuthorityRecords != Empty)
            {
                throw Errors.Fail(ErrorCode.Corruption, ChangedSource);
            }
            return null;
        }
        var source = mapping?.Source ?? proof.Floor?.PhysicalOwner;
        var target = mapping?.Target ?? explicitTarget;
        if (source is null || target is null
            || mapping is not null && mapping.Version != ClusterRestoreOwnerMapping.CurrentVersion)
        {
            throw Errors.Fail(ErrorCode.Validation, MissingMapping);
        }
        var origin = ResourceRetirementConservationOriginImport.SourceCapture(view, source, identity,
            position, applied, target, options.MaximumReadCutRecords, options.MaximumRangeRecords, options.MaxFrameBytes, cancellationToken);
        var sameFloor = proof.Floor is null ? origin.SourceFloor is null
            : origin.SourceFloor is not null && ResourceRetirementConservation.Same(proof.Floor, origin.SourceFloor);
        if (!sameFloor || origin.SourceAuthorityRecordCount != proof.AuthorityRecords
            || !origin.SourceAuthorityDigest.Span.SequenceEqual(proof.Digest.Span))
        {
            throw Errors.Fail(ErrorCode.Corruption, ChangedSource);
        }
        return origin;
    }
}
