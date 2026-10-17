using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Contains a page of events and source continuation metadata.</summary>
/// <param name="Source">The source that was read.</param>
/// <param name="Head">The source head observed for the page.</param>
/// <param name="Events">The events in this page.</param>
/// <param name="Cursor">The continuation cursor.</param>
/// <param name="CutPosition">The committed position at which this page was read.</param>
/// <param name="HasMore">Whether additional events are available.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.EventSourcePage)]
public sealed record EventSourcePage([property: Orleans.Id(0)] EventSourceRef Source, [property: Orleans.Id(1)] EventSourceHead Head, [property: Orleans.Id(2)] ImmutableArray<SourceEventRecord> Events, [property: Orleans.Id(3)] string Cursor,
    [property: Orleans.Id(4)] long CutPosition, [property: Orleans.Id(5)] bool HasMore);
