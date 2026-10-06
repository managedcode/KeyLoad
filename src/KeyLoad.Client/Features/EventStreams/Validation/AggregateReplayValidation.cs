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

    internal static string ValidatePage(
        AggregateReplayPage page,
        AggregateReplayReducer reducer,
        AggregateReplayWorkerLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        ValidatePageIdentity(page);
        var input = new AggregateReplayInput(limits.MaximumInputBytes, limits.MaximumJsonDepth);
        var initialState = page.Snapshot is { } snapshot
            ? ValidateSnapshot(page, snapshot, reducer, limits, input)
            : ValidateInitialState(page, reducer, limits, input);
        ValidateEvents(page, reducer, limits, input, cancellationToken);
        return initialState;
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

    private static void ValidateEvents(
        AggregateReplayPage page,
        AggregateReplayReducer reducer,
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
        HashSet<string> eventIds = new(StringComparer.Ordinal);
        long priorSequence = AggregateReplayProtocol.NoSequence;
        for (var index = AggregateReplayProtocol.FirstEventIndex; index < page.Events.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = page.Events[index] ?? throw new InvalidDataException(AggregateReplayMessages.NullEvent);
            ValidateEventIdentity(record, page, reducer, sourceRevision, index, priorSequence, eventIds);
            input.AddJson(record.Data.PayloadJson, AggregateReplayMessages.EventPayload);
            input.AddJson(record.Data.HeadersJson, AggregateReplayMessages.EventHeaders);
            priorSequence = record.EventSequence;
        }
    }

    private static void ValidateEventIdentity(
        EventRecord record,
        AggregateReplayPage page,
        AggregateReplayReducer reducer,
        long sourceRevision,
        int index,
        long priorSequence,
        HashSet<string> eventIds)
    {
        var data = record.Data;
        if (data is null || record.Stream != page.Stream ||
            record.Revision != sourceRevision + index + AggregateReplayProtocol.RevisionStep ||
            record.EventSequence <= priorSequence || record.EventSequence <= AggregateReplayProtocol.NoSequence ||
            string.IsNullOrWhiteSpace(data.EventId) || string.IsNullOrWhiteSpace(data.EventType) ||
            !eventIds.Add(data.EventId))
        {
            throw new InvalidDataException(AggregateReplayMessages.InvalidEvent);
        }
        if (data.SchemaVersion != reducer.EventSchemaVersion)
        {
            throw new InvalidDataException(AggregateReplayMessages.IncompatibleEventSchema);
        }
    }
}
