namespace KeyLoad;

/// <summary>Defines the one queue message emitted by a saga timeout.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RecurringSagaContractAliases.SagaTimeout)]
public sealed record SagaTimeoutDefinition(
    [property: global::Orleans.Id(0)] QueueLaneRef Queue,
    [property: global::Orleans.Id(1)] string PayloadJson,
    [property: global::Orleans.Id(2)] string HeadersJson,
    [property: global::Orleans.Id(3)] string? OrderingKey = null,
    [property: global::Orleans.Id(4)] TimeSpan? TimeToLive = null);
