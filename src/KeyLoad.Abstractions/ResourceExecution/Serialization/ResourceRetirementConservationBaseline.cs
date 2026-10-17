using KeyLoad.Storage;

namespace KeyLoad;

/// <summary>Reconstructs one original chain baseline by a single exact immutable provenance lookup.</summary>
internal static class ResourceRetirementConservationBaseline
{
    private const string Missing = "The native retirement conservation baseline origin is absent or inconsistent.";

    internal static ResourceRetirementConservationFloorV1 Read(IKeyValueView view,
        ResourceRetirementConservationFloorV1 floor, int maximumRecordBytes, StorageReadObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ResourceRetirementConservation.Validate(floor);
        if (floor.BaselineOriginKey.IsEmpty)
        {
            return ResourceRetirementConservation.Initial(floor.PhysicalOwner);
        }
        ResourceRetirementConservationFloorV1? baseline = null;
        var key = floor.BaselineOriginKey.ToArray();
        view.ReadValue(key, bytes => baseline = FromOrigin(floor, key, bytes, maximumRecordBytes), observer);
        return baseline ?? throw Errors.Fail(ErrorCode.Corruption, Missing);
    }

    internal static ResourceRetirementConservationFloorV1 FromOrigin(ResourceRetirementConservationFloorV1 floor,
        ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, int maximumRecordBytes)
    {
        ResourceRetirementConservation.Validate(floor);
        ResourceRetirementAuthorityClassifier.RequireBound(key, value, maximumRecordBytes);
        if (floor.BaselineOriginKey.IsEmpty || !key.SequenceEqual(floor.BaselineOriginKey.Span))
        {
            throw Errors.Fail(ErrorCode.Corruption, Missing);
        }
        var origin = NativeSerialization.Deserialize<ResourceRetirementConservationSourceOriginV1>(value);
        ResourceRetirementConservationOriginValidation.Require(origin);
        if (!ResourceRetirementConservation.SameOwner(origin.TargetOwner, floor.PhysicalOwner)
            || !key.SequenceEqual(ResourceRetirementConservationOriginKeys.Row(origin.SourceOwner, origin.TargetOwner)))
        {
            throw Errors.Fail(ErrorCode.Corruption, Missing);
        }
        var authority = new ResourceRetirementAuthorityAccumulator();
        authority.Add(new ResourceRetirementAuthoritySummary(origin.SourceAuthorityRecordCount, origin.SourceAuthorityDigest));
        authority.Add(key, value);
        var summary = authority.Complete();
        return ResourceRetirementConservation.Initial(floor.PhysicalOwner) with
        {
            AuthorityRecordCount = summary.Records,
            AuthorityDigest = summary.Digest,
            BaselineOriginKey = key.ToArray()
        };
    }
}
