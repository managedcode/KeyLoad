using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Contains a page of live query changes.</summary>
/// <param name="Changes">The changes in this page.</param>
/// <param name="Cursor">The continuation cursor.</param>
/// <param name="ThroughSequence">The final sequence included in this page.</param>
/// <param name="HasMore">Whether additional changes are available.</param>
/// <param name="CutPosition">The committed position at which the page was read.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.LiveQueryPage)]
public sealed record LiveQueryPage([property: Orleans.Id(0)] ImmutableArray<LiveQueryChange> Changes, [property: Orleans.Id(1)] string Cursor, [property: Orleans.Id(2)] long ThroughSequence, [property: Orleans.Id(3)] bool HasMore, [property: Orleans.Id(4)] long CutPosition);
