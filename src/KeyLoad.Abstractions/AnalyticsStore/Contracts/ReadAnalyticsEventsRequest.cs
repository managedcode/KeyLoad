namespace KeyLoad;

/// <summary>Reads one bounded ID-ordered page over a half-open UTC event-time range.</summary>
/// <param name="Partition">Canonical atomic partition.</param>
/// <param name="Store">Independent analytics store.</param>
/// <param name="FromInclusive">Inclusive UTC event-time boundary.</param>
/// <param name="ToExclusive">Exclusive UTC event-time boundary.</param>
/// <param name="Limit">Positive page limit subject to server bounds.</param>
/// <param name="AfterId">Optional exclusive event-ID continuation, not a snapshot token.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AnalyticsStoreContractAliases.Read)]
public sealed record ReadAnalyticsEventsRequest(
    [property: Orleans.Id(0)] PartitionRef Partition,
    [property: Orleans.Id(1)] string Store,
    [property: Orleans.Id(2)] DateTimeOffset FromInclusive,
    [property: Orleans.Id(3)] DateTimeOffset ToExclusive,
    [property: Orleans.Id(4)] int Limit,
    [property: Orleans.Id(5)] string? AfterId);
