namespace KeyLoad;

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
