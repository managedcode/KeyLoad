namespace KeyLoad;

/// <summary>One bounded actual image row awaiting an exact same-lifetime native release witness.</summary>
internal sealed record ResourceRetirementAuthorityCandidate(
    byte[]? ReleaseKey, ResourceLifetimeV1? Lifetime, PhysicalShardRecord? PhysicalOwner)
{
    private const string Invalid = "The native retirement authority release does not match its original row.";

    internal bool IsReleasedBy(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, int maximumRecordBytes)
    {
        if (ReleaseKey is null)
        {
            return true;
        }
        if (!key.SequenceEqual(ReleaseKey))
        {
            return false;
        }
        ResourceRetirementAuthorityClassifier.RequireBound(key, value, maximumRecordBytes);
        var release = NativeSerialization.Deserialize<ResourceRetirementOwnerReleaseV1>(value);
        if (release?.Finalization?.Control?.Lifetime is not { } lifetime || release.Receiver?.Owner is not { } owner
            || Lifetime is null || PhysicalOwner is null || lifetime != Lifetime
            || !ResourceRetirementConservation.SameOwner(owner, PhysicalOwner)
            || release.Receipt is null || release.Receipt.Phase != ResourceRetirementPhase.ReleaseOwnerTopology
            || release.Receipt.Lifetime != lifetime || !ResourceRetirementConservation.SameOwner(release.Receipt.PhysicalOwner, owner)
            || release.Finalization.Receipt is null
            || release.Finalization.Receipt.Phase != ResourceRetirementPhase.FinalizeCatalog)
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        return true;
    }
}
