using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Separates exact retired-lifetime history from the reusable resource-name successor.</summary>
internal static class ResourceRetirementFinalizationKeys
{
    private const string HistorySpace = "resource-retirement-finalization-v1";
    private const string LatestSpace = "resource-retirement-finalization-latest-v1";

    internal static byte[] History(ResourceLifetimeV1 lifetime)
        => KeyCodec.Encode(HistorySpace, lifetime.TenantId, lifetime.DatabaseId,
            lifetime.ResourceName, lifetime.Incarnation.Value);

    internal static byte[] Latest(string tenantId, string databaseId, string resourceName)
        => KeyCodec.Encode(LatestSpace, tenantId, databaseId, resourceName);
}
