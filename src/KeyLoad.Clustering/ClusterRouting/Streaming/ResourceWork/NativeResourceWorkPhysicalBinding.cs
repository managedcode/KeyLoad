using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

/// <summary>Binds the existing native owner to its actual borrowed physical database before admission.</summary>
internal sealed class NativeResourceWorkPhysicalBinding
{
    private PhysicalShardRecord? physical;
    private DatabaseEngine? database;
    private long attachedApplied;
    private Guid attachedNode;
    private long attachedGeneration;

    internal void Bind(PhysicalShardRecord actualOwner)
    {
        ArgumentNullException.ThrowIfNull(actualOwner);
        if (physical is not null && !Same(physical, actualOwner))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
        physical = actualOwner;
    }

    internal void Attach(DatabaseEngine sameDatabase, IResourceLifetimeWorkFence owner, long applied, Guid node, long generation)
    {
        ArgumentNullException.ThrowIfNull(sameDatabase);
        if (physical is null || database is not null && !ReferenceEquals(database, sameDatabase))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
        if (database is not null)
        {
            return;
        }
        attachedApplied = applied;
        attachedNode = node;
        attachedGeneration = generation;
        sameDatabase.AttachResourceLifetimeWorkFence(owner);
        database = sameDatabase;
    }

    internal PhysicalShardRecord Require(ResourceRetirementOwnerFenceV1 fence)
    {
        var actual = physical ?? throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
        if (!Same(actual, fence.Receipt.PhysicalOwner))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
        return actual;
    }

    internal static (long Applied, Guid NodeId, long Generation) CaptureStartup(DatabaseEngine database, long durableLogLastIndex)
    {
        const long InitialPosition = 0;
        if (durableLogLastIndex < InitialPosition)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
        var identity = database.Store.Identity;
        return (Math.Max(database.LastApplied, durableLogLastIndex), identity.NodeId, identity.ReadGeneration);
    }

    internal bool CanQualify(ResourceRetirementOwnerFenceV1 fence)
    {
        var actual = database ?? throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
        var identity = actual.Store.Identity;
        return fence.Receipt.AppliedPosition > attachedApplied && identity.NodeId == attachedNode
            && identity.ReadGeneration == attachedGeneration;
    }

    internal void RequireDatabase(DatabaseEngine actualDatabase)
    {
        if (!ReferenceEquals(database, actualDatabase))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
    }

    internal void RequireActual(PhysicalShardRecord actualOwner, ReplicaSiloDiscovery runtime)
    {
        if (physical is null || !Same(physical, actualOwner) || !runtime.TransportReady
            || runtime.Incarnation != actualOwner.Incarnation
            || !actualOwner.VoterIds.Contains(runtime.VoterId, StringComparer.Ordinal)
            || string.IsNullOrEmpty(runtime.SiloAddress))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
    }

    private static bool Same(PhysicalShardRecord first, PhysicalShardRecord second)
        => NativeSerialization.Serialize(first).AsSpan().SequenceEqual(NativeSerialization.Serialize(second));
}
