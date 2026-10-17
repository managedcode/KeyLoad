using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Returns the exact-compatible snapshot and complete tail from one committed cut.</summary>
/// <param name="Stream">The complete stream generation.</param>
/// <param name="Head">The same-cut head and retained floor.</param>
/// <param name="Snapshot">The compatible latest snapshot, or null for complete history.</param>
/// <param name="Events">Every subsequent revision through the captured tail.</param>
/// <param name="CutPosition">The committed storage position under the read gate.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.AggregateReplayPage)]
public sealed record AggregateReplayPage([property: Orleans.Id(0)] StreamRef Stream,
    [property: Orleans.Id(1)] StreamHead Head, [property: Orleans.Id(2)] AggregateSnapshotState? Snapshot,
    [property: Orleans.Id(3)] ImmutableArray<EventRecord> Events, [property: Orleans.Id(4)] long CutPosition);
