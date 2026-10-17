using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Returns one current-policy projection of retained saga state.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RecurringSagaContractAliases.SagaInspection)]
public sealed record SagaInspection(
    [property: global::Orleans.Id(0)] QueueLaneRef Lane,
    [property: global::Orleans.Id(1)] Guid SagaId,
    [property: global::Orleans.Id(2)] long Revision,
    [property: global::Orleans.Id(3)] SagaPhase Phase,
    [property: global::Orleans.Id(4)] string StateJson,
    [property: global::Orleans.Id(5)] DateTimeOffset? Deadline,
    [property: global::Orleans.Id(6)] bool Redacted,
    [property: global::Orleans.Id(7)] ImmutableArray<string> RedactedFields);
