namespace KeyLoad.Orleans;

/// <summary>Correlates the current native request with its stable command without carrying authority or payloads.</summary>
/// <param name="RequestId">The fresh request actor GUID.</param>
/// <param name="CommandId">The stable write GUID, or empty for a read.</param>
/// <param name="ConnectionId"></param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(GrainRoutingProtocol.ContextAlias)]
public sealed record GrainRequestContextState(
    [property: global::Orleans.Id(0)] Guid RequestId,
    [property: global::Orleans.Id(1)] Guid CommandId,
    [property: global::Orleans.Id(2)] Guid ConnectionId = default);
