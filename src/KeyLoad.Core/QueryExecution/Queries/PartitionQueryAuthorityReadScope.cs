using System.Security.Cryptography;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Core.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes PartitionQueryAuthorityReadScope operations against the caller's database context.</summary>
public static class PartitionQueryAuthorityReadScope
{
    internal static T WithPartitionQueryView<T>(this DatabaseEngine engine, string principalId, PartitionRef partition, string collection, ReadExecutionBudgetReadGrant grant, Func<IKeyValueView, PrincipalRecord, ResourceDefinition, T> read, ReadExecutionBudget? work = null) => engine.WithPartitionQueryFenceView(principalId, partition, collection, grant, (view, principal, resource, _) => read(view, principal, resource), work);
    internal static T WithPartitionQueryFenceView<T>(this DatabaseEngine engine, string principalId, PartitionRef partition, string collection, ReadExecutionBudgetReadGrant grant, Func<IKeyValueView, PrincipalRecord, ResourceDefinition, string, T> read, ReadExecutionBudget? work = null) => engine.Store.Read(view => engine.ReadPartitionQueryFenceView(view, principalId, partition, collection, grant, read, work));
    internal static T ReadPartitionQueryFenceView<T>(this DatabaseEngine engine, IKeyValueView view, string principalId, PartitionRef partition, string collection, ReadExecutionBudgetReadGrant grant, Func<IKeyValueView, PrincipalRecord, ResourceDefinition, string, T> read, ReadExecutionBudget? work = null)
    {
        EngineResourceAccess.ValidatePartition(partition);
        var principal = ReadPartitionQueryAuthority<PrincipalRecord>(view, KeySpace.Principal(principalId), grant);
        if (principal is null || principal.Revoked || principal.ExpiresAt <= engine.Clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, AuthorizationExecution.UnavailableCredentialDetail);
        }

        engine.Authorization.Require(principal, partition, collection, Capability.Query | Capability.DocumentsRead);
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
            throw Errors.Fail(ErrorCode.NotFound, EngineResourceAccess.DatabaseEngineResourceIsNotConfiguredDetail);
        }

        if (resource.TransactionDomainId != partition.TransactionDomainId)
        {
            throw Errors.Fail(ErrorCode.Conflict, EngineResourceAccess.DatabaseEngineResourceBelongsToADifferentTransactionDomainDetail);
        }

        if (resource.Kind != ResourceKind.Collection)
        {
            throw Errors.Fail(ErrorCode.Validation, EngineResourceAccess.DatabaseEngineResourceHasADifferentKindDetail);
        }

        var actualLifetime = lifetime ?? throw Errors.Fail(ErrorCode.Corruption, ResourceLifetimeValidationMessages.MissingAuthority);
        work?.ObserveResourceLifetime(partition, resource, actualLifetime);
        return read(view, principal, resource, digest);
    }

    internal static T? ReadPartitionQueryAuthority<T>(IKeyValueView view, byte[] key, ReadExecutionBudgetReadGrant grant)
        where T : class
    {
        T? record = null;
        grant.ReadValue(view, key, bytes => record = OrleansSerialization.Deserialize<T>(bytes));
        return record;
    }
}
