namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal static AtomicPartitionPlacementResolution ReadClusterBackupPlacement(PartitionRef partition, PhysicalShardRecord control) => ResolveRegisteredPlacement(partition, control);
}
