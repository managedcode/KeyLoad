namespace KeyLoad.Core.Features.Messaging;

internal static class RecurringSagaProtocol
{
    internal const long MaximumMessageTimeToLiveTicks = 365L * TimeSpan.TicksPerDay;
    internal const string ScheduleSpace = "recurring-schedule";
    internal const string SagaSpace = "saga-state";
    internal const string CapacitySpace = "recurring-saga-capacity";
    internal const string ScheduleReceipt = "configureRecurringSchedule";
    internal const string EmitReceipt = "emitRecurringOccurrences";
    internal const string CancelReceipt = "cancelRecurringSchedule";
    internal const string SagaReceipt = "compareExchangeSaga";
    internal const string TimeoutReceipt = "expireSaga";
    internal const string SchedulePrefix = "recurring-";
    internal const string SagaTimeoutPrefix = "saga-timeout-";
    internal const string IdentifierFormat = "N";
    internal const string HexOrdinalFormat = "x16";
    internal const string UtcZone = "UTC";
    internal const string InvalidRequest = "The recurring schedule or saga request is invalid.";
    internal const string RevisionConflict = "The expected schedule or saga revision does not match.";
    internal const string GenerationChanged = "The recurring schedule generation changed.";
    internal const string MissingSchedule = "The recurring schedule is unavailable.";
    internal const string MissingSaga = "The saga is unavailable.";
    internal const string CorruptRecord = "Persisted recurring schedule or saga state is inconsistent.";
    internal const string CapacityExhausted = "The retained recurring schedule and saga capacity is exhausted.";
    internal const string NotDue = "The saga deadline has not been reached.";
}

internal static class RecurringSagaAliases
{
    internal const string Schedule = "keyload.core.recurring-schedule-record.v1";
    internal const string Saga = "keyload.core.saga-record.v1";
    internal const string Capacity = "keyload.core.recurring-saga-capacity.v1";
}

internal static class RecurringSagaFields
{
    internal const int Lane = 0;
    internal const int Identity = 1;
    internal const int CreatorPrincipalId = 2;
    internal const int Definition = 3;
    internal const int Revision = 4;
    internal const int Generation = 5;
    internal const int NextOrdinal = 6;
    internal const int Cancelled = 7;
    internal const int Phase = 8;
    internal const int StateJson = 9;
    internal const int Deadline = 10;
    internal const int Timeout = 11;
    internal const int Records = 12;
    internal const int Bytes = 13;
}

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RecurringSagaAliases.Schedule)]
internal sealed record RecurringScheduleRecord(
    [property: global::Orleans.Id(RecurringSagaFields.Lane)] QueueLaneRef Lane,
    [property: global::Orleans.Id(RecurringSagaFields.Identity)] Guid ScheduleId,
    [property: global::Orleans.Id(RecurringSagaFields.CreatorPrincipalId)] string CreatorPrincipalId,
    [property: global::Orleans.Id(RecurringSagaFields.Definition)] RecurringScheduleDefinition Definition,
    [property: global::Orleans.Id(RecurringSagaFields.Revision)] long Revision,
    [property: global::Orleans.Id(RecurringSagaFields.Generation)] long Generation,
    [property: global::Orleans.Id(RecurringSagaFields.NextOrdinal)] long NextOrdinal,
    [property: global::Orleans.Id(RecurringSagaFields.Cancelled)] bool Cancelled);

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

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RecurringSagaAliases.Capacity)]
internal sealed record RecurringSagaCapacity(
    [property: global::Orleans.Id(RecurringSagaFields.Records)] long Records,
    [property: global::Orleans.Id(RecurringSagaFields.Bytes)] long Bytes);
