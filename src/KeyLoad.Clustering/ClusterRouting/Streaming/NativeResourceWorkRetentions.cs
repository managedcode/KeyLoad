using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Retains only outstanding retirement observations within the existing frame capacity and owner gate.</summary>
internal sealed class NativeResourceWorkRetentions(IOptions<GrainRoutingOptions> options)
{
    private readonly GrainRoutingOptions settings = options.Value;
    private readonly Dictionary<(Guid RetirementId, Guid CommandId), NativeResourceWorkClosure> qualified = [];

    internal NativeResourceWorkClosure[] Outstanding => qualified.Values.ToArray();

    internal void RequireAdmissionSlot(int existingFrames)
    {
        if (existingFrames + qualified.Count >= settings.MaximumTotalFrames)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, NativeRequestWorkLimits.CapacityMessage);
        }
    }

    internal IResourceLifetimeWorkSettlement CloseMatching(NativeRequestWorkOwner owner,
        ResourceRetirementOwnerFenceV1 fence, ImmutableArray<byte> fingerprint, PhysicalShardRecord actualOwner, NativeRequestWorkAdmissions admissions, bool canQualify, bool retainCancellation)
    {
        var key = (fence.Receipt.RetirementId, fence.Receipt.CommandId);
        if (qualified.TryGetValue(key, out var original))
        {
            RequireSameFence(original.Fence, fence);
            return original;
        }
        // Closing is unconditional even when there is no slot for a qualified settlement observation.
        var originals = admissions.CloseMatching(fence, fingerprint);
        if (!canQualify)
        {
            return new NativeResourceWorkRecoveryRefusal();
        }
        if (admissions.Count + qualified.Count >= settings.MaximumTotalFrames)
        {
            return new NativeResourceWorkCapacityRefusal();
        }
        var leases = originals.Select(lease => new NativeResourceWorkClosingLease(lease, lease.ClosePublication(retainCancellation))).ToImmutableArray();
        var retained = new NativeResourceWorkClosure(owner, fence, fingerprint, actualOwner, leases);
        qualified.Add(key, retained);
        return retained;
    }

    internal NativeResourceWorkClosure? Find(ResourceRetirementOwnerFenceV1 fence)
    {
        var key = (fence.Receipt.RetirementId, fence.Receipt.CommandId);
        if (!qualified.TryGetValue(key, out var original))
        {
            return null;
        }
        RequireSameFence(original.Fence, fence);
        return original;
    }

    internal void Consume(ResourceRetirementOwnerFenceV1 fence, ImmutableArray<byte> fingerprint, NativeResourceWorkClosure originalHandle,
        NativeRequestWorkAdmissions admissions)
    {
        var key = (fence.Receipt.RetirementId, fence.Receipt.CommandId);
        if (!qualified.TryGetValue(key, out var original) || !ReferenceEquals(original, originalHandle)
            || !original.HasCompletedOriginalJoin || original.TerminalReport is null)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
        RequireSameFence(original.Fence, fence);
        foreach (var item in original.OriginalLeases)
        {
            _ = item.Lease.RetirementSnapshot(fence, fingerprint);
        }
        foreach (var item in original.OriginalLeases)
        {
            admissions.ConsumeRetirement(item.Lease, fence, fingerprint);
        }
        qualified.Remove(key);
    }

    internal static ImmutableArray<byte> Fingerprint(ResourceRetirementOwnerFenceV1 fence)
        => [.. SHA256.HashData(NativeSerialization.Serialize(fence))];

    internal static void RequireFingerprint(ImmutableArray<byte> original, ImmutableArray<byte> current)
    {
        if (!original.AsSpan().SequenceEqual(current.AsSpan()))
        {
            throw Errors.Fail(ErrorCode.Corruption, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
    }

    internal static void RequireSameFence(ResourceRetirementOwnerFenceV1 original, ResourceRetirementOwnerFenceV1 current)
    {
        if (!NativeSerialization.Serialize(original).AsSpan().SequenceEqual(NativeSerialization.Serialize(current)))
        {
            throw Errors.Fail(ErrorCode.Corruption, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
    }
}

/// <summary>Refuses a D4 acknowledgement without throwing capacity failure through a committed D3 apply.</summary>
internal sealed class NativeResourceWorkCapacityRefusal : IResourceLifetimeWorkSettlement
{
    private readonly Lazy<Task> original = new(() => Task.FromException(
        Errors.Fail(ErrorCode.BudgetExceeded, NativeRequestWorkLimits.CapacityMessage)));

    public Task CancelAndJoinAsync() => original.Value;
}
