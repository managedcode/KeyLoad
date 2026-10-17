namespace KeyLoad;

/// <summary>Creates or advances one same-partition saga revision.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RecurringSagaContractAliases.CompareExchangeSaga)]
public sealed record CompareExchangeSaga(
    [property: global::Orleans.Id(0)] QueueLaneRef Lane,
    [property: global::Orleans.Id(1)] Guid SagaId,
    [property: global::Orleans.Id(2)] long ExpectedRevision,
    [property: global::Orleans.Id(3)] SagaPhase Phase,
    [property: global::Orleans.Id(4)] string StateJson,
    [property: global::Orleans.Id(5)] DateTimeOffset? Deadline = null,
    [property: global::Orleans.Id(6)] SagaTimeoutDefinition? Timeout = null) : Mutation(Lane?.Queue!);
