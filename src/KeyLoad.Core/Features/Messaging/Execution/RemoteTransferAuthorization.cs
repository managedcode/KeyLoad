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
        => principal with { ClusterAdministrator = false };

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
            Authorization.RequireFieldWrite(principal, resource with { FieldPolicies = resource.HeaderPolicies }, policy.Path);
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
            Authorization.RequireFieldUse(principal, resource with { FieldPolicies = resource.HeaderPolicies }, policy.Path);
        }
    }
}
