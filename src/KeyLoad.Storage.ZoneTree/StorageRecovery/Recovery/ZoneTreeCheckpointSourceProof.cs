namespace KeyLoad.Storage.ZoneTree;

/// <summary>Accumulates only the writer's existing stream, resolving maturity against its same gated native view.</summary>
internal sealed class ZoneTreeCheckpointSourceProof
{
    private const long Empty = 0;
    private const string Invalid = "The native source retirement authority does not match its conserved floor.";
    private static readonly byte[] StepPrefix = ResourceRetirementConservationKeys.StepPrefix();
    private static readonly byte[] MarkerPrefix = ResourceRetirementConservationKeys.ReleasePrefix();
    private readonly ZoneTreeStoreOptions options;
    private readonly ResourceRetirementConservationSourceOriginV1? admittedOrigin;
    private readonly ResourceRetirementAuthorityAccumulator authority = new();
    private readonly ZoneTreeConservationLineage lineage;
    internal ZoneTreeConservationView View
    {
        get;
    }
    internal ResourceRetirementConservationFloorV1? Floor
    {
        get;
    }

    internal ZoneTreeCheckpointSourceProof(IKeyValueView view, ZoneTreeStoreOptions options,
        TimeProvider clock, CancellationToken token, ResourceRetirementConservationSourceOriginV1? admittedOrigin = null)
    {
        this.options = options;
        this.admittedOrigin = admittedOrigin;
        View = new(view, new(options, clock, token));
        Floor = ResourceRetirementConservationRead.ReadFloor(View, options.MaxFrameBytes);
        if (admittedOrigin is not null)
        {
            ResourceRetirementConservationOriginValidation.Require(admittedOrigin);
            if ((Floor is null) != (admittedOrigin.SourceFloor is null)
                || Floor is not null && !ResourceRetirementConservation.Same(Floor, admittedOrigin.SourceFloor!))
            {
                throw Errors.Fail(ErrorCode.Corruption, Invalid);
            }
            ResourceRetirementConservationCatalog.RequireOwner(View, admittedOrigin.TargetOwner, options.MaxFrameBytes);
        }
        else if (Floor is not null)
        {
            ResourceRetirementConservationCatalog.RequireOwner(View, Floor.PhysicalOwner, options.MaxFrameBytes);
        }
        lineage = new(Floor, null, View.ReadOwnedValue, options.MaxFrameBytes);
    }

    internal ZoneTreeCheckpointMetadata Metadata(Guid incarnation, long position, long appliedPosition)
    {
        if (Floor is not null && Floor.PhysicalOwner.Incarnation != incarnation)
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        return new(ZoneTreePersistenceFormat.CheckpointVersion, KeyCodec.Version, incarnation, position, appliedPosition)
        {
            ConservationFloor = Floor,
            ConservationAuthorityRecords = Floor?.AuthorityRecordCount ?? Empty,
            ConservationAuthorityDigest = Floor?.AuthorityDigest ?? new byte[ResourceRetirementAuthoritySummary.DigestBytes]
        };
    }

    internal void Observe(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        View.Budget.Check();
        if (ResourceRetirementAuthorityClassifier.TryRead(key, value, options.MaxFrameBytes, out var candidate)
            && candidate is not null && ResourceRetirementAuthorityCapture.IsMature(View, candidate, options.MaxFrameBytes))
        {
            authority.Add(key, value);
        }
        if (key.StartsWith(StepPrefix))
        {
            lineage.Step(key, value);
        }
        if (key.StartsWith(MarkerPrefix))
        {
            lineage.Marker(key, value);
        }
    }

    internal ZoneTreeRetirementConservationProof Complete()
    {
        View.Budget.Check();
        var summary = authority.Complete();
        lineage.Complete();
        if (Floor is null ? summary.Records != Empty
            : !summary.Same(new(Floor.AuthorityRecordCount, Floor.AuthorityDigest)))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        if (admittedOrigin is not null && !summary.Same(new(admittedOrigin.SourceAuthorityRecordCount,
                admittedOrigin.SourceAuthorityDigest)))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        return new(Floor, summary.Records, summary.Digest);
    }
}
