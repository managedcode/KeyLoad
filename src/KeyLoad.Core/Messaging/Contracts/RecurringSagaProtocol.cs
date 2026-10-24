namespace KeyLoad.Core.Messaging;

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
