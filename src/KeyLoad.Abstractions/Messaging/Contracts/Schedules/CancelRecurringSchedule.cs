namespace KeyLoad;

/// <summary>Cancels one schedule revision without discarding its occurrence watermark.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RecurringSagaContractAliases.CancelSchedule)]
public sealed record CancelRecurringSchedule(
    [property: global::Orleans.Id(0)] QueueLaneRef Lane,
    [property: global::Orleans.Id(1)] Guid ScheduleId,
    [property: global::Orleans.Id(2)] long ExpectedRevision) : Mutation(Lane?.Queue!);
