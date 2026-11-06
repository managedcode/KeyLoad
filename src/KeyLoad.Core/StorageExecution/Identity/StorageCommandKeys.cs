using KeyLoad.Core.CommandOutcomes.Identity;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

/// <summary>Addresses the ordering state of one atomic partition or the shared metadata command owner.</summary>
public static class StorageCommandKeys
{
    private const string Family = "command-order";
    private const string AppliedFamily = "command-applied";
    private const string Metadata = "metadata";
    private const string ClockName = "clock";
    private const string ReplicaName = "replica-batch";

    /// <summary>Returns the validated atomic scope of an admitted operation; metadata and invalid inputs share one owner.</summary>
    public static PartitionRef? PartitionOf(Command<DatabaseCommand> operation)
    {
        var scope = CommandOutcomePartitionIdentity.Resolve(operation);
        return scope.Partition;
    }

    /// <summary>Addresses the persisted sequence for one command owner.</summary>
    public static byte[] Applied(PartitionRef? partition) => partition is null
        ? KeyCodec.Encode(AppliedFamily, Metadata) : KeySpace.Partition(AppliedFamily, partition);
    internal static byte[] AppliedPrefix => KeyCodec.Encode(AppliedFamily);
    internal static byte[] Clock(PartitionRef? partition) => Key(partition, ClockName);
    internal static byte[] ReplicaBatch(PartitionRef? partition) => Key(partition, ReplicaName);

    private static byte[] Key(PartitionRef? partition, string name)
        => partition is null ? KeyCodec.Encode(Family, Metadata, name)
            : KeySpace.Partition(Family, partition, name);
}
