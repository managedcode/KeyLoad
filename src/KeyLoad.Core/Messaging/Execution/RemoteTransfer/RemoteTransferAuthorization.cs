namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string TransferAdministratorMessage = "Cluster administration is required for queue transfers.";
    private static void RequireTransferAdministrator(PrincipalRecord principal)
    {
        if (!principal.ClusterAdministrator)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, TransferAdministratorMessage);
        }
    }

    private void RequireQueueTransferCapabilities(PrincipalRecord principal, Mutation mutation)
    {
        switch (mutation)
        {
            case CreateQueueTransfer create:
                RequireTransferAdministrator(principal);
                RequireTransferCapabilities(principal, create.SourceQueue, Capability.QueuePublish);
                RequireTransferCapabilities(principal, create.Destination, Capability.QueuePublish);
                break;
            case AcceptQueueTransfer accept:
                RequireTransferAdministrator(principal);
                RequireTransferCapabilities(principal, accept.DestinationQueue, Capability.QueuePublish);
                break;
            case CompleteQueueTransfer complete:
                RequireTransferAdministrator(principal);
                RequireTransferCapabilities(principal, complete.SourceQueue, Capability.QueuePublish);
                break;
            case AdvanceQueueTransferAttempt advance:
                RequireTransferAdministrator(principal);
                RequireTransferCapabilities(principal, advance.SourceQueue, Capability.QueueInspect);
                RequireTransferCapabilities(principal, advance.SourceQueue, Capability.QueuePublish);
                break;
            case AdvanceQueueTransferRepair repair:
                RequireTransferAdministrator(principal);
                RequireTransferCapabilities(principal, repair.SourceQueue, Capability.QueueInspect);
                RequireTransferCapabilities(principal, repair.SourceQueue, Capability.QueuePublish);
                break;
        }
    }

    private void RequireTransferCapabilities(PrincipalRecord principal, QueueLaneRef lane, Capability capabilities)
        => Authorization.Require(TransferDataPrincipal(principal), lane.Partition, lane.Queue, capabilities);

    private void RequireTransferPublisher(PrincipalRecord principal, QueueLaneRef lane, ResourceDefinition resource)
    {
        var dataPrincipal = TransferDataPrincipal(principal);
        Authorization.Require(dataPrincipal, lane.Partition, lane.Queue, Capability.QueuePublish);
        RequireTransferFieldWrites(dataPrincipal, resource);
    }

    private void RequireTransferInspector(PrincipalRecord principal, QueueLaneRef lane, ResourceDefinition resource)
    {
        var dataPrincipal = TransferDataPrincipal(principal);
        Authorization.Require(dataPrincipal, lane.Partition, lane.Queue, Capability.QueueInspect);
        RequireTransferFieldUses(dataPrincipal, resource);
    }

    private static PrincipalRecord TransferDataPrincipal(PrincipalRecord principal)
        => principal with
        {
            ClusterAdministrator = false
        };

    private void RequireTransferFieldWrites(PrincipalRecord principal, ResourceDefinition resource)
    {
        foreach (var policy in resource.FieldPolicies)
        {
            Authorization.RequireFieldWrite(principal, resource, policy.Path);
        }
        RequireTransferHeaderWrites(principal, resource);
    }

    private void RequireTransferHeaderWrites(PrincipalRecord principal, ResourceDefinition resource)
    {
        foreach (var policy in resource.HeaderPolicies)
        {
            Authorization.RequireFieldWrite(principal, resource with
            {
                FieldPolicies = resource.HeaderPolicies
            }, policy.Path);
        }
    }

    private void RequireTransferFieldUses(PrincipalRecord principal, ResourceDefinition resource)
    {
        foreach (var policy in resource.FieldPolicies)
        {
            Authorization.RequireFieldUse(principal, resource, policy.Path);
        }
        RequireTransferHeaderUses(principal, resource);
    }

    private void RequireTransferHeaderUses(PrincipalRecord principal, ResourceDefinition resource)
    {
        foreach (var policy in resource.HeaderPolicies)
        {
            Authorization.RequireFieldUse(principal, resource with
            {
                FieldPolicies = resource.HeaderPolicies
            }, policy.Path);
        }
    }
}
