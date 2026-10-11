using KeyLoad.Core.Features.Authorization;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string AlreadyConfiguredVisibility = "The node visibility owner is already configured.";
    private bool visibilityConfigured;
    internal RowVisibilityCache? RowVisibility { get; private set; }

    internal RowVisibilityCache? ConfigureRowVisibility(ICacheMemoryBudget memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        if (visibilityConfigured)
        { throw new InvalidOperationException(AlreadyConfiguredVisibility); }
        visibilityConfigured = true;
        return RowVisibility = RowVisibilityCache.Create(memory);
    }

    internal RowVisibilityCurrentScope CaptureCurrentRowVisibility(PrincipalRecord principal,
        long schemaVersion, PartitionRef partition, string collection)
        => new(Store.Identity.NodeId, Store.Identity.Incarnation, Store.Identity.ReadGeneration,
            Store.Position, partition, collection, schemaVersion, principal.Id,
            principal.TenantId, principal.PolicyEpoch);

    internal RowVisibilityScope CaptureRowVisibility(PrincipalRecord principal,
        ResourceDefinition resource, PartitionRef partition, string collection, long documentEpoch)
        => new(Store.Identity.NodeId, Store.Identity.Incarnation, Store.Identity.ReadGeneration,
            Store.Position, partition, collection, resource.SchemaVersion, documentEpoch,
            principal.Id, principal.TenantId, principal.PolicyEpoch);
}
