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
