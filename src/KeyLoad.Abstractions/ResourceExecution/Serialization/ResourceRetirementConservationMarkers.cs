namespace KeyLoad;

/// <summary>Checks noncircular release markers and their exact one-for-one coverage of immutable chain steps.</summary>
internal static class ResourceRetirementConservationMarkers
{
    private const string Invalid = "The native retirement conservation release marker is invalid.";
    private const int FirstByte = 0;
    private const int ByteStep = 1;

    internal static void AppendExpected(ResourceRetirementAuthorityAccumulator coverage,
        ResourceRetirementConservationStepV1 step)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        ResourceRetirementConservation.ValidateStep(step);
        coverage.Add(ResourceRetirementConservationKeys.Release(step.Lifetime, step.PhysicalOwner), step.Digest.Span);
    }

    internal static void AppendActual(ResourceRetirementAuthorityAccumulator coverage,
        ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, int maximumRecordBytes)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        var marker = Require(key, value, maximumRecordBytes);
        coverage.Add(key, marker.After.Digest.Span);
    }

    internal static ResourceRetirementConservationReleaseV1 Require(ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> value, int maximumRecordBytes)
    {
        ResourceRetirementAuthorityClassifier.RequireBound(key, value, maximumRecordBytes);
        var marker = NativeSerialization.Deserialize<ResourceRetirementConservationReleaseV1>(value);
        var release = marker?.OwnerRelease;
        var lifetime = release?.Finalization?.Control?.Lifetime;
        var owner = release?.Receiver?.Owner;
        if (marker is null || release is null || lifetime is null || owner is null
            || !key.SequenceEqual(ResourceRetirementConservationKeys.Release(lifetime, owner)))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        var releaseKey = ResourceRetirementAuthorityKeys.Release(lifetime, owner);
        new ResourceRetirementAuthorityCandidate(releaseKey, lifetime, owner)
            .IsReleasedBy(releaseKey, NativeSerialization.Serialize(release), maximumRecordBytes);
        ResourceRetirementConservation.Validate(marker.Before);
        ResourceRetirementConservation.Validate(marker.After);
        if (!ResourceRetirementConservation.SameOwner(marker.Before.PhysicalOwner, owner)
            || !ResourceRetirementConservation.SameOwner(marker.After.PhysicalOwner, owner))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        var before = new ResourceRetirementAuthoritySummary(marker.Before.AuthorityRecordCount, marker.Before.AuthorityDigest);
        var after = new ResourceRetirementAuthoritySummary(marker.After.AuthorityRecordCount, marker.After.AuthorityDigest);
        if (after.Records < before.Records)
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        var difference = after.Digest.ToArray();
        for (var index = FirstByte; index < difference.Length; index += ByteStep)
        {
            difference[index] ^= before.Digest.Span[index];
        }
        var step = ResourceRetirementConservation.CreateStep(marker.Before, lifetime,
            release.Receipt.RetirementId, release.Receipt.CommandId, release.Receipt.AppliedPosition,
            new ResourceRetirementAuthoritySummary(after.Records - before.Records, difference));
        if (!ResourceRetirementConservation.Same(ResourceRetirementConservation.Advance(marker.Before, step), marker.After))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        return marker;
    }
}
