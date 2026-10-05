using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.GraphTraversal;

internal static class GraphIncomingReadScope
{
    private const string MissingPrincipal = "The credential is unavailable or expired.";
    private const string MissingResource = "The resource is not configured.";
    private const string ResourceDomainConflict = "The resource belongs to a different transaction domain.";
    private const string ResourceKindConflict = "The resource has a different kind.";

    internal static PrincipalRecord ReadPrincipal(IKeyValueView view, ReadExecutionBudgetReadGrant grant,
        string principalId, DateTimeOffset now)
    {
        var principal = GraphCrossPartitionRecords.Read<PrincipalRecord>(view,
            KeySpace.Principal(principalId), grant);
        if (principal is null || principal.Revoked || principal.ExpiresAt <= now)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, MissingPrincipal);
        }
        return principal;
    }

    internal static ResourceDefinition ReadGraphResource(IKeyValueView view,
        ReadExecutionBudgetReadGrant grant, PartitionRef partition, string graph)
    {
        var resource = GraphCrossPartitionRecords.Read<ResourceDefinition>(view,
            KeySpace.Resource(partition.TenantId, partition.DatabaseId, graph), grant)
            ?? throw Errors.Fail(ErrorCode.NotFound, MissingResource);
        if (resource.TransactionDomainId != partition.TransactionDomainId)
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceDomainConflict);
        }
        if (resource.Kind != ResourceKind.Graph)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceKindConflict);
        }
        return resource;
    }

    internal static ResourceDefinition ReadCollection(IKeyValueView view,
        ReadExecutionBudgetReadGrant grant, PartitionRef partition, string collection)
    {
        var resource = GraphCrossPartitionRecords.Read<ResourceDefinition>(view,
            KeySpace.Resource(partition.TenantId, partition.DatabaseId, collection), grant)
            ?? throw Errors.Fail(ErrorCode.NotFound, MissingResource);
        if (resource.TransactionDomainId != partition.TransactionDomainId)
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceDomainConflict);
        }
        if (resource.Kind != ResourceKind.Collection)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceKindConflict);
        }
        return resource;
    }

    internal static DocumentRecord? ReadDocument(IKeyValueView view,
        ReadExecutionBudgetReadGrant grant, EntityRef entity)
        => GraphCrossPartitionRecords.Read<DocumentRecord>(view,
            DocumentStorageKeys.RecordKey(entity), grant);
}
