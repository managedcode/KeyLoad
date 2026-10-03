using System.Collections.Immutable;

namespace KeyLoad.Client;

internal static class AggregateReplayValidation
{
    internal static void ValidateLimits(AggregateReplayWorkerLimits limits)
    {
        if (limits.MaximumEvents is <= 0 or > 65_536 ||
            limits.MaximumStateBytes is <= 0 or > 16_777_216 ||
            limits.MaximumInputBytes is <= 0 or > 67_108_864 ||
            limits.MaximumJsonDepth is <= 0 or > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "Replay worker limits must be positive and within their hard ceilings.");
        }
    }

    internal static void ValidateReducer(AggregateReplayReducer reducer)
    {
        ArgumentNullException.ThrowIfNull(reducer);
        if (string.IsNullOrWhiteSpace(reducer.Version) || reducer.StateSchemaVersion <= 0 ||
            reducer.EventSchemaVersion <= 0 || reducer.Apply is null)
        {
            throw new ArgumentException("The reducer identity, schema versions and callback must be valid.", nameof(reducer));
        }
    }

    internal static ValidatedReplay ValidatePage(
        AggregateReplayPage page,
        AggregateReplayReducer reducer,
        IEnumerable<EventUpcaster>? upcasters,
        AggregateReplayWorkerLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        ValidatePageIdentity(page);
        var input = new AggregateReplayInput(limits.MaximumInputBytes, limits.MaximumJsonDepth);
        var initialState = page.Snapshot is { } snapshot
            ? ValidateSnapshot(page, snapshot, reducer, limits, input)
            : ValidateInitialState(page, reducer, limits, input);
        var upcasterMap = AggregateReplayUpcast.BuildMap(upcasters, cancellationToken);
        var paths = ValidateEvents(page, reducer, upcasterMap, limits, input, cancellationToken);
        return new(initialState, paths);
    }

    private static string ValidateInitialState(
        AggregateReplayPage page,
        AggregateReplayReducer reducer,
        AggregateReplayWorkerLimits limits,
        AggregateReplayInput input)
    {
        if (page.Head.FirstAvailableRevision != 1)
        {
            throw new InvalidDataException("A snapshot-free replay must contain complete retained history.");
        }
        input.AddState(reducer.InitialStateJson, "Initial state", limits);
        return reducer.InitialStateJson;
    }

    private static string ValidateSnapshot(
        AggregateReplayPage page,
        AggregateSnapshotState snapshot,
        AggregateReplayReducer reducer,
        AggregateReplayWorkerLimits limits,
        AggregateReplayInput input)
    {
        if (snapshot.SnapshotVersion <= 0 || snapshot.SourceRevision < 0 ||
            snapshot.SourceRevision > page.Head.TailRevision ||
            snapshot.SourceRevision < page.Head.FirstAvailableRevision - 1 ||
            snapshot.StateSchemaVersion <= 0 ||
            !string.Equals(snapshot.ReducerVersion, reducer.Version, StringComparison.Ordinal) ||
            snapshot.StateSchemaVersion != reducer.StateSchemaVersion)
        {
            throw new InvalidDataException("The snapshot is incompatible with the reducer or replay head.");
        }
        input.AddState(snapshot.StateJson, "Snapshot state", limits);
        return snapshot.StateJson;
    }

    private static void ValidatePageIdentity(AggregateReplayPage page)
    {
        if (page.Stream is null || page.Stream.Partition is null || page.Head is null ||
            string.IsNullOrWhiteSpace(page.Stream.StreamSet) || string.IsNullOrWhiteSpace(page.Stream.StreamId) ||
            page.Stream.Generation <= 0 || page.Head.Generation != page.Stream.Generation ||
            page.Head.TailRevision < 0 || page.Head.FirstAvailableRevision <= 0 ||
            page.Head.FirstAvailableRevision - 1 > page.Head.TailRevision || page.CutPosition < 0)
        {
            throw new InvalidDataException("Replay stream identity, head, floor or cut is invalid.");
        }
        if (page.Events.IsDefault)
        {
            throw new InvalidDataException("Replay events must be an initialized immutable array.");
        }
        if (page.Snapshot is { } snapshot && snapshot.Stream != page.Stream)
        {
            throw new InvalidDataException("The snapshot belongs to another stream generation.");
        }
    }

    private static Dictionary<int, ImmutableArray<EventUpcaster>> ValidateEvents(
        AggregateReplayPage page,
        AggregateReplayReducer reducer,
        IReadOnlyDictionary<int, EventUpcaster> upcasters,
        AggregateReplayWorkerLimits limits,
        AggregateReplayInput input,
        CancellationToken cancellationToken)
    {
        if (page.Events.Length > limits.MaximumEvents)
        {
            throw new InvalidDataException("The replay event count exceeds the worker limit.");
        }
        var sourceRevision = page.Snapshot?.SourceRevision ?? 0;
        if (sourceRevision > page.Head.TailRevision || page.Head.TailRevision - sourceRevision != page.Events.Length)
        {
            throw new InvalidDataException("Replay must contain the complete tail through the captured head.");
        }
        Dictionary<int, ImmutableArray<EventUpcaster>> paths = [];
        HashSet<string> eventIds = new(StringComparer.Ordinal);
        long priorSequence = 0;
        for (var index = 0; index < page.Events.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = page.Events[index] ?? throw new InvalidDataException("Replay contains a null event.");
            ValidateEventIdentity(record, page, sourceRevision, index, priorSequence, eventIds);
            input.AddJson(record.Data.PayloadJson, "Event payload");
            input.AddJson(record.Data.HeadersJson, "Event headers");
            if (!paths.ContainsKey(record.Data.SchemaVersion))
            {
                paths.Add(record.Data.SchemaVersion,
                    AggregateReplayUpcast.ResolvePath(record.Data.SchemaVersion, reducer.EventSchemaVersion, upcasters));
            }
            priorSequence = record.EventSequence;
        }
        return paths;
    }

    private static void ValidateEventIdentity(
        EventRecord record,
        AggregateReplayPage page,
        long sourceRevision,
        int index,
        long priorSequence,
        HashSet<string> eventIds)
    {
        var data = record.Data;
        if (record.Stream != page.Stream || record.Revision != sourceRevision + index + 1 ||
            record.EventSequence <= priorSequence || record.EventSequence <= 0 ||
            data is null || string.IsNullOrWhiteSpace(data.EventId) || string.IsNullOrWhiteSpace(data.EventType) ||
            data.SchemaVersion <= 0 || !eventIds.Add(data.EventId))
        {
            throw new InvalidDataException("Replay event identity, position or ordering is invalid.");
        }
    }

    internal sealed record ValidatedReplay(
        string InitialState,
        Dictionary<int, ImmutableArray<EventUpcaster>> Paths);
}
