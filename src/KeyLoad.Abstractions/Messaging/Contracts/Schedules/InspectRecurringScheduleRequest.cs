namespace KeyLoad;

/// <summary>Requests authorized inspection of one recurring schedule.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RecurringSagaContractAliases.InspectScheduleRequest)]
public sealed record InspectRecurringScheduleRequest(
    [property: global::Orleans.Id(0)] QueueLaneRef Lane,
    [property: global::Orleans.Id(1)] Guid ScheduleId);
