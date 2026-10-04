using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Stores bounded derived worker state using an exact prior snapshot version.</summary>
/// <param name="StreamSet">The configured event stream resource.</param>
/// <param name="StreamId">The canonical stream identity.</param>
/// <param name="SourceRevision">The final event revision incorporated into state.</param>
/// <param name="ReducerVersion">The exact worker reducer identifier.</param>
/// <param name="StateSchemaVersion">The positive state schema version.</param>
/// <param name="StateJson">The original valid JSON worker state.</param>
/// <param name="ExpectedSnapshotVersion">The prior version, or zero for an absent slot.</param>
/// <param name="Generation">The current stream generation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.StoreAggregateSnapshot)]
public sealed record StoreAggregateSnapshot([property: Orleans.Id(0)] string StreamSet,
    [property: Orleans.Id(1)] string StreamId, [property: Orleans.Id(2)] long SourceRevision,
    [property: Orleans.Id(3)] string ReducerVersion, [property: Orleans.Id(4)] int StateSchemaVersion,
    [property: Orleans.Id(5)] string StateJson, [property: Orleans.Id(6)] long ExpectedSnapshotVersion,
    [property: Orleans.Id(7)] long Generation = 1) : Mutation(StreamSet);

/// <summary>Contains an exact-versioned, checksummed latest aggregate snapshot.</summary>
/// <param name="Stream">The complete stream generation.</param>
/// <param name="SnapshotVersion">The monotone CAS version.</param>
/// <param name="SourceRevision">The final incorporated event revision.</param>
/// <param name="ReducerVersion">The exact worker reducer identifier.</param>
/// <param name="StateSchemaVersion">The positive state schema version.</param>
/// <param name="StateJson">The original valid JSON worker state.</param>
/// <param name="Checksum">SHA256 of the frozen snapshot identity.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.AggregateSnapshotState)]
public sealed record AggregateSnapshotState([property: Orleans.Id(0)] StreamRef Stream,
    [property: Orleans.Id(1)] long SnapshotVersion, [property: Orleans.Id(2)] long SourceRevision,
    [property: Orleans.Id(3)] string ReducerVersion, [property: Orleans.Id(4)] int StateSchemaVersion,
    [property: Orleans.Id(5)] string StateJson, [property: Orleans.Id(6)] string Checksum);

/// <summary>Requests a complete bounded replay slice under one authorized read cut.</summary>
/// <param name="Stream">The complete stream generation.</param>
/// <param name="ReducerVersion">The exact compatible worker reducer identifier.</param>
/// <param name="StateSchemaVersion">The expected positive state schema version.</param>
/// <param name="FromBeginning">Explicitly ignores the snapshot and requires full history.</param>
/// <param name="MaximumEvents">Maximum complete tail records, with no partial page.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReadAggregateReplayRequest)]
public sealed record ReadAggregateReplayRequest([property: Orleans.Id(0)] StreamRef Stream,
    [property: Orleans.Id(1)] string ReducerVersion, [property: Orleans.Id(2)] int StateSchemaVersion = 1,
    [property: Orleans.Id(3)] bool FromBeginning = false, [property: Orleans.Id(4)] int MaximumEvents = 100);

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
