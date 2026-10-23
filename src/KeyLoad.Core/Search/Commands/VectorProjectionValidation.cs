using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Owns the VectorProjectionValidation operation contract.</summary>
internal static class VectorProjectionValidation
{
    internal static void ValidateProjectionRequest(PartitionRef partition, ApplyVectorProjection request)
    {
        const int GenerationValidationBoundary = 1;
        const int SourceEventRevisionValidationBoundary = 1;
        const int SourceDocumentRevisionValidationBoundary = 1;
        const int ReducerGenerationValidationBoundary = 1;
        const int DimensionFirstCount = 1;
        const int DimensionValidationBound = 4_096;
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SourceStream);
        ArgumentNullException.ThrowIfNull(request.SourceDocument);
        ArgumentNullException.ThrowIfNull(request.Target);
        ArgumentNullException.ThrowIfNull(request.Target.Space);
        ProtocolValidation.ValidateMutationStructure(request.Target);
        EngineResourceAccess.ValidatePartition(partition);
        ArgumentNullException.ThrowIfNull(request.SourceStream.Partition);
        ArgumentNullException.ThrowIfNull(request.SourceDocument.Partition);
        if (request.SourceStream.Partition != partition || request.SourceDocument.Partition != partition || request.SourceStream.Generation < GenerationValidationBoundary || request.SourceEventRevision < SourceEventRevisionValidationBoundary || request.SourceDocumentRevision < SourceDocumentRevisionValidationBoundary || request.ReducerGeneration < ReducerGenerationValidationBoundary)
        {
            throw Errors.Fail(ErrorCode.Validation, VectorProjectionApply.InvalidProjectionMessage);
        }

