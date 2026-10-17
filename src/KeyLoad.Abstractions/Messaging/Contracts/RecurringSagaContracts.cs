using System.Collections.Immutable;

namespace KeyLoad;

internal static class RecurringSagaContractAliases
{
    internal const string ScheduleDefinition = "keyload.contract.recurring-schedule-definition.v1";
    internal const string MisfirePolicy = "keyload.contract.recurring-misfire-policy.v1";
    internal const string ConfigureSchedule = "keyload.contract.configure-recurring-schedule.v1";
    internal const string EmitOccurrences = "keyload.contract.emit-recurring-occurrences.v1";
    internal const string CancelSchedule = "keyload.contract.cancel-recurring-schedule.v1";
    internal const string SagaTimeout = "keyload.contract.saga-timeout-definition.v1";
    internal const string SagaPhase = "keyload.contract.saga-phase.v1";
    internal const string CompareExchangeSaga = "keyload.contract.compare-exchange-saga.v1";
    internal const string ExpireSaga = "keyload.contract.expire-saga.v1";
    internal const string InspectScheduleRequest = "keyload.contract.inspect-recurring-schedule-request.v1";
    internal const string ScheduleInspection = "keyload.contract.recurring-schedule-inspection.v1";
    internal const string InspectSagaRequest = "keyload.contract.inspect-saga-request.v1";
    internal const string SagaInspection = "keyload.contract.saga-inspection.v1";
}

/// <summary>Specifies the finite recurrence behavior supported by a schedule.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RecurringSagaContractAliases.MisfirePolicy)]
public enum RecurringMisfirePolicy
{
    /// <summary>Emits due occurrences contiguously, retaining excess backlog.</summary>
    CatchUp
}

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

/// <summary>Revises or creates a durable recurring schedule with compare-exchange.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RecurringSagaContractAliases.ConfigureSchedule)]
public sealed record ConfigureRecurringSchedule(
    [property: global::Orleans.Id(0)] RecurringScheduleDefinition Definition,
    [property: global::Orleans.Id(1)] long ExpectedRevision) : Mutation(Definition?.Lane?.Queue!);

/// <summary>Emits a bounded contiguous prefix of due recurring occurrences.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RecurringSagaContractAliases.EmitOccurrences)]
public sealed record EmitRecurringOccurrences(
    [property: global::Orleans.Id(0)] QueueLaneRef Lane,
    [property: global::Orleans.Id(1)] Guid ScheduleId,
    [property: global::Orleans.Id(2)] long ExpectedGeneration,
    [property: global::Orleans.Id(3)] int MaxOccurrences = EmitRecurringOccurrences.DefaultMaxOccurrences) : Mutation(Lane?.Queue!)
{
    private const int DefaultMaxOccurrences = 1;
}

/// <summary>Cancels one schedule revision without discarding its occurrence watermark.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RecurringSagaContractAliases.CancelSchedule)]
public sealed record CancelRecurringSchedule(
    [property: global::Orleans.Id(0)] QueueLaneRef Lane,
    [property: global::Orleans.Id(1)] Guid ScheduleId,
    [property: global::Orleans.Id(2)] long ExpectedRevision) : Mutation(Lane?.Queue!);

/// <summary>Defines the one queue message emitted by a saga timeout.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RecurringSagaContractAliases.SagaTimeout)]
public sealed record SagaTimeoutDefinition(
    [property: global::Orleans.Id(0)] QueueLaneRef Queue,
    [property: global::Orleans.Id(1)] string PayloadJson,
    [property: global::Orleans.Id(2)] string HeadersJson,
    [property: global::Orleans.Id(3)] string? OrderingKey = null,
    [property: global::Orleans.Id(4)] TimeSpan? TimeToLive = null);

/// <summary>Identifies the only saga transitions supported by this protocol.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RecurringSagaContractAliases.SagaPhase)]
public enum SagaPhase
{
    /// <summary>The saga may be revised or completed before its deadline.</summary>
    Waiting,
    /// <summary>The saga completed through an explicit revision transition.</summary>
    Completed,
    /// <summary>The saga was cancelled through an explicit revision transition.</summary>
    Cancelled,
    /// <summary>The expected waiting revision expired and committed its timeout message.</summary>
    TimedOut
}

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

/// <summary>Atomically times out one due waiting saga revision and enqueues its timeout message.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RecurringSagaContractAliases.ExpireSaga)]
public sealed record ExpireSaga(
    [property: global::Orleans.Id(0)] QueueLaneRef Lane,
    [property: global::Orleans.Id(1)] Guid SagaId,
    [property: global::Orleans.Id(2)] long ExpectedRevision) : Mutation(Lane?.Queue!);

/// <summary>Requests authorized inspection of one recurring schedule.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RecurringSagaContractAliases.InspectScheduleRequest)]
public sealed record InspectRecurringScheduleRequest(
    [property: global::Orleans.Id(0)] QueueLaneRef Lane,
    [property: global::Orleans.Id(1)] Guid ScheduleId);

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

/// <summary>Requests authorized inspection of one retained saga.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RecurringSagaContractAliases.InspectSagaRequest)]
public sealed record InspectSagaRequest(
    [property: global::Orleans.Id(0)] QueueLaneRef Lane,
    [property: global::Orleans.Id(1)] Guid SagaId);

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
