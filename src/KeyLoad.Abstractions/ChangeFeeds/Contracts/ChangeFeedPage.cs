using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Contains a page of collection changes and retention metadata.</summary>
/// <param name="Changes">The changes in this page.</param>
/// <param name="Cursor">The continuation cursor.</param>
/// <param name="ThroughSequence">The final sequence included in this page.</param>
/// <param name="Tail">The current outbox tail sequence.</param>
/// <param name="FirstAvailable">The earliest retained sequence.</param>
/// <param name="HasMore">Whether additional changes are available.</param>
/// <param name="CutPosition">The committed position at which the page was read.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ChangeFeedPage)]
public sealed record ChangeFeedPage([property: Orleans.Id(0)] ImmutableArray<DocumentChange> Changes, [property: Orleans.Id(1)] string Cursor, [property: Orleans.Id(2)] long ThroughSequence, [property: Orleans.Id(3)] long Tail,
    [property: Orleans.Id(4)] long FirstAvailable, [property: Orleans.Id(5)] bool HasMore, [property: Orleans.Id(6)] long CutPosition);
