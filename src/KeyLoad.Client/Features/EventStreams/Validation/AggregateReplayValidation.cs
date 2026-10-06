using System.Collections.Immutable;

namespace KeyLoad.Client;

internal static class AggregateReplayValidation
{
    internal static void ValidateLimits(AggregateReplayWorkerLimits limits)
    {
        if (!limits.IsValid())
        {
            throw new ArgumentOutOfRangeException(nameof(limits), AggregateReplayWorkerLimits.ValidationMessage);
        }
    }

    internal static void ValidateReducer(AggregateReplayReducer reducer)
    {
        ArgumentNullException.ThrowIfNull(reducer);
        if (string.IsNullOrWhiteSpace(reducer.Version) || reducer.StateSchemaVersion <= AggregateReplayProtocol.InvalidSchemaVersion ||
            reducer.EventSchemaVersion <= AggregateReplayProtocol.InvalidSchemaVersion || reducer.Apply is null)
        {
            throw new ArgumentException(AggregateReplayMessages.InvalidReducer, nameof(reducer));
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
        var upcasterMap = AggregateReplayUpcast.BuildMap(upcasters, limits.MaximumRegisteredUpcasters, cancellationToken);
        var paths = ValidateEvents(page, reducer, upcasterMap, limits, input, cancellationToken);
        return new(initialState, paths);
    }

    private static string ValidateInitialState(
        AggregateReplayPage page,
        AggregateReplayReducer reducer,
        AggregateReplayWorkerLimits limits,
        AggregateReplayInput input)
    {
        if (page.Head.FirstAvailableRevision != AggregateReplayProtocol.FirstRetainedRevision)
        {
            throw new InvalidDataException(AggregateReplayMessages.IncompleteHistory);
        }
        input.AddState(reducer.InitialStateJson, AggregateReplayMessages.InitialState, limits);
        return reducer.InitialStateJson;
    }

    private static string ValidateSnapshot(
        AggregateReplayPage page,
        AggregateSnapshotState snapshot,
        AggregateReplayReducer reducer,
        AggregateReplayWorkerLimits limits,
        AggregateReplayInput input)
    {
        if (snapshot.SnapshotVersion <= AggregateReplayProtocol.NoRevision || snapshot.SourceRevision < AggregateReplayProtocol.NoRevision ||
            snapshot.SourceRevision > page.Head.TailRevision ||
            snapshot.SourceRevision < page.Head.FirstAvailableRevision - AggregateReplayProtocol.RevisionStep ||
            snapshot.StateSchemaVersion <= AggregateReplayProtocol.InvalidSchemaVersion ||
            !string.Equals(snapshot.ReducerVersion, reducer.Version, StringComparison.Ordinal) ||
            snapshot.StateSchemaVersion != reducer.StateSchemaVersion)
        {
            throw new InvalidDataException(AggregateReplayMessages.IncompatibleSnapshot);
        }
        input.AddState(snapshot.StateJson, AggregateReplayMessages.SnapshotState, limits);
        return snapshot.StateJson;
    }

    private static void ValidatePageIdentity(AggregateReplayPage page)
    {
        if (page.Stream is null || page.Stream.Partition is null || page.Head is null ||
            string.IsNullOrWhiteSpace(page.Stream.StreamSet) || string.IsNullOrWhiteSpace(page.Stream.StreamId) ||
            page.Stream.Generation <= AggregateReplayProtocol.MissingGeneration || page.Head.Generation != page.Stream.Generation ||
            page.Head.TailRevision < AggregateReplayProtocol.NoRevision || page.Head.FirstAvailableRevision <= AggregateReplayProtocol.NoRevision ||
            page.Head.FirstAvailableRevision - AggregateReplayProtocol.RevisionStep > page.Head.TailRevision || page.CutPosition < AggregateReplayProtocol.NoPosition)
        {
            throw new InvalidDataException(AggregateReplayMessages.InvalidHead);
        }
        if (page.Events.IsDefault)
        {
            throw new InvalidDataException(AggregateReplayMessages.UninitializedEvents);
        }
        if (page.Snapshot is { } snapshot && snapshot.Stream != page.Stream)
        {
            throw new InvalidDataException(AggregateReplayMessages.DifferentGeneration);
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
            throw new InvalidDataException(AggregateReplayMessages.EventLimitExceeded);
        }
        var sourceRevision = page.Snapshot?.SourceRevision ?? AggregateReplayProtocol.NoRevision;
        if (sourceRevision > page.Head.TailRevision || page.Head.TailRevision - sourceRevision != page.Events.Length)
        {
            throw new InvalidDataException(AggregateReplayMessages.IncompleteTail);
        }
        Dictionary<int, ImmutableArray<EventUpcaster>> paths = [];
        HashSet<string> eventIds = new(StringComparer.Ordinal);
        long priorSequence = AggregateReplayProtocol.NoSequence;
        for (var index = AggregateReplayProtocol.FirstEventIndex; index < page.Events.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = page.Events[index] ?? throw new InvalidDataException(AggregateReplayMessages.NullEvent);
            ValidateEventIdentity(record, page, sourceRevision, index, priorSequence, eventIds);
            input.AddJson(record.Data.PayloadJson, AggregateReplayMessages.EventPayload);
            input.AddJson(record.Data.HeadersJson, AggregateReplayMessages.EventHeaders);
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
        if (record.Stream != page.Stream || record.Revision != sourceRevision + index + AggregateReplayProtocol.RevisionStep ||
            record.EventSequence <= priorSequence || record.EventSequence <= AggregateReplayProtocol.NoSequence ||
            data is null || string.IsNullOrWhiteSpace(data.EventId) || string.IsNullOrWhiteSpace(data.EventType) ||
            data.SchemaVersion <= AggregateReplayProtocol.InvalidSchemaVersion || !eventIds.Add(data.EventId))
        {
            throw new InvalidDataException(AggregateReplayMessages.InvalidEvent);
        }
    }

    internal sealed record ValidatedReplay(
        string InitialState,
        Dictionary<int, ImmutableArray<EventUpcaster>> Paths);
}
