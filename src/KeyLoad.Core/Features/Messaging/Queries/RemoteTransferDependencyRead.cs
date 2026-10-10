using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private RemoteTransferAcceptDependency CaptureRemoteTransferDependency(IKeyValueView view,
        PrincipalRecord principal, QueueLaneRef destination)
    {
        var resource = Resource(view, destination.Partition, destination.Queue, ResourceKind.WorkQueue);
        var counters = Counters(view, destination);
        var retained = RemoteTransferStorage.TargetCapacity(view, destination);
        return new(principal.PolicyEpoch, JsonData.Fingerprint(new { resource.FieldPolicies, resource.HeaderPolicies }),
            resource.QueuePolicy.MaxStoredMessages, resource.QueuePolicy.MaxStoredBytes, counters.StoredMessages,
            counters.StoredBytes, retained.StoredRecords, retained.StoredBytes, Limits.MaxBatchBytes, Limits.MaxScanRecords);
    }
}
