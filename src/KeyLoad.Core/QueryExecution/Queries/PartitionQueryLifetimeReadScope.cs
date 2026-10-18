using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string PartitionQuerySourceMetadataInvalid = "The query source metadata is invalid.";

    internal T WithPartitionQueryLifetimeView<T>(string principalId, PartitionRef partition, string collection,
        ReadExecutionBudgetReadGrant grant, ReadExecutionBudget budget, PartitionQueryLifetimeMetadata metadata,
        Func<IKeyValueView, PrincipalRecord, ResourceDefinition, string, T> read)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        budget.Check();
        ResourceLifetimeCoverage.Require(metadata.Coverage, partition, [metadata.Definition], Limits.MaxBatchBytes);
        if (metadata.Principal is null || metadata.Principal.Id != principalId
            || metadata.Definition.Name != collection || metadata.Definition.Kind != ResourceKind.Collection)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, PartitionQuerySourceMetadataInvalid);
        }
        if (metadata.Principal.Revoked || metadata.Principal.ExpiresAt <= Clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, UnavailableCredentialDetail);
        }
        Authorization.Require(metadata.Principal, partition, collection, Capability.Query | Capability.DocumentsRead);
        return Store.Read(view => ReadPartitionQueryFenceView(view, principalId, partition, collection, grant,
            (raw, principal, resource, digest) =>
            {
                budget.CaptureAuthenticatedResourceLifetimes(this, budget.CreateView(raw, grant),
                    metadata.Coverage, [metadata.Definition]);
                return read(raw, principal, resource, digest);
            }, budget));
    }
}
