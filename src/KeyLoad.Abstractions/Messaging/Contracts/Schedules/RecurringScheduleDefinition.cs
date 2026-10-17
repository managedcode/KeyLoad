namespace KeyLoad;

/// <summary>Describes one UTC fixed-interval message schedule.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RecurringSagaContractAliases.ScheduleDefinition)]
public sealed record RecurringScheduleDefinition(
    [property: global::Orleans.Id(0)] QueueLaneRef Lane,
    [property: global::Orleans.Id(1)] Guid ScheduleId,
    [property: global::Orleans.Id(2)] DateTimeOffset FirstDueAt,
    [property: global::Orleans.Id(3)] TimeSpan Interval,
    [property: global::Orleans.Id(4)] string TimeZone,
    [property: global::Orleans.Id(5)] RecurringMisfirePolicy Misfire,
    [property: global::Orleans.Id(6)] string PayloadJson,
    [property: global::Orleans.Id(7)] string HeadersJson,
    [property: global::Orleans.Id(8)] string? OrderingKey = null,
    [property: global::Orleans.Id(9)] TimeSpan? MessageTimeToLive = null);
