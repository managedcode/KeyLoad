using System.Security.Cryptography;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal T WithPartitionQueryView<T>(string principalId, PartitionRef partition, string collection,
        ReadExecutionBudgetReadGrant grant, Func<IKeyValueView, PrincipalRecord, ResourceDefinition, T> read)
        => WithPartitionQueryFenceView(principalId, partition, collection, grant,
            (view, principal, resource, _) => read(view, principal, resource));

    internal T WithPartitionQueryFenceView<T>(string principalId, PartitionRef partition, string collection,
        ReadExecutionBudgetReadGrant grant, Func<IKeyValueView, PrincipalRecord, ResourceDefinition, string, T> read)
        => Store.Read(view =>
        {
            ValidatePartition(partition);
            var principal = ReadPartitionQueryAuthority<PrincipalRecord>(view, KeySpace.Principal(principalId), grant);
            if (principal is null || principal.Revoked || principal.ExpiresAt <= Clock.GetUtcNow())
            { throw Errors.Fail(ErrorCode.Unauthenticated, UnavailableCredentialDetail); }
            Authorization.Require(principal, partition, collection, Capability.Query | Capability.DocumentsRead);
            ResourceDefinition? resource = null;
            string? digest = null;
            grant.ReadValue(view, KeySpace.Resource(partition.TenantId, partition.DatabaseId, collection), bytes =>
            {
                resource = NativeSerialization.Deserialize<ResourceDefinition>(bytes);
                digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
            });
            if (resource is null || digest is null)
            { throw Errors.Fail(ErrorCode.NotFound, DatabaseEngineResourceIsNotConfiguredDetail); }
            if (resource.TransactionDomainId != partition.TransactionDomainId)
            { throw Errors.Fail(ErrorCode.Conflict, DatabaseEngineResourceBelongsToADifferentTransactionDomainDetail); }
            if (resource.Kind != ResourceKind.Collection)
            { throw Errors.Fail(ErrorCode.Validation, DatabaseEngineResourceHasADifferentKindDetail); }
            return read(view, principal, resource, digest);
        });

    private static T? ReadPartitionQueryAuthority<T>(IKeyValueView view, byte[] key,
        ReadExecutionBudgetReadGrant grant) where T : class
    {
        T? record = null;
        grant.ReadValue(view, key, bytes => record = NativeSerialization.Deserialize<T>(bytes));
        return record;
    }
}
