using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal static AtomicPartitionPlacementResolution ReadClusterBackupPlacement(IKeyValueView view,
        PartitionRef partition, PhysicalShardRecord control) => ResolveRegisteredPlacement(view, partition, control);
}
