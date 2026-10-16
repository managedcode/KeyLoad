namespace KeyLoad.Storage.ZoneTree;

/// <summary>Only fixed family offsets and one actual native floor; no pending authority collection.</summary>
internal sealed class ZoneTreeCheckpointAuthorityIndex
{
    private const long Missing = -1;
    private const int First = 0;
    private const int Next = 1;
    private const string Invalid = "The native checkpoint conservation floor does not match its header.";
    private static readonly byte[] FloorKey = ResourceRetirementConservationKeys.Floor();
    private static readonly byte[] CatalogKey = ResourceRetirementConservationCatalog.Key();
    private byte[]? actualCatalog;
    private readonly byte[][] prefixes = ResourceRetirementAuthorityClassifier.Prefixes
        .Append(ResourceRetirementConservationKeys.StepPrefix())
        .Append(ResourceRetirementConservationKeys.ReleasePrefix()).ToArray();
    private readonly long[] offsets;
    internal ZoneTreeCheckpointMetadata? Metadata
    {
        get; set;
    }
    private ResourceRetirementConservationFloorV1? actualFloor;

    internal ZoneTreeCheckpointAuthorityIndex()
    {
        offsets = Enumerable.Repeat(Missing, prefixes.Length).ToArray();
    }

    internal long Offset(ReadOnlySpan<byte> prefix)
    {
        for (var index = First; index < prefixes.Length; index += Next)
        {
            if (prefix.SequenceEqual(prefixes[index]))
            {
                return offsets[index];
            }
        }
        return Missing;
    }

    internal void Observe(StorageMutation mutation, long frameOffset)
    {
        for (var index = First; index < prefixes.Length; index += Next)
        {
            if (offsets[index] == Missing && mutation.Key.Span.StartsWith(prefixes[index]))
            {
                offsets[index] = frameOffset;
            }
        }
        if (mutation.Key.Span.SequenceEqual(FloorKey))
        {
            actualFloor = NativeSerialization.Deserialize<ResourceRetirementConservationFloorV1>(mutation.Value!.Value.Span);
            ResourceRetirementConservation.Validate(actualFloor);
        }
        if (mutation.Key.Span.SequenceEqual(CatalogKey))
        {
            actualCatalog = mutation.Value!.Value.ToArray();
        }
    }

    internal void RequireFloor(int maximumBytes)
    {
        var declared = Metadata!.ConservationFloor;
        if ((declared is null) != (actualFloor is null)
            || declared is not null && !ResourceRetirementConservation.Same(declared, actualFloor!))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        if (declared is not null)
        {
            ResourceRetirementConservationCatalog.RequireOwner(actualCatalog ?? throw Errors.Fail(ErrorCode.Corruption, Invalid),
                declared.PhysicalOwner, maximumBytes);
        }
    }
}
