using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ResourceOwnerInventoryRequestV1 AdmitResourceOwnerInventoryRequest(string principalId, Guid inventoryId,
        string tenantId, string databaseId, string resourceName, int pageSize, DateTimeOffset expiresAt,
        ReadExecutionBudget work)
    {
        work.Check();
        ResourceOwnerInventoryReadValidation.Fields(inventoryId, tenantId, databaseId, resourceName, pageSize, Limits);
        // The native caller supplies the original authenticated expiry; this only shortens its original work.
        ResourceOwnerInventoryReadValidation.Expiry(expiresAt, EvaluationClock.GetUtcNow(), work);
        var result = Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            _ = RequireInventoryAdministrator(view, principalId);
            var (_, lifetime) = ReadInventoryResourcePair(view, tenantId, databaseId, resourceName, initial: true);
            var admitted = new ResourceOwnerInventoryRequestV1(ResourceOwnerInventoryReadValidation.Version,
                inventoryId, tenantId, databaseId, resourceName, lifetime.Incarnation, pageSize, expiresAt);
            ResourceOwnerInventoryReadValidation.Bound(admitted, Limits);
            return admitted;
        });
        work.CheckResult(result);
        work.Check();
        return result;
    }

    private static (ResourceDefinition Definition, ResourceLifetimeV1 Lifetime) ReadInventoryResourcePair(
        IKeyValueView view, string tenantId, string databaseId, string resourceName, bool initial)
    {
        var definition = view.GetRecord<ResourceDefinition>(KeySpace.Resource(tenantId, databaseId, resourceName));
        var lifetime = ResourceLifetimeCatalog.Read(view, tenantId, databaseId, resourceName, definition)
            ?? throw Errors.Fail(initial ? ErrorCode.NotFound : ErrorCode.TokenInvalidated,
                ResourceOwnerInventoryReadValidation.Changed);
        return (definition!, lifetime);
    }
}
