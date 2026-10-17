namespace KeyLoad;

/// <summary>Requests authorized inspection of one retained saga.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RecurringSagaContractAliases.InspectSagaRequest)]
public sealed record InspectSagaRequest(
    [property: global::Orleans.Id(0)] QueueLaneRef Lane,
    [property: global::Orleans.Id(1)] Guid SagaId);
