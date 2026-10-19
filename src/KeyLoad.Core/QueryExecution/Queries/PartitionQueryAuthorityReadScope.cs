using System.Security.Cryptography;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal T WithPartitionQueryView<T>(string principalId, PartitionRef partition, string collection,
        ReadExecutionBudgetReadGrant grant, Func<IKeyValueView, PrincipalRecord, ResourceDefinition, T> read, ReadExecutionBudget? work = null)
        => WithPartitionQueryFenceView(principalId, partition, collection, grant,
            (view, principal, resource, _) => read(view, principal, resource), work);

    internal T WithPartitionQueryFenceView<T>(string principalId, PartitionRef partition, string collection,
        ReadExecutionBudgetReadGrant grant, Func<IKeyValueView, PrincipalRecord, ResourceDefinition, string, T> read, ReadExecutionBudget? work = null)
        => Store.Read(view => ReadPartitionQueryFenceView(view, principalId, partition, collection, grant, read, work));

    private T ReadPartitionQueryFenceView<T>(IKeyValueView view, string principalId, PartitionRef partition,
        string collection, ReadExecutionBudgetReadGrant grant,
        Func<IKeyValueView, PrincipalRecord, ResourceDefinition, string, T> read, ReadExecutionBudget? work = null)
    {
        ValidatePartition(partition);
        var principal = ReadPartitionQueryAuthority<PrincipalRecord>(view, KeySpace.Principal(principalId), grant);
        if (principal is null || principal.Revoked || principal.ExpiresAt <= Clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, UnavailableCredentialDetail);
        }
        Authorization.Require(principal, partition, collection, Capability.Query | Capability.DocumentsRead);
        ResourceDefinition? resource = null;
        string? digest = null;
        grant.ReadValue(view, KeySpace.Resource(partition.TenantId, partition.DatabaseId, collection), bytes =>
        {
            resource = OrleansSerialization.Deserialize<ResourceDefinition>(bytes);
            digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        });
        var charged = grant.CreateView(view);
        var lifetime = ResourceLifetimeCatalog.Read(charged, partition.TenantId, partition.DatabaseId, collection, resource);
        if (resource is null || digest is null)
        {
            throw Errors.Fail(ErrorCode.NotFound, DatabaseEngineResourceIsNotConfiguredDetail);
        }
        if (resource.TransactionDomainId != partition.TransactionDomainId)
        {
            throw Errors.Fail(ErrorCode.Conflict, DatabaseEngineResourceBelongsToADifferentTransactionDomainDetail);
        }
        if (resource.Kind != ResourceKind.Collection)
        {
            throw Errors.Fail(ErrorCode.Validation, DatabaseEngineResourceHasADifferentKindDetail);
        }
        var actualLifetime = lifetime ?? throw Errors.Fail(ErrorCode.Corruption, ResourceLifetimeValidationMessages.MissingAuthority);
        work?.ObserveResourceLifetime(partition, resource, actualLifetime);
        return read(view, principal, resource, digest);
    }

    private static T? ReadPartitionQueryAuthority<T>(IKeyValueView view, byte[] key,
        ReadExecutionBudgetReadGrant grant) where T : class
    {
        T? record = null;
        grant.ReadValue(view, key, bytes => record = OrleansSerialization.Deserialize<T>(bytes));
        return record;
    }
}
