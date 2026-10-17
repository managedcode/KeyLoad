namespace KeyLoad;

/// <summary>Represents one committed mutation retained in the system outbox.</summary>
/// <param name="Sequence">The monotonically increasing outbox sequence.</param>
/// <param name="Ordinal">The mutation's ordinal within its commit.</param>
/// <param name="Commit">The commit token for the mutation.</param>
/// <param name="CommittedAt">The time at which the commit was recorded.</param>
/// <param name="Mutation">The committed mutation.</param>
/// <param name="Receipt">The result of applying the mutation.</param>
/// <param name="Before">The prior document value when the mutation targets a document.</param>
/// <param name="After">The resulting document value when the mutation targets a document.</param>
/// <remarks>The system outbox is separate from business topics, event streams, and the private redo journal.</remarks>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.OutboxEntry)]
public sealed record OutboxEntry([property: Orleans.Id(0)] long Sequence, [property: Orleans.Id(1)] int Ordinal, [property: Orleans.Id(2)] CommitToken Commit, [property: Orleans.Id(3)] DateTimeOffset CommittedAt,
    [property: Orleans.Id(4)] Mutation Mutation, [property: Orleans.Id(5)] MutationReceipt Receipt, [property: Orleans.Id(6)] DocumentRecord? Before = null, [property: Orleans.Id(7)] DocumentRecord? After = null);
