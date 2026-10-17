using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void AuthorizeQueueDeadline(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, AdvanceQueueDeadline request)
    {
        Authorization.Require(principal, partition, request.Queue, Capability.QueueConsume);
        Authorization.RequireWorkerInput(principal, Resource(view, partition, request.Queue, ResourceKind.WorkQueue));
    }
}
