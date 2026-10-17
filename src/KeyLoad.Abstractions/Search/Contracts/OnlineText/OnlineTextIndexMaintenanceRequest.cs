namespace KeyLoad;

/// <summary>Maintains an online generation using an existing administrator-configured canonical consumer.</summary>
/// <param name="CommandId">CommandId within the original authorized maintenance request.</param>
/// <param name="Consumer">Consumer within the original authorized maintenance request.</param>
/// <param name="Collection">Collection within the original authorized maintenance request.</param>
/// <param name="Field">Field within the original authorized maintenance request.</param>
/// <param name="ConsumerGeneration">ConsumerGeneration within the original authorized maintenance request.</param>
/// <param name="NodeId">NodeId within the original authorized maintenance request.</param>
/// <param name="Placement">Placement within the original authorized maintenance request.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(OnlineTextIndexMaintenanceProtocol.RequestAlias)]
public sealed record OnlineTextIndexMaintenanceRequest(
    [property: global::Orleans.Id(0)] Guid CommandId,
    [property: global::Orleans.Id(1)] ProjectionConsumerRef Consumer,
    [property: global::Orleans.Id(2)] string Collection,
    [property: global::Orleans.Id(3)] string Field,
    [property: global::Orleans.Id(4)] long ConsumerGeneration,
    [property: global::Orleans.Id(5)] Guid NodeId,
    [property: global::Orleans.Id(6)] PhysicalShardRecord Placement);
