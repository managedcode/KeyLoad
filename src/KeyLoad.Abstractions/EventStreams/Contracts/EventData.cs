namespace KeyLoad;

/// <summary>Carries event identity, type, payload, headers, and occurrence metadata.</summary>
/// <param name="EventId">Identifies the event.</param>
/// <param name="EventType">Names the event type.</param>
/// <param name="PayloadJson">Contains the optional message payload.</param>
/// <param name="HeadersJson">Contains optional JSON headers.</param>
/// <param name="SchemaVersion">Identifies the payload schema version.</param>
/// <param name="OccurredAt">Records the optional event occurrence time.</param>
/// <param name="CorrelationId">Carries the optional correlation identifier.</param>
/// <param name="CausationId">Carries the optional causation identifier.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.EventData)]
public sealed record EventData([property: Orleans.Id(0)] string EventId, [property: Orleans.Id(1)] string EventType, [property: Orleans.Id(2)] string PayloadJson, [property: Orleans.Id(3)] string HeadersJson = EventData.DefaultHeadersJson,
    [property: Orleans.Id(4)] int SchemaVersion = EventData.InitialSchemaVersion, [property: Orleans.Id(5)] DateTimeOffset? OccurredAt = null, [property: Orleans.Id(6)] string? CorrelationId = null, [property: Orleans.Id(7)] string? CausationId = null)
{
    private const string DefaultHeadersJson = "{}";
    private const int InitialSchemaVersion = 1;
}
