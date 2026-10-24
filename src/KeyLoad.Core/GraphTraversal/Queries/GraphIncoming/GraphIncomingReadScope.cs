using KeyLoad.Core.DocumentStorage;
using KeyLoad.Core.GraphTraversal.Serialization;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Core.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core.GraphTraversal;

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
        ReadExecutionBudgetReadGrant grant, PartitionRef partition, string graph, ReadExecutionBudget budget)
    {
        var resource = GraphCrossPartitionRecords.Read<ResourceDefinition>(view,
            KeySpace.Resource(partition.TenantId, partition.DatabaseId, graph), grant);
        var metadata = budget.CreateView(view, grant);
        var lifetime = ResourceLifetimeCatalog.Read(metadata, partition.TenantId, partition.DatabaseId, graph, resource);
        if (resource is null)
        {
            throw Errors.Fail(ErrorCode.NotFound, MissingResource);
        }
        if (resource.TransactionDomainId != partition.TransactionDomainId)
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceDomainConflict);
        }
        if (resource.Kind != ResourceKind.Graph)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceKindConflict);
        }
        ResourceLifetimeReadBinding.BindAuthenticatedReadResourceLifetime(metadata, partition, resource, lifetime!);
        return resource;
    }

    internal static ResourceDefinition ReadCollection(IKeyValueView view,
        ReadExecutionBudgetReadGrant grant, PartitionRef partition, string collection, ReadExecutionBudget budget)
    {
        var resource = GraphCrossPartitionRecords.Read<ResourceDefinition>(view,
            KeySpace.Resource(partition.TenantId, partition.DatabaseId, collection), grant);
        var metadata = budget.CreateView(view, grant);
        var lifetime = ResourceLifetimeCatalog.Read(metadata, partition.TenantId, partition.DatabaseId, collection, resource);
        if (resource is null)
        {
            throw Errors.Fail(ErrorCode.NotFound, MissingResource);
        }
        if (resource.TransactionDomainId != partition.TransactionDomainId)
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceDomainConflict);
        }
        if (resource.Kind != ResourceKind.Collection)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceKindConflict);
        }
        ResourceLifetimeReadBinding.BindAuthenticatedReadResourceLifetime(metadata, partition, resource, lifetime!);
        return resource;
    }

    internal static DocumentRecord? ReadDocument(IKeyValueView view,
        ReadExecutionBudgetReadGrant grant, EntityRef entity)
    {
        const string VertexMismatch = "A canonical graph vertex record is inconsistent.";
        var document = GraphCrossPartitionRecords.Read<DocumentRecord>(view,
            DocumentStorageKeys.RecordKey(entity), grant);
        if (document is not null && document.Reference != entity)
        {
            throw Errors.Fail(ErrorCode.Corruption, VertexMismatch);
        }
        return document;
    }
}
