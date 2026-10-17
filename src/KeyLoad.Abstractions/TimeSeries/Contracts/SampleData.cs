namespace KeyLoad;

/// <summary>Carries a time-series sample and its event identity.</summary>
/// <param name="EventId">Identifies the event.</param>
/// <param name="Timestamp">Specifies the timestamp value.</param>
/// <param name="Value">Specifies the value value.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.SampleData)]
public sealed record SampleData([property: Orleans.Id(0)] string EventId, [property: Orleans.Id(1)] DateTimeOffset Timestamp, [property: Orleans.Id(2)] double Value);
