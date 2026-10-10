using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void AuthorizeQueueLifecycle(IKeyValueView view, PrincipalRecord principal, PartitionRef partition, Mutation mutation)
    {
        if (!principal.ClusterAdministrator)
        { throw Errors.Fail(ErrorCode.PermissionDenied, QueueLifecycleProtocol.AdministratorRequired); }
        var (queue, _, _, _) = QueueLifecycleOperands(mutation);
        var dataPrincipal = TransferDataPrincipal(principal);
        var capability = mutation is CancelQueueMessage ? Capability.QueueCancel : Capability.DeadLettersRedrive;
        Authorization.Require(dataPrincipal, partition, queue, capability);
        var resource = Resource(view, partition, queue, ResourceKind.WorkQueue);
        RequireTransferFieldWrites(dataPrincipal, resource);
        RequireTransferFieldUses(dataPrincipal, resource);
    }
}
