using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes MessagingQueriesRemoteTransferRemoteTransferDependencyRead operations against the caller's database context.</summary>
public static class MessagingQueriesRemoteTransferRemoteTransferDependencyRead
{
    internal static RemoteTransferAcceptDependency CaptureRemoteTransferDependency(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, QueueLaneRef destination)
    {
        var resource = engine.Resource(view, destination.Partition, destination.Queue, ResourceKind.WorkQueue);
        var counters = MessagingExecutionMessaging.Counters(view, destination);
        var retained = RemoteTransferStorage.TargetCapacity(view, destination);
        return new(principal.PolicyEpoch, JsonData.Fingerprint(new { resource.FieldPolicies, resource.HeaderPolicies }), resource.QueuePolicy.MaxStoredMessages, resource.QueuePolicy.MaxStoredBytes, counters.StoredMessages, counters.StoredBytes, retained.StoredRecords, retained.StoredBytes, engine.Limits.MaxBatchBytes, engine.Limits.MaxScanRecords);
    }
}
