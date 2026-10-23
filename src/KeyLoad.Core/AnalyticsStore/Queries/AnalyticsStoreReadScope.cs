using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.AnalyticsStore;

internal sealed record AnalyticsStoreReadScope(PrincipalRecord Principal, ResourceDefinition Resource,
    ResourceLifetime Lifetime)
{
    private const long AbsentGeneration = 0;
    internal static AnalyticsStoreReadScope Open(DatabaseEngine database, IKeyValueView view,
        string principalId, PartitionRef partition, string store)
    {
        EngineResourceAccess.ValidatePartition(partition);
        JsonData.Identifier(store);
        var principal = database.Principal(view, principalId, database.EvaluationClock.GetUtcNow());
        database.Authorization.Require(principal, partition, store, Capability.AnalyticsRead);
        var resource = ResourceLifetimeReadBinding.BindAuthorizedReadResource(view, partition, store, ResourceKind.AnalyticsStore);
        AnalyticsStoreValidation.ValidateSchema(resource);
        var lifetime = ResourceLifetimeCatalog.Read(view, partition.TenantId, partition.DatabaseId, store, resource)
            ?? throw Errors.Fail(ErrorCode.Corruption, AnalyticsStoreValidation.Invalid);
        return new(principal, resource, lifetime);
    }

    internal static long Generation(IKeyValueView view, PartitionRef partition, string store)
    {
        var raw = view.ReadOwnedValue(AnalyticsStoreKeys.Generation(partition, store));
        var value = raw is null ? AbsentGeneration : OrleansSerialization.Deserialize<long>(raw);
        if (value < AbsentGeneration)
        {
            throw Errors.Fail(ErrorCode.Corruption, AnalyticsStoreValidation.Invalid);
        }

        return value;
    }
}
