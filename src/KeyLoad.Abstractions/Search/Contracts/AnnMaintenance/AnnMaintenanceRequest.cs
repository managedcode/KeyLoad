namespace KeyLoad;

/// <summary>Addresses a bounded admin operation; this parent has no atomic group receipt.</summary>
/// <param name="CommandId">Stable parent identity used for distinct canonical child identities.</param>
/// <param name="Consumer">Exact persisted projection consumer and atomic partition.</param>
/// <param name="Collection">Canonical vector source collection.</param>
/// <param name="Field">Canonical vector source field.</param>
/// <param name="Space">Exact model, version, dimension and metric.</param>
/// <param name="IndexGeneration">Explicit positive generation of the active consumer.</param>
/// <param name="NodeId">Exact opaque node-local storage owner.</param>
/// <param name="Placement">Exact persisted physical shard placement witness.</param>
/// <param name="Mode">Explicit build, restore or release; restore never rebuilds instead of loading.</param>
[Orleans.GenerateSerializer, Orleans.Alias(AnnMaintenanceAliases.Request)]
public sealed record AnnMaintenanceRequest(
    [property: Orleans.Id(0)] Guid CommandId,
    [property: Orleans.Id(1)] ProjectionConsumerRef Consumer,
    [property: Orleans.Id(2)] string Collection,
    [property: Orleans.Id(3)] string Field,
    [property: Orleans.Id(4)] VectorSpace Space,
    [property: Orleans.Id(5)] long IndexGeneration,
    [property: Orleans.Id(6)] Guid NodeId,
    [property: Orleans.Id(7)] PhysicalShardRecord Placement,
    [property: Orleans.Id(8)] AnnMaintenanceMode Mode)
{
    /// <summary>Gets the optional immutable committed-vector maintenance profile bound to this request.</summary>
    [Orleans.Id(9), System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public AnnProjectionMaintenanceProfile? MaintenanceProfile
    {
        get; init;
    }
}