        JsonData.Identifier(request.SourceStream.StreamSet);
        JsonData.Identifier(request.SourceStream.StreamId);
        JsonData.Identifier(request.SourceEventId);
        JsonData.Identifier(request.SourceDocument.Collection);
        JsonData.Identifier(request.SourceDocument.Id);
        _ = JsonData.PathSegments(request.InputField);
        JsonData.Identifier(request.ReducerId);
        JsonData.Identifier(request.ReducerVersion);
        JsonData.Identifier(request.Target.Collection);
        JsonData.Identifier(request.Target.Id);
        _ = JsonData.PathSegments(request.Target.Field);
        JsonData.Identifier(request.Target.Space.Id);
        JsonData.Identifier(request.Target.Space.Model);
        JsonData.Identifier(request.Target.Space.Version);
        if (!Enum.IsDefined(request.Target.Space.Metric) || request.Target.Space.Dimension is < DimensionFirstCount or > DimensionValidationBound || request.Target.Values.IsDefault || request.Target.Values.Length != request.Target.Space.Dimension || request.Target.Values.Any(value => !float.IsFinite(value)))
        {
            throw Errors.Fail(ErrorCode.Validation, VectorProjectionApply.InvalidProjectionMessage);
        }
    }

    internal static void ValidateProjectionEvent(IKeyValueView view, PartitionRef partition, ApplyVectorProjection request)
    {
        const int TailRevisionEmptyCount = 0;
        const int FirstAvailableRevisionSingleItemCount = 1;
        const int GenerationSingleItemCount = 1;
        const string EventSpace = "event";
        const string EventIdentitySpace = "event-id";
        var stream = request.SourceStream;
        var headKey = KeySpace.Partition(AggregateReplayReader.StreamHeadKeySpace, partition, stream.StreamSet, stream.StreamId);
        var head = view.GetRecord<StreamHead>(headKey) ?? new StreamHead(TailRevisionEmptyCount, FirstAvailableRevisionSingleItemCount, GenerationSingleItemCount);
        AggregateReplayReader.ValidateHead(head);
        if (head.Generation != stream.Generation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, VectorProjectionApply.SourceEventMismatchMessage);
        }

        if (request.SourceEventRevision < head.FirstAvailableRevision)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, VectorProjectionApply.SourceEventMissingMessage);
        }

        if (request.SourceEventRevision > head.TailRevision)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, VectorProjectionApply.SourceEventMismatchMessage);
        }

        var eventKey = KeySpace.Partition(EventSpace, partition, stream.StreamSet, stream.StreamId, stream.Generation, request.SourceEventRevision);
        var record = view.GetRecord<EventRecord>(eventKey) ?? throw Errors.Fail(ErrorCode.Corruption, VectorProjectionApply.ProjectionLineageCorruptMessage);
        VectorProjectionValidation.ValidateProjectionEventRecord(request, record);
        var identity = view.GetRecord<EventIdentity>(KeySpace.Partition(EventIdentitySpace, partition, stream.StreamSet, stream.StreamId, stream.Generation, record.Data.EventId));
        if (identity is null || identity.StreamId != stream.StreamId || identity.Generation != stream.Generation || identity.Revision != record.Revision)
        {
            throw Errors.Fail(ErrorCode.Corruption, VectorProjectionApply.ProjectionLineageCorruptMessage);
        }
    }

    internal static void ValidateProjectionEventRecord(ApplyVectorProjection request, EventRecord record)
    {
        const int EventSequenceValidationBoundary = 1;
        const int SchemaVersionValidationBoundary = 1;
        if (record.Stream != request.SourceStream || record.Revision != request.SourceEventRevision || record.EventSequence < EventSequenceValidationBoundary || record.Data is null || record.Data.SchemaVersion < SchemaVersionValidationBoundary)
        {
            throw Errors.Fail(ErrorCode.Corruption, VectorProjectionApply.ProjectionLineageCorruptMessage);
        }

        try
        {
            JsonData.Identifier(record.Data.EventId);
        }
        catch (KeyLoadException)
        {
            throw Errors.Fail(ErrorCode.Corruption, VectorProjectionApply.ProjectionLineageCorruptMessage);
        }

        if (record.Data.EventId != request.SourceEventId)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, VectorProjectionApply.SourceEventMismatchMessage);
        }
    }

    internal static void RequireCompatibleClassifications(System.Collections.Immutable.ImmutableArray<string> source, System.Collections.Immutable.ImmutableArray<string> target)
    {
        if (!VectorProjectionPolicy.TargetIsAtLeastAsRestrictive(source, target))
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, VectorProjectionApply.ProjectionClassificationMessage);
        }
    }

    internal static DocumentRecord ValidateProjectionTarget(this DatabaseEngine engine, IAtomicTransaction transaction, PrincipalRecord principal, PartitionRef partition, PutVector vector, out ResourceDefinition resource)
    {
        resource = engine.Resource(transaction, partition, vector.Collection, ResourceKind.Collection);
        engine.Authorization.RequireFieldWrite(principal, resource, vector.Field);
        var document = engine.VisibleVertex(transaction, principal, new(partition, vector.Collection, vector.Id));
        engine.Authorization.RequireWriteRow(principal, document.Access);
        DocumentStorageExecutionDocuments.CheckRevision(document.Revision, vector.ExpectedDocumentRevision);
        Features.Search.VectorFieldProfiles.Require(resource, vector);
        return document;
    }

    internal static void RequireCanonicalProjectionEffect(IKeyValueView view, PartitionRef partition, PutVector target, VectorProjectionEffect effect)
    {
        var vector = view.GetRecord<VectorRecord>(KeySpace.Partition(VisibleVectorReads.VectorKeySpace, partition, target.Collection, target.Field, target.Id));
        if (vector is null || vector.DocumentId != target.Id || vector.Field != target.Field || vector.Space != target.Space || vector.DocumentRevision != target.ExpectedDocumentRevision || effect.Receipt.Revision != vector.DocumentRevision || !VectorProjectionPolicy.MatchesCanonicalVector(effect.Fingerprint, target.Collection, vector))
        {
            throw Errors.Fail(ErrorCode.Corruption, VectorProjectionApply.ProjectionEffectCorruptMessage);
        }
    }

    internal static void ValidateEffectReceipt(MutationReceipt receipt, PutVector target)
    {
        const int RevisionValidationBoundary = 1;
        if (receipt is null || receipt.Kind != VectorProjectionApply.ProjectionKind || receipt.Resource != target.Collection || receipt.Id != target.Id || receipt.Revision < RevisionValidationBoundary)
        {
            throw Errors.Fail(ErrorCode.Corruption, VectorProjectionApply.ProjectionEffectCorruptMessage);
        }
    }
}
