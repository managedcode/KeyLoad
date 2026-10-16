using KeyLoad.Storage;

namespace KeyLoad;

/// <summary>Recognizes only immutable rows matured by the exact actual owner-release record.</summary>
internal static class ResourceRetirementAuthorityClassifier
{
    private const string Invalid = "The native retirement authority row has an invalid key or identity.";
    private const string Oversized = "The native retirement authority row exceeds its admitted byte limit.";
    private static readonly byte[] Fence = KeyCodec.Encode(ResourceRetirementAuthorityKeys.FenceSpace);
    private static readonly byte[] Finalization = KeyCodec.Encode(ResourceRetirementAuthorityKeys.FinalizationSpace);
    private static readonly byte[] Cleanup = KeyCodec.Encode(ResourceRetirementAuthorityKeys.CleanupSpace);
    private static readonly byte[] Release = KeyCodec.Encode(ResourceRetirementAuthorityKeys.ReleaseSpace);
    private static readonly byte[] Origin = ResourceRetirementConservationOriginKeys.Prefix();

    internal static IReadOnlyList<byte[]> Prefixes =>
        [Fence.ToArray(), Finalization.ToArray(), Cleanup.ToArray(), Release.ToArray(), Origin.ToArray()];

    internal static bool TryRead(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, int maximumRecordBytes,
        out ResourceRetirementAuthorityCandidate? candidate)
    {
        candidate = null;
        if (key.StartsWith(Fence))
        {
            candidate = ReadFence(key, value, maximumRecordBytes);
            return true;
        }
        if (key.StartsWith(Finalization))
        {
            candidate = ReadFinalization(key, value, maximumRecordBytes);
            return true;
        }
        if (key.StartsWith(Cleanup))
        {
            candidate = ReadCleanup(key, value, maximumRecordBytes);
            return true;
        }
        if (key.StartsWith(Release))
        {
            candidate = ReadRelease(key, value, maximumRecordBytes);
            return true;
        }
        if (key.StartsWith(Origin))
        {
            candidate = ReadOrigin(key, value, maximumRecordBytes);
            return true;
        }
        return false;
    }

    private static ResourceRetirementAuthorityCandidate ReadFence(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value,
        int maximumRecordBytes)
    {
        RequireBound(key, value, maximumRecordBytes);
        var row = NativeSerialization.Deserialize<ResourceRetirementOwnerFenceV1>(value);
        var lifetime = row?.Control?.Lifetime ?? throw Errors.Fail(ErrorCode.Corruption, Invalid);
        var owner = row.Receipt?.PhysicalOwner ?? throw Errors.Fail(ErrorCode.Corruption, Invalid);
        return Complete(key, lifetime, owner,
            ResourceRetirementAuthorityKeys.Scope(ResourceRetirementAuthorityKeys.FenceSpace, lifetime));
    }

    private static ResourceRetirementAuthorityCandidate ReadFinalization(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value,
        int maximumRecordBytes)
    {
        RequireBound(key, value, maximumRecordBytes);
        var row = NativeSerialization.Deserialize<ResourceRetirementFinalizationV1>(value);
        var lifetime = row?.Control?.Lifetime ?? throw Errors.Fail(ErrorCode.Corruption, Invalid);
        var owner = row.Receipt?.PhysicalOwner ?? throw Errors.Fail(ErrorCode.Corruption, Invalid);
        return Complete(key, lifetime, owner,
            ResourceRetirementAuthorityKeys.Scope(ResourceRetirementAuthorityKeys.FinalizationSpace, lifetime));
    }

    private static ResourceRetirementAuthorityCandidate ReadCleanup(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value,
        int maximumRecordBytes)
    {
        RequireBound(key, value, maximumRecordBytes);
        var row = NativeSerialization.Deserialize<ResourceRetirementOwnerCleanupV1>(value);
        var lifetime = row?.Control?.Lifetime ?? throw Errors.Fail(ErrorCode.Corruption, Invalid);
        var owner = row.Receiver?.Owner ?? throw Errors.Fail(ErrorCode.Corruption, Invalid);
        return Complete(key, lifetime, owner,
            ResourceRetirementAuthorityKeys.Owner(ResourceRetirementAuthorityKeys.CleanupSpace, lifetime, owner));
    }

    private static ResourceRetirementAuthorityCandidate ReadRelease(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value,
        int maximumRecordBytes)
    {
        RequireBound(key, value, maximumRecordBytes);
        var row = NativeSerialization.Deserialize<ResourceRetirementOwnerReleaseV1>(value);
        var lifetime = row?.Finalization?.Control?.Lifetime ?? throw Errors.Fail(ErrorCode.Corruption, Invalid);
        var owner = row.Receiver?.Owner ?? throw Errors.Fail(ErrorCode.Corruption, Invalid);
        return Complete(key, lifetime, owner, ResourceRetirementAuthorityKeys.Release(lifetime, owner));
    }

    private static ResourceRetirementAuthorityCandidate ReadOrigin(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value,
        int maximumRecordBytes)
    {
        RequireBound(key, value, maximumRecordBytes);
        var origin = NativeSerialization.Deserialize<ResourceRetirementConservationSourceOriginV1>(value);
        ResourceRetirementConservationOriginValidation.Require(origin);
        if (!key.SequenceEqual(ResourceRetirementConservationOriginKeys.Row(origin.SourceOwner, origin.TargetOwner)))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        return new(null, null, null);
    }

    private static ResourceRetirementAuthorityCandidate Complete(ReadOnlySpan<byte> key, ResourceLifetimeV1 lifetime,
        PhysicalShardRecord owner, byte[] expected)
    {
        if (lifetime.Incarnation is null || !key.SequenceEqual(expected))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        ResourceRetirementConservation.ValidateOwner(owner);
        return new(ResourceRetirementAuthorityKeys.Release(lifetime, owner), lifetime, owner);
    }

    internal static void RequireBound(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, int maximumRecordBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRecordBytes);
        if (key.Length > maximumRecordBytes || value.Length > maximumRecordBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, Oversized);
        }
    }
}
