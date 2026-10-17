namespace KeyLoad;

/// <summary>Contains one dense half-open UTC window and its complete statistics.</summary>
/// <param name="From">Inclusive UTC beginning.</param>
/// <param name="UntilExclusive">Exclusive UTC end, or null only after the greatest representable tick.</param>
/// <param name="Aggregate">Raw statistics for this window.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.SampleAggregateWindow)]
public sealed record SampleAggregateWindow([property: Orleans.Id(0)] DateTimeOffset From, [property: Orleans.Id(1)] DateTimeOffset? UntilExclusive, [property: Orleans.Id(2)] SampleAggregate Aggregate);
