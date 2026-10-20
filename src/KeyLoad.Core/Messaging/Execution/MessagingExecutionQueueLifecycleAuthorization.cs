using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes MessagingExecutionQueueLifecycleAuthorization operations against the caller's database context.</summary>
public static class MessagingExecutionQueueLifecycleAuthorization
{
    internal static void AuthorizeQueueLifecycle(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, Mutation mutation)
    {
        if (!principal.ClusterAdministrator)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, QueueLifecycleProtocol.AdministratorRequired);
        }

        var (queue, _, _, _) = MessagingValidationQueueLifecycleQueueLifecycleConditions.QueueLifecycleOperands(mutation);
        var dataPrincipal = MessagingExecutionRemoteTransferRemoteTransferAuthorization.TransferDataPrincipal(principal);
        var capability = mutation is CancelQueueMessage ? Capability.QueueCancel : Capability.DeadLettersRedrive;
        engine.Authorization.Require(dataPrincipal, partition, queue, capability);
        var resource = engine.Resource(view, partition, queue, ResourceKind.WorkQueue);
        engine.RequireTransferFieldWrites(dataPrincipal, resource);
        engine.RequireTransferFieldUses(dataPrincipal, resource);
    }
}
