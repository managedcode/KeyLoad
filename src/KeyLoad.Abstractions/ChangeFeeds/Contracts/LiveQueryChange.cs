namespace KeyLoad;

/// <summary>Describes one change to a live query result.</summary>
/// <param name="Sequence">The outbox sequence of the change.</param>
/// <param name="Commit">The commit token for the change.</param>
/// <param name="Kind">Whether the result row was added/updated or removed.</param>
/// <param name="Reference">The changed entity reference.</param>
/// <param name="Revision">The changed entity revision.</param>
/// <param name="Row">The resulting query row when the kind is <see cref="LiveQueryChangeKind.Upsert"/>.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.LiveQueryChange)]
public sealed record LiveQueryChange([property: Orleans.Id(0)] long Sequence, [property: Orleans.Id(1)] CommitToken Commit, [property: Orleans.Id(2)] LiveQueryChangeKind Kind, [property: Orleans.Id(3)] EntityRef Reference,
    [property: Orleans.Id(4)] long Revision, [property: Orleans.Id(5)] QueryRow? Row);
