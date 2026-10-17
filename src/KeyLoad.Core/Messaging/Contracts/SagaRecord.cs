namespace KeyLoad.Core.Features.Messaging;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RecurringSagaAliases.Saga)]
internal sealed record SagaRecord(
    [property: global::Orleans.Id(RecurringSagaFields.Lane)] QueueLaneRef Lane,
    [property: global::Orleans.Id(RecurringSagaFields.Identity)] Guid SagaId,
    [property: global::Orleans.Id(RecurringSagaFields.CreatorPrincipalId)] string CreatorPrincipalId,
    [property: global::Orleans.Id(RecurringSagaFields.Revision)] long Revision,
    [property: global::Orleans.Id(RecurringSagaFields.Phase)] SagaPhase Phase,
    [property: global::Orleans.Id(RecurringSagaFields.StateJson)] string StateJson,
    [property: global::Orleans.Id(RecurringSagaFields.Deadline)] DateTimeOffset? Deadline,
    [property: global::Orleans.Id(RecurringSagaFields.Timeout)] SagaTimeoutDefinition? Timeout);
