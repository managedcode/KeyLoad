namespace KeyLoad;

/// <summary>Addresses one explicit bounded administrator-owned text-index operation.</summary>
/// <param name="CommandId">Immutable parent identity; retain the entire request when reconciling uncertainty.</param>
/// <param name="Consumer">Exact persisted projection consumer and atomic partition.</param>
/// <param name="Collection">Canonical text source collection.</param>
/// <param name="Field">Exact canonical text field.</param>
/// <param name="IndexGeneration">Positive persisted consumer generation.</param>
/// <param name="NodeId">Actual node-local storage owner.</param>
/// <param name="Placement">Exact persisted physical placement witness.</param>
/// <param name="Mode">Explicit construction, incremental recovery or release.</param>
[Orleans.GenerateSerializer, Orleans.Alias(TextIndexMaintenanceAliases.Request)]
public sealed record TextIndexMaintenanceRequest(
    [property: Orleans.Id(0)] Guid CommandId,
    [property: Orleans.Id(1)] ProjectionConsumerRef Consumer,
    [property: Orleans.Id(2)] string Collection,
    [property: Orleans.Id(3)] string Field,
    [property: Orleans.Id(4)] long IndexGeneration,
    [property: Orleans.Id(5)] Guid NodeId,
    [property: Orleans.Id(6)] PhysicalShardRecord Placement,
    [property: Orleans.Id(7)] TextIndexMaintenanceMode Mode);
