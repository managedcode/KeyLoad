using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>The complete admitted journal catalog for the initial RF3 authority group.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(RuntimeJournalAliases.Catalog)]
public sealed record RuntimeJournalCatalog(
    [property: Orleans.Id(0)] ImmutableArray<RuntimeJournalSnapshot> Journals);
