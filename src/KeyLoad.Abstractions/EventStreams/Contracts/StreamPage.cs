using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Returns a bounded event page with the observed head and committed cut.</summary>
/// <param name="Stream">Specifies the stream value.</param>
/// <param name="Head">Reports the stream head observed for this page.</param>
/// <param name="Events">Lists events to append.</param>
/// <param name="CutPosition">Identifies the committed read cut.</param>
/// <param name="HasMore">Indicates whether more events are available.</param>
/// <param name="Cursor">The signed continuation for the same captured traversal, or null when exhausted.</param>
/// <param name="SnapshotCutPosition">The original committed cut captured at the start of this traversal.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.StreamPage)]
public sealed record StreamPage([property: Orleans.Id(0)] StreamRef Stream, [property: Orleans.Id(1)] StreamHead Head, [property: Orleans.Id(2)] ImmutableArray<EventRecord> Events, [property: Orleans.Id(3)] long CutPosition, [property: Orleans.Id(4)] bool HasMore,
    [property: Orleans.Id(5)] string? Cursor = null,
    [property: Orleans.Id(6)] long SnapshotCutPosition = StreamPage.EmptySnapshotCut)
{
    private const long EmptySnapshotCut = 0L;
}
