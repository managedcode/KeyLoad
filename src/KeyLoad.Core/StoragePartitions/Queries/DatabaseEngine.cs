using KeyLoad.Core.Features.CommandOutcomes.Contracts;
using KeyLoad.Core.Features.RuntimeJournaling.Contracts;
using KeyLoad.Core.Features.StoragePartitions.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string StorageIdentityAdminRequired = "Cluster administration is required.";

    /// <summary>Reads the actual local storage identity after fresh administrator authorization.</summary>
    /// <param name="principalId">The current persisted principal.</param>
    /// <param name="partition">The complete atomic partition identity.</param>
    /// <returns>The storage identity used for this operation.</returns>
    public AtomicPartitionPlacementResolution ReadAtomicPartitionPlacement(string principalId, PartitionRef partition)
        => Store.Read(view => ReadAtomicPartitionPlacement(view, principalId, partition));

    internal AtomicPartitionPlacementResolution ReadAtomicPartitionPlacement(IKeyValueView view,
        string principalId, PartitionRef partition)
    {
        ArgumentNullException.ThrowIfNull(view);
        ValidatePartition(partition);
        if (!Principal(view, principalId, Clock.GetUtcNow()).ClusterAdministrator)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, StorageIdentityAdminRequired);
        }
        return ReadPlacementWitness(view, partition);
    }

    internal AtomicPartitionPlacementResolution ReadPlacementWitness(IKeyValueView view, PartitionRef partition)
    {
        ArgumentNullException.ThrowIfNull(view);
        ValidatePartition(partition);
        var identity = Store.Identity;
        return new(AtomicPartitionPlacementProtocol.CurrentVersion, partition, identity.NodeId, identity.Incarnation);
    }
}
