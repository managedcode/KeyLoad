namespace KeyLoad;

/// <summary>Specifies the finite recurrence behavior supported by a schedule.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RecurringSagaContractAliases.MisfirePolicy)]
public enum RecurringMisfirePolicy
{
    /// <summary>Emits due occurrences contiguously, retaining excess backlog.</summary>
    CatchUp
}
