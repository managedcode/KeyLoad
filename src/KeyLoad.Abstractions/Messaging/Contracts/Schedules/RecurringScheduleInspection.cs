using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Returns one current-policy projection of a retained schedule.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RecurringSagaContractAliases.ScheduleInspection)]
public sealed record RecurringScheduleInspection(
    [property: global::Orleans.Id(0)] RecurringScheduleDefinition Definition,
    [property: global::Orleans.Id(1)] long Revision,
    [property: global::Orleans.Id(2)] long Generation,
    [property: global::Orleans.Id(3)] long NextOrdinal,
    [property: global::Orleans.Id(4)] bool Cancelled,
    [property: global::Orleans.Id(5)] bool Redacted,
    [property: global::Orleans.Id(6)] ImmutableArray<string> RedactedFields);
