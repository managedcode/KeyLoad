using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes EventStreamsCommands operations against the caller's database context.</summary>
public static class EventStreamsCommands
{
    internal const string InvalidSnapshotSourceMessage = "The aggregate snapshot source is invalid.";
    internal const string SnapshotSourceAheadMessage = "The aggregate snapshot source is ahead of the stream tail.";
    internal const string SnapshotSourceBeforeFloorMessage = "The aggregate snapshot source is older than retained history.";
    internal const string SnapshotVersionConflictMessage = "The aggregate snapshot version has changed.";
    internal const string SnapshotSourceMovedBackwardsMessage = "The aggregate snapshot source cannot move backwards.";
    internal const string SnapshotVersionExhaustedMessage = "The aggregate snapshot version is exhausted.";
    internal const string SnapshotCorruptMessage = "The stored aggregate snapshot is inconsistent with its stream.";
    internal const string SnapshotGenerationStaleMessage = "The aggregate snapshot stream generation is stale.";
    internal const string SnapshotMutationKind = "storeAggregateSnapshot";
    internal static MutationReceipt SaveAggregateSnapshot(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, StoreAggregateSnapshot request)
    {
        const int TailRevisionEmptyCount = 0;
        const int FirstAvailableRevisionSingleItemCount = 1;
        const int GenerationSingleItemCount = 1;
        const int CurrentSnapshotVersionValidationBoundary = 0;
        const int VersionSingleItemCount = 1;
        engine.ValidateSnapshotRequest(request);
        var resource = engine.Resource(tx, partition, request.StreamSet, ResourceKind.StreamSet);
        engine.Authorization.RequireReplayInput(principal, resource);
        var head = tx.GetRecord<StreamHead>(KeySpace.Partition(AggregateReplayReader.StreamHeadKeySpace, partition, request.StreamSet, request.StreamId)) ?? new StreamHead(TailRevisionEmptyCount, FirstAvailableRevisionSingleItemCount, GenerationSingleItemCount);
        AggregateReplayReader.ValidateHead(head);
        if (head.Generation != request.Generation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, SnapshotGenerationStaleMessage);
        }

        ValidateSnapshotSource(request, head);
        var stream = new StreamRef(partition, request.StreamSet, request.StreamId, head.Generation);
        var key = AggregateSnapshotPersistence.Key(stream);
        var current = tx.ReadOwnedValue(key) is { } bytes ? AggregateSnapshotPersistence.Deserialize(bytes, engine.Limits) : null;
        if (current is not null && (current.Stream != stream || current.SourceRevision > head.TailRevision))
        {
            throw Errors.Fail(ErrorCode.Corruption, SnapshotCorruptMessage);
        }

        var currentVersion = current?.SnapshotVersion ?? CurrentSnapshotVersionValidationBoundary;
        if (request.ExpectedSnapshotVersion != currentVersion)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, SnapshotVersionConflictMessage);
        }

        if (current is not null && request.SourceRevision < current.SourceRevision)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, SnapshotSourceMovedBackwardsMessage);
        }

        if (currentVersion == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, SnapshotVersionExhaustedMessage);
        }

        var state = AggregateSnapshotPersistence.Create(stream, currentVersion + VersionSingleItemCount, request.SourceRevision, request.ReducerVersion, request.StateSchemaVersion, request.StateJson);
        tx.Put(key, AggregateSnapshotPersistence.Serialize(state));
        return new(SnapshotMutationKind, request.StreamSet, request.StreamId, state.SnapshotVersion);
    }

    internal static void ValidateSnapshotRequest(this DatabaseEngine engine, StoreAggregateSnapshot request)
    {
        const int GenerationValidationBoundary = 1;
        const int SourceRevisionValidationBoundary = 0;
        const int StateSchemaVersionValidationBoundary = 1;
        const int ExpectedSnapshotVersionValidationBoundary = 0;
        ArgumentNullException.ThrowIfNull(request);
        JsonData.Identifier(request.StreamSet);
        JsonData.Identifier(request.StreamId);
        JsonData.Identifier(request.ReducerVersion);
        if (request.Generation < GenerationValidationBoundary || request.SourceRevision < SourceRevisionValidationBoundary || request.StateSchemaVersion < StateSchemaVersionValidationBoundary || request.ExpectedSnapshotVersion < ExpectedSnapshotVersionValidationBoundary || request.StateJson is null)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidSnapshotSourceMessage);
        }

        _ = JsonData.Validate(request.StateJson, engine.Limits, requireObject: false);
    }

    internal static void ValidateSnapshotSource(StoreAggregateSnapshot request, StreamHead head)
    {
        const int FirstAvailableRevisionStep = 1;
        if (request.SourceRevision > head.TailRevision)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, SnapshotSourceAheadMessage);
        }

        if (request.SourceRevision < head.FirstAvailableRevision - FirstAvailableRevisionStep)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, SnapshotSourceBeforeFloorMessage);
        }
    }
}
