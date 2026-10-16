using KeyLoad.Storage;

namespace KeyLoad;

/// <summary>Resolves retained owner authority by exact baseline links within the original caller's work ledger.</summary>
internal static class ResourceRetirementConservationProvenance
{
    private const int InitialDepth = 0;
    private const int DepthStep = 1;
    private const string Unknown = "The original retirement owner is absent from the conserved restore lineage.";
    private const string Invalid = "The native retirement restore provenance link is invalid or cyclic.";
    private const string Exhausted = "The native retirement restore provenance exceeds its admitted depth.";

    internal static ResourceRetirementConservationFloorV1 Resolve(IKeyValueView view,
        ResourceRetirementConservationFloorV1 currentFloor, PhysicalShardRecord requiredOwner,
        int maximumRecordBytes, int maximumOrigins, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRecordBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumOrigins);
        ResourceRetirementConservation.ValidateOwner(requiredOwner);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var floor = currentFloor;
        var depth = InitialDepth;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResourceRetirementConservation.Validate(floor);
            if (ResourceRetirementConservation.SameOwner(floor.PhysicalOwner, requiredOwner))
            {
                return floor;
            }
            if (floor.BaselineOriginKey.IsEmpty)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, Unknown);
            }
            if (depth >= maximumOrigins)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, Exhausted);
            }
            if (floor.BaselineOriginKey.Length > maximumRecordBytes
                || !seen.Add(Convert.ToBase64String(floor.BaselineOriginKey.Span)))
            {
                throw Errors.Fail(ErrorCode.Corruption, Invalid);
            }
            depth += DepthStep;
            var key = floor.BaselineOriginKey.ToArray();
            ResourceRetirementConservationSourceOriginV1? origin = null;
            view.ReadValue(key, value =>
            {
                ResourceRetirementAuthorityClassifier.RequireBound(key, value, maximumRecordBytes);
                origin = NativeSerialization.Deserialize<ResourceRetirementConservationSourceOriginV1>(value);
                ResourceRetirementConservationOriginValidation.Require(origin);
                if (!ResourceRetirementConservation.SameOwner(origin.TargetOwner, floor.PhysicalOwner)
                    || !key.AsSpan().SequenceEqual(ResourceRetirementConservationOriginKeys.Row(origin.SourceOwner, origin.TargetOwner)))
                {
                    throw Errors.Fail(ErrorCode.Corruption, Invalid);
                }
                // Validates the exact original baseline bytes without a refreshed budget or registry traversal.
                _ = ResourceRetirementConservationBaseline.FromOrigin(floor, key, value, maximumRecordBytes);
            }, observer);
            if (origin?.SourceFloor is not { } source
                || !ResourceRetirementConservation.SameOwner(source.PhysicalOwner, origin.SourceOwner))
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, Unknown);
            }
            floor = source;
        }
    }
}
