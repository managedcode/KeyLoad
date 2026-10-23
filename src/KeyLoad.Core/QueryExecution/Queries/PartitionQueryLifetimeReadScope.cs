using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes PartitionQueryLifetimeReadScope operations against the caller's database context.</summary>
public static class PartitionQueryLifetimeReadScope
{
    internal const string PartitionQuerySourceMetadataInvalid = "The query source metadata is invalid.";
    internal const string PartitionQueryReceivingIdentityChanged = "The partition query receiving identity changed.";
    internal static T WithPartitionQueryLifetimeView<T>(this DatabaseEngine engine, string principalId, PartitionRef partition, string collection, ReadExecutionBudgetReadGrant grant, ReadExecutionBudget budget, PartitionQueryLifetimeMetadata metadata, Func<IKeyValueView, PrincipalRecord, ResourceDefinition, string, T> read)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        budget.Check();
        ResourceLifetimeCoverageValidation.Require(metadata.Coverage, partition, [metadata.Definition], engine.Limits.MaxBatchBytes);
        if (metadata.Principal is null || metadata.Principal.Id != principalId || metadata.Definition.Name != collection || metadata.Definition.Kind != ResourceKind.Collection)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, PartitionQuerySourceMetadataInvalid);
        }

        if (metadata.Principal.Revoked || metadata.Principal.ExpiresAt <= engine.Clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, AuthorizationExecution.UnavailableCredentialDetail);
        }

        engine.Authorization.Require(metadata.Principal, partition, collection, Capability.Query | Capability.DocumentsRead);
        return engine.Store.Read(view => engine.ReadPartitionQueryFenceView(view, principalId, partition, collection, grant, (raw, principal, resource, digest) =>
        {
            budget.Check();
            engine.RequireBudget(OrleansSerialization.Measure(principal));
            engine.RequireBudget(OrleansSerialization.Measure(metadata.Principal));
            if (!OrleansSerialization.Serialize(principal).AsSpan().SequenceEqual(OrleansSerialization.Serialize(metadata.Principal)))
            {
                throw Errors.Fail(ErrorCode.PermissionDenied, PartitionQueryReceivingIdentityChanged);
            }

            budget.Check();
            budget.CaptureAuthenticatedResourceLifetimes(engine, budget.CreateView(raw, grant), metadata.Coverage, [metadata.Definition]);
            return read(raw, principal, resource, digest);
        }, budget));
    }
}
