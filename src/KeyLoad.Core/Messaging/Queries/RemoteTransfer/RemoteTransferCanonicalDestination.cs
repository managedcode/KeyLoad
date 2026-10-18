using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private ResourceDefinition RemoteTransferDestinationResource(IKeyValueView view, QueueLaneRef destination)
        => Resource(view, destination.Partition, destination.Queue, ResourceKind.WorkQueue);
}
