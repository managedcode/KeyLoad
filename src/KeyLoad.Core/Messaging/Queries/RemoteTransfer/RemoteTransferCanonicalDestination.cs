using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes RemoteTransferCanonicalDestination operations against the caller's database context.</summary>
public static class RemoteTransferCanonicalDestination
{
    internal static ResourceDefinition RemoteTransferDestinationResource(this DatabaseEngine engine, IKeyValueView view, QueueLaneRef destination) => engine.Resource(view, destination.Partition, destination.Queue, ResourceKind.WorkQueue);
}
