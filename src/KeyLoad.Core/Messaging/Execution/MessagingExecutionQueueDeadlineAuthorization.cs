using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes MessagingExecutionQueueDeadlineAuthorization operations against the caller's database context.</summary>
public static class MessagingExecutionQueueDeadlineAuthorization
{
    internal static void AuthorizeQueueDeadline(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, AdvanceQueueDeadline request)
    {
        engine.Authorization.Require(principal, partition, request.Queue, Capability.QueueConsume);
        engine.Authorization.RequireWorkerInput(principal, engine.Resource(view, partition, request.Queue, ResourceKind.WorkQueue));
    }
}
