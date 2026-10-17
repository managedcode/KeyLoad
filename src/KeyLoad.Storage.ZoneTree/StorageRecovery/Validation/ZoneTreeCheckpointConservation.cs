namespace KeyLoad.Storage.ZoneTree;

/// <summary>Reconciles actual immutable image rows with native release witnesses and the complete step chain.</summary>
internal static class ZoneTreeCheckpointConservation
{
    private const long Empty = 0;
    private const string Invalid = "The native checkpoint does not conserve its original retirement authority.";

    internal static void Require(FileStream input, ZoneTreeStoreOptions options,
        ZoneTreeCheckpointAuthorityIndex index, ZoneTreeConservationBudget budget,
        ResourceRetirementConservationFloorV1? requiredFloor)
    {
        index.RequireFloor(options.MaxFrameBytes);
        var metadata = index.Metadata!;
        var summary = Capture(input, options, index, budget);
        if (!summary.Same(new(metadata.ConservationAuthorityRecords, metadata.ConservationAuthorityDigest)))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        var floor = metadata.ConservationFloor;
        if (floor is null)
        {
            if (requiredFloor is not null || summary.Records != Empty
                || index.Offset(ResourceRetirementConservationKeys.StepPrefix()) >= Empty
                || index.Offset(ResourceRetirementConservationKeys.ReleasePrefix()) >= Empty)
            {
                throw Errors.Fail(ErrorCode.Corruption, Invalid);
            }
            return;
        }
        if (!summary.Same(new(floor.AuthorityRecordCount, floor.AuthorityDigest)))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        RequireChain(input, options, index, budget, floor, requiredFloor);
    }

    private static ResourceRetirementAuthoritySummary Capture(FileStream input, ZoneTreeStoreOptions options,
        ZoneTreeCheckpointAuthorityIndex index, ZoneTreeConservationBudget budget)
    {
        var accumulator = new ResourceRetirementAuthorityAccumulator();
        var releasePrefix = ResourceRetirementAuthorityKeys.ReleasePrefix();
        foreach (var prefix in ResourceRetirementAuthorityClassifier.Prefixes)
        {
            var rows = Cursor(input, index, budget, prefix);
            var releases = Cursor(input, index, budget, releasePrefix);
            var hasRelease = releases.MoveNext();
            var previousRelease = ReadOnlyMemory<byte>.Empty;
            while (rows.MoveNext())
            {
                var row = rows.Current!;
                if (!ResourceRetirementAuthorityClassifier.TryRead(row.Key.Span, row.Value!.Value.Span,
                        options.MaxFrameBytes, out var candidate) || candidate is null)
                {
                    throw Errors.Fail(ErrorCode.Corruption, Invalid);
                }
                if (candidate.ReleaseKey is not { } releaseKey)
                {
                    accumulator.Add(row.Key.Span, row.Value!.Value.Span);
                    continue;
                }
                if (!previousRelease.IsEmpty && releaseKey.AsSpan().SequenceCompareTo(previousRelease.Span) < Empty)
                {
                    throw Errors.Fail(ErrorCode.Corruption, Invalid);
                }
                previousRelease = releaseKey;
                while (hasRelease && releases.Current!.Key.Span.SequenceCompareTo(releaseKey) < Empty)
                {
                    hasRelease = releases.MoveNext();
                }
                if (hasRelease && candidate.IsReleasedBy(releases.Current!.Key.Span,
                        releases.Current!.Value!.Value.Span, options.MaxFrameBytes))
                {
                    accumulator.Add(row.Key.Span, row.Value!.Value.Span);
                }
            }
        }
        return accumulator.Complete();
    }

    private static void RequireChain(FileStream input, ZoneTreeStoreOptions options,
        ZoneTreeCheckpointAuthorityIndex index, ZoneTreeConservationBudget budget,
        ResourceRetirementConservationFloorV1 floor, ResourceRetirementConservationFloorV1? required)
    {
        byte[]? ReadOrigin(byte[] key)
        {
            var origins = Cursor(input, index, budget, ResourceRetirementConservationOriginKeys.Prefix());
            while (origins.MoveNext())
            {
                var row = origins.Current!;
                var comparison = row.Key.Span.SequenceCompareTo(key);
                if (comparison == Empty)
                {
                    return row.Value!.Value.ToArray();
                }
                if (comparison > Empty)
                {
                    break;
                }
            }
            return null;
        }
        var lineage = new ZoneTreeConservationLineage(floor, required, ReadOrigin, options.MaxFrameBytes);
        var steps = Cursor(input, index, budget, ResourceRetirementConservationKeys.StepPrefix());
        while (steps.MoveNext())
        {
            lineage.Step(steps.Current!.Key.Span, steps.Current!.Value!.Value.Span);
        }
        var markers = Cursor(input, index, budget, ResourceRetirementConservationKeys.ReleasePrefix());
        while (markers.MoveNext())
        {
            lineage.Marker(markers.Current!.Key.Span, markers.Current!.Value!.Value.Span);
        }
        lineage.Complete();
    }

    private static ZoneTreeCheckpointFamilyCursor Cursor(FileStream input, ZoneTreeCheckpointAuthorityIndex index,
        ZoneTreeConservationBudget budget, byte[] prefix) => new(input, budget, prefix, index.Offset(prefix));
}
