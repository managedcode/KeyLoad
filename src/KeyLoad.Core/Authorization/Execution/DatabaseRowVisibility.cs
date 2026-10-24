using KeyLoad.Core.Authorization;

namespace KeyLoad.Core;
/// <summary>Executes DatabaseRowVisibility operations against the caller's database context.</summary>
public static class DatabaseRowVisibility
{
    internal const string AlreadyConfiguredVisibility = "The node visibility owner is already configured.";
    internal static RowVisibilityCache? ConfigureRowVisibility(this DatabaseEngine engine, ICacheMemoryBudget memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        if (engine.visibilityConfigured)
        {
            throw new InvalidOperationException(AlreadyConfiguredVisibility);
        }

        engine.visibilityConfigured = true;
        return engine.RowVisibility = RowVisibilityCache.Create(memory);
    }

    internal static RowVisibilityCurrentScope CaptureCurrentRowVisibility(this DatabaseEngine engine, PrincipalRecord principal, long schemaVersion, PartitionRef partition, string collection) => new(engine.Store.Identity.NodeId, engine.Store.Identity.Incarnation, engine.Store.Identity.ReadGeneration, engine.Store.Position, partition, collection, schemaVersion, principal.Id, principal.TenantId, principal.PolicyEpoch);
    internal static RowVisibilityScope CaptureRowVisibility(this DatabaseEngine engine, PrincipalRecord principal, ResourceDefinition resource, PartitionRef partition, string collection, long documentEpoch) => new(engine.Store.Identity.NodeId, engine.Store.Identity.Incarnation, engine.Store.Identity.ReadGeneration, engine.Store.Position, partition, collection, resource.SchemaVersion, documentEpoch, principal.Id, principal.TenantId, principal.PolicyEpoch);
}
