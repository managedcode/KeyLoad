namespace KeyLoad;

/// <summary>The canonical outcome used to resolve an uncertain journal mutation.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(RuntimeJournalAliases.Result)]
public sealed record RuntimeJournalMutationResult(
    [property: Orleans.Id(0)] bool Applied,
    [property: Orleans.Id(1)] RuntimeJournalSnapshot? Snapshot);
