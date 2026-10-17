namespace KeyLoad;

/// <summary>Revises or creates a durable recurring schedule with compare-exchange.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RecurringSagaContractAliases.ConfigureSchedule)]
public sealed record ConfigureRecurringSchedule(
    [property: global::Orleans.Id(0)] RecurringScheduleDefinition Definition,
    [property: global::Orleans.Id(1)] long ExpectedRevision) : Mutation(Definition?.Lane?.Queue!);
