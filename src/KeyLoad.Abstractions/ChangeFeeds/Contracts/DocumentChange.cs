namespace KeyLoad;

/// <summary>Describes a document change recorded in the system outbox.</summary>
/// <param name="Sequence">The outbox sequence of the change.</param>
/// <param name="Commit">The commit token for the change.</param>
/// <param name="CommittedAt">The time at which the change committed.</param>
/// <param name="Reference">The changed document reference.</param>
/// <param name="Revision">The resulting document revision.</param>
/// <param name="Deleted">Whether the document was deleted.</param>
/// <param name="Before">The prior document result, when retained and visible.</param>
/// <param name="After">The resulting document value, when present and visible.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.DocumentChange)]
public sealed record DocumentChange([property: Orleans.Id(0)] long Sequence, [property: Orleans.Id(1)] CommitToken Commit, [property: Orleans.Id(2)] DateTimeOffset CommittedAt,
    [property: Orleans.Id(3)] EntityRef Reference, [property: Orleans.Id(4)] long Revision, [property: Orleans.Id(5)] bool Deleted, [property: Orleans.Id(6)] DocumentResult? Before, [property: Orleans.Id(7)] DocumentResult? After);
