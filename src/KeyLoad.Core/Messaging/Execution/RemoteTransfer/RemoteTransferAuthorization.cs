namespace KeyLoad.Core;
/// <summary>Executes RemoteTransferAuthorization operations against the caller's database context.</summary>
public static class RemoteTransferAuthorization
{
    internal const string TransferAdministratorMessage = "Cluster administration is required for queue transfers.";
    internal static void RequireTransferAdministrator(PrincipalRecord principal)
    {
        if (!principal.ClusterAdministrator)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, TransferAdministratorMessage);
        }
    }

    internal static void RequireQueueTransferCapabilities(this DatabaseEngine engine, PrincipalRecord principal, Mutation mutation)
    {
        switch (mutation)
        {
            case CreateQueueTransfer create:
                RequireTransferAdministrator(principal);
                engine.RequireTransferCapabilities(principal, create.SourceQueue, Capability.QueuePublish);
                engine.RequireTransferCapabilities(principal, create.Destination, Capability.QueuePublish);
                break;
            case AcceptQueueTransfer accept:
                RequireTransferAdministrator(principal);
                engine.RequireTransferCapabilities(principal, accept.DestinationQueue, Capability.QueuePublish);
                break;
            case CompleteQueueTransfer complete:
                RequireTransferAdministrator(principal);
                engine.RequireTransferCapabilities(principal, complete.SourceQueue, Capability.QueuePublish);
                break;
            case AdvanceQueueTransferAttempt advance:
                RequireTransferAdministrator(principal);
                engine.RequireTransferCapabilities(principal, advance.SourceQueue, Capability.QueueInspect);
                engine.RequireTransferCapabilities(principal, advance.SourceQueue, Capability.QueuePublish);
                break;
            case AdvanceQueueTransferRepair repair:
                RequireTransferAdministrator(principal);
                engine.RequireTransferCapabilities(principal, repair.SourceQueue, Capability.QueueInspect);
                engine.RequireTransferCapabilities(principal, repair.SourceQueue, Capability.QueuePublish);
                break;
        }
    }

    internal static void RequireTransferCapabilities(this DatabaseEngine engine, PrincipalRecord principal, QueueLaneRef lane, Capability capabilities) => engine.Authorization.Require(TransferDataPrincipal(principal), lane.Partition, lane.Queue, capabilities);
    internal static void RequireTransferPublisher(this DatabaseEngine engine, PrincipalRecord principal, QueueLaneRef lane, ResourceDefinition resource)
    {
        var dataPrincipal = TransferDataPrincipal(principal);
        engine.Authorization.Require(dataPrincipal, lane.Partition, lane.Queue, Capability.QueuePublish);
        engine.RequireTransferFieldWrites(dataPrincipal, resource);
    }

    internal static void RequireTransferInspector(this DatabaseEngine engine, PrincipalRecord principal, QueueLaneRef lane, ResourceDefinition resource)
    {
        var dataPrincipal = TransferDataPrincipal(principal);
        engine.Authorization.Require(dataPrincipal, lane.Partition, lane.Queue, Capability.QueueInspect);
        engine.RequireTransferFieldUses(dataPrincipal, resource);
    }

    internal static PrincipalRecord TransferDataPrincipal(PrincipalRecord principal) => principal with
    {
        ClusterAdministrator = false
    };
    internal static void RequireTransferFieldWrites(this DatabaseEngine engine, PrincipalRecord principal, ResourceDefinition resource)
    {
        foreach (var policy in resource.FieldPolicies)
        {
            engine.Authorization.RequireFieldWrite(principal, resource, policy.Path);
        }

        engine.RequireTransferHeaderWrites(principal, resource);
    }

    internal static void RequireTransferHeaderWrites(this DatabaseEngine engine, PrincipalRecord principal, ResourceDefinition resource)
    {
        foreach (var policy in resource.HeaderPolicies)
        {
            engine.Authorization.RequireFieldWrite(principal, resource with { FieldPolicies = resource.HeaderPolicies }, policy.Path);
        }
    }

    internal static void RequireTransferFieldUses(this DatabaseEngine engine, PrincipalRecord principal, ResourceDefinition resource)
    {
        foreach (var policy in resource.FieldPolicies)
        {
            engine.Authorization.RequireFieldUse(principal, resource, policy.Path);
        }

        engine.RequireTransferHeaderUses(principal, resource);
    }

    internal static void RequireTransferHeaderUses(this DatabaseEngine engine, PrincipalRecord principal, ResourceDefinition resource)
    {
        foreach (var policy in resource.HeaderPolicies)
        {
            engine.Authorization.RequireFieldUse(principal, resource with { FieldPolicies = resource.HeaderPolicies }, policy.Path);
        }
    }
}
