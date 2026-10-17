namespace KeyLoad;

/// <summary>Atomically times out one due waiting saga revision and enqueues its timeout message.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RecurringSagaContractAliases.ExpireSaga)]
public sealed record ExpireSaga(
    [property: global::Orleans.Id(0)] QueueLaneRef Lane,
    [property: global::Orleans.Id(1)] Guid SagaId,
    [property: global::Orleans.Id(2)] long ExpectedRevision) : Mutation(Lane?.Queue!);
