using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.Search;

internal static class AnnLifetimeReadAuthority
{
    private const string Invalid = "The ANN source lifetime authority is invalid.";
    private const string FieldUnavailable = "The requested ANN field binding is unavailable.";

    internal static ResourceLifetimeV1 Require(DatabaseEngine database, IKeyValueView view, PrincipalRecord target,
        ResourceDefinition targetResource, PartitionRef partition, string collection, string field,
        AnnReadLifetimeMetadata source, ReadExecutionBudget budget)
    {
        budget.Check();
        source.Require(partition, database.Limits.MaxBatchBytes);
        if (source.Principal is null || source.Principal.Id != target.Id
            || source.Definition.Name != collection || source.Definition.Kind != ResourceKind.Collection)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, Invalid);
        }
        if (source.Principal.Revoked || source.Principal.ExpiresAt <= database.EvaluationClock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, Invalid);
        }
        var lifetime = ResourceLifetimeCatalog.Read(budget.CreateView(view), partition.TenantId,
            partition.DatabaseId, collection, targetResource)
            ?? throw Errors.Fail(ErrorCode.Corruption, Invalid);
        RequireSchema(database, source.Principal, source.Definition, field);
        RequireSchema(database, target, targetResource, field);
        database.Authorization.Require(source.Principal, partition, collection, Capability.VectorSearch);
        database.Authorization.Require(target, partition, collection, Capability.VectorSearch);
        database.Authorization.RequireFieldUse(source.Principal, source.Definition, field);
        database.Authorization.RequireFieldUse(target, targetResource, field);
        return lifetime;
    }

    internal static void RequireSchema(DatabaseEngine database, PrincipalRecord principal,
        ResourceDefinition resource, string field)
    {
        foreach (var policy in database.Authorization.GetEffectiveFieldPolicies(resource, field))
        {
            if (!database.Authorization.CanReadFieldSchema(principal, policy))
            {
                throw Errors.Fail(ErrorCode.Validation, FieldUnavailable);
            }
        }
    }
}
