using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes MessagingQueriesRemoteTransferRemoteTransferCanonicalDestination operations against the caller's database context.</summary>
public static class MessagingQueriesRemoteTransferRemoteTransferCanonicalDestination
{
    internal static ResourceDefinition RemoteTransferDestinationResource(this DatabaseEngine engine, IKeyValueView view, QueueLaneRef destination) => engine.Resource(view, destination.Partition, destination.Queue, ResourceKind.WorkQueue);
}
