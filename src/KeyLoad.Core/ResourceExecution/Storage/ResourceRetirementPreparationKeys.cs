using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceRetirementPreparationKeys
{
    internal const string StateSpace = "resource-retirement-preparation-v1";
    internal const string OwnerSpace = "resource-retirement-reservation-v1";
    internal const string SourceActive = "resource-retirement-preparation-active-v1";
    internal const string OwnerActive = "resource-retirement-reservation-active-v1";
    private const string GlobalCount = "resource-retirement-preparing-global-v1";
    private const string DatabaseCount = "resource-retirement-preparing-database-v1";

    internal static byte[] Life(string space, ResourceLifetimeV1 lifetime)
        => KeyCodec.Encode(space, lifetime.TenantId, lifetime.DatabaseId, lifetime.ResourceName, lifetime.Incarnation.Value);
    internal static byte[] Global() => KeyCodec.Encode(GlobalCount);
    internal static byte[] Database(string tenant, string database) => KeyCodec.Encode(DatabaseCount, tenant, database);
}
