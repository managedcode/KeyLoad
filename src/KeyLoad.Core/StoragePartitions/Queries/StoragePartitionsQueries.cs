using KeyLoad.Core.StoragePartitions.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes StoragePartitionsQueries operations against the caller's database context.</summary>
public static class StoragePartitionsQueries
{
    internal const string StorageIdentityAdminRequired = "Cluster administration is required.";
    /// <summary>Reads the actual local storage identity after fresh administrator authorization.</summary>
    /// <param name = "principalId">The current persisted principal.</param>
    /// <param name = "partition">The complete atomic partition identity.</param>
    /// <returns>The storage identity used for this operation.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static AtomicPartitionPlacementResolution ReadAtomicPartitionPlacement(this DatabaseEngine engine, string principalId, PartitionRef partition)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.Store.Read(view => engine.ReadAtomicPartitionPlacement(view, principalId, partition));
    }

    internal static AtomicPartitionPlacementResolution ReadAtomicPartitionPlacement(this DatabaseEngine engine, IKeyValueView view, string principalId, PartitionRef partition)
    {
        ArgumentNullException.ThrowIfNull(view);
        EngineResourceAccess.ValidatePartition(partition);
        if (!engine.Principal(view, principalId, engine.Clock.GetUtcNow()).ClusterAdministrator)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, StorageIdentityAdminRequired);
        }

        return engine.ReadPlacementWitness(view, partition);
    }

    internal static AtomicPartitionPlacementResolution ReadPlacementWitness(this DatabaseEngine engine, IKeyValueView view, PartitionRef partition)
    {
        ArgumentNullException.ThrowIfNull(view);
        EngineResourceAccess.ValidatePartition(partition);
        var identity = engine.Store.Identity;
        return new(AtomicPartitionPlacementProtocol.CurrentVersion, partition, identity.NodeId, identity.Incarnation);
    }
}
