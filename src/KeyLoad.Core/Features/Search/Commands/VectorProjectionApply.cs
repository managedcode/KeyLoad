using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string InvalidProjectionMessage = "The vector projection identity or target is invalid.";
    private const string SourceEventMissingMessage = "The vector projection source event is unavailable.";
    private const string SourceEventMismatchMessage = "The vector projection source event changed.";
    private const string ProjectionEffectConflictMessage = "The projection identity was reused with different vector content.";
    private const string ProjectionEffectCorruptMessage = "The stored vector projection effect is corrupt.";
    private const string ProjectionLineageCorruptMessage = "The stored vector projection lineage is corrupt.";
    private const string ProjectionClassificationMessage = "The target vector field does not preserve source classification.";
    private const string ProjectionKind = "applyVectorProjection";

    internal MutationReceipt ApplyVectorProjection(IAtomicTransaction transaction, PrincipalRecord principal,
        PartitionRef partition, ApplyVectorProjection request)
    {
        ValidateProjectionRequest(partition, request);
        var streamResource = Resource(transaction, partition, request.SourceStream.StreamSet, ResourceKind.StreamSet);
        Authorization.Require(principal, partition, streamResource.Name, Capability.EventsRead);
        var sourceResource = Resource(transaction, partition, request.SourceDocument.Collection, ResourceKind.Collection);
        Authorization.Require(principal, partition, sourceResource.Name, Capability.DocumentsRead);
        Authorization.RequireFieldUse(principal, sourceResource, request.InputField);
        var sourceDocument = VisibleVertex(transaction, principal, request.SourceDocument);
        if (sourceDocument.Reference != request.SourceDocument)
        {
            throw Errors.Fail(ErrorCode.Corruption, ProjectionLineageCorruptMessage);
        }
        if (sourceDocument.Revision != request.SourceDocumentRevision)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, SourceEventMismatchMessage);
        }

        ValidateProjectionEvent(transaction, partition, request);
        var targetDocument = ValidateProjectionTarget(transaction, principal, partition, request.Target,
            out var targetResource);
        var sourceClasses = VectorProjectionPolicy.Classifications(Authorization, sourceResource, request.InputField);
        var targetClasses = VectorProjectionPolicy.Classifications(Authorization, targetResource, request.Target.Field);
        RequireCompatibleClassifications(sourceClasses, targetClasses);
        var fingerprint = JsonData.Fingerprint(request.Target);
        var effectKey = VectorProjectionKeys.Effect(partition, request);
        if (transaction.GetRecord<VectorProjectionEffect>(effectKey) is { } previous)
        {
            return ReuseProjectionEffect(transaction, partition, request, sourceResource, targetResource,
                sourceClasses, targetClasses, fingerprint, previous);
        }

        var receipt = new MutationReceipt(ProjectionKind, request.Target.Collection, request.Target.Id,
            targetDocument.Revision);
        PutCanonicalVector(transaction, partition, request.Target, targetDocument);
        var lineage = CreateProjectionLineage(request, sourceResource, targetResource, principal.PolicyEpoch,
            sourceClasses, targetClasses);
        transaction.PutRecord(VectorProjectionKeys.Lineage(partition, request.Target.Collection,
            request.Target.Field, request.Target.Id), lineage);
        transaction.PutRecord(effectKey, new VectorProjectionEffect(fingerprint, receipt));
        return receipt;
    }

    internal void ReauthorizeVectorProjection(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, ApplyVectorProjection request)
    {
        ValidateProjectionRequest(partition, request);
        var streamResource = Resource(view, partition, request.SourceStream.StreamSet, ResourceKind.StreamSet);
        Authorization.Require(principal, partition, streamResource.Name, Capability.EventsRead);
        var sourceResource = Resource(view, partition, request.SourceDocument.Collection, ResourceKind.Collection);
        Authorization.Require(principal, partition, sourceResource.Name, Capability.DocumentsRead);
        Authorization.RequireFieldUse(principal, sourceResource, request.InputField);
        _ = VisibleVertex(view, principal, request.SourceDocument);

        var targetResource = Resource(view, partition, request.Target.Collection, ResourceKind.Collection);
        Authorization.Require(principal, partition, targetResource.Name, Capability.DocumentsWrite);
        Authorization.RequireFieldUse(principal, targetResource, request.Target.Field);
        Authorization.RequireFieldWrite(principal, targetResource, request.Target.Field);
        var target = VisibleVertex(view, principal,
            new(partition, request.Target.Collection, request.Target.Id));
        Authorization.RequireWriteRow(principal, target.Access);
        var sourceClasses = VectorProjectionPolicy.Classifications(Authorization, sourceResource, request.InputField);
        var targetClasses = VectorProjectionPolicy.Classifications(Authorization, targetResource, request.Target.Field);
        RequireCompatibleClassifications(sourceClasses, targetClasses);
    }

    private static void ValidateProjectionRequest(PartitionRef partition, ApplyVectorProjection request)
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
        ValidatePartition(partition);
        ArgumentNullException.ThrowIfNull(request.SourceStream.Partition);
        ArgumentNullException.ThrowIfNull(request.SourceDocument.Partition);
        if (request.SourceStream.Partition != partition || request.SourceDocument.Partition != partition
            || request.SourceStream.Generation < GenerationValidationBoundary || request.SourceEventRevision < SourceEventRevisionValidationBoundary
            || request.SourceDocumentRevision < SourceDocumentRevisionValidationBoundary || request.ReducerGeneration < ReducerGenerationValidationBoundary)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidProjectionMessage);
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
        if (!Enum.IsDefined(request.Target.Space.Metric)
            || request.Target.Space.Dimension is < DimensionFirstCount or > DimensionValidationBound
            || request.Target.Values.IsDefault || request.Target.Values.Length != request.Target.Space.Dimension
            || request.Target.Values.Any(value => !float.IsFinite(value)))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidProjectionMessage);
        }
    }

    private static void ValidateProjectionEvent(IKeyValueView view, PartitionRef partition,
        ApplyVectorProjection request)
    {
        const int TailRevisionEmptyCount = 0;
        const int FirstAvailableRevisionSingleItemCount = 1;
        const int GenerationSingleItemCount = 1;

        const string EventSpace = "event";
        const string EventIdentitySpace = "event-id";
        var stream = request.SourceStream;
        var headKey = KeySpace.Partition(AggregateReplayReader.StreamHeadKeySpace, partition,
            stream.StreamSet, stream.StreamId);
        var head = view.GetRecord<StreamHead>(headKey) ?? new StreamHead(TailRevisionEmptyCount, FirstAvailableRevisionSingleItemCount, GenerationSingleItemCount);
        AggregateReplayReader.ValidateHead(head);
        if (head.Generation != stream.Generation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, SourceEventMismatchMessage);
        }
        if (request.SourceEventRevision < head.FirstAvailableRevision)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, SourceEventMissingMessage);
        }
        if (request.SourceEventRevision > head.TailRevision)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, SourceEventMismatchMessage);
        }

        var eventKey = KeySpace.Partition(EventSpace, partition, stream.StreamSet,
            stream.StreamId, stream.Generation, request.SourceEventRevision);
        var record = view.GetRecord<EventRecord>(eventKey)
            ?? throw Errors.Fail(ErrorCode.Corruption, ProjectionLineageCorruptMessage);
        ValidateProjectionEventRecord(request, record);
        var identity = view.GetRecord<EventIdentity>(KeySpace.Partition(EventIdentitySpace,
            partition, stream.StreamSet, stream.StreamId, stream.Generation, record.Data.EventId));
        if (identity is null || identity.StreamId != stream.StreamId
            || identity.Generation != stream.Generation || identity.Revision != record.Revision)
        {
            throw Errors.Fail(ErrorCode.Corruption, ProjectionLineageCorruptMessage);
        }
    }

    private static void ValidateProjectionEventRecord(ApplyVectorProjection request, EventRecord record)
    {
        const int EventSequenceValidationBoundary = 1;
        const int SchemaVersionValidationBoundary = 1;

        if (record.Stream != request.SourceStream || record.Revision != request.SourceEventRevision
            || record.EventSequence < EventSequenceValidationBoundary || record.Data is null || record.Data.SchemaVersion < SchemaVersionValidationBoundary)
        {
            throw Errors.Fail(ErrorCode.Corruption, ProjectionLineageCorruptMessage);
        }
        try
        {
            JsonData.Identifier(record.Data.EventId);
        }
        catch (KeyLoadException)
        {
            throw Errors.Fail(ErrorCode.Corruption, ProjectionLineageCorruptMessage);
        }
        if (record.Data.EventId != request.SourceEventId)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, SourceEventMismatchMessage);
        }
    }

    private static void RequireCompatibleClassifications(System.Collections.Immutable.ImmutableArray<string> source,
        System.Collections.Immutable.ImmutableArray<string> target)
    {
        if (!VectorProjectionPolicy.TargetIsAtLeastAsRestrictive(source, target))
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, ProjectionClassificationMessage);
        }
    }

    private static VectorProjectionLineage CreateProjectionLineage(ApplyVectorProjection request,
        ResourceDefinition source, ResourceDefinition target, long policyEpoch,
        System.Collections.Immutable.ImmutableArray<string> sourceClasses,
        System.Collections.Immutable.ImmutableArray<string> targetClasses)
        => new(request.SourceStream, request.SourceEventRevision, request.SourceEventId,
            request.SourceDocument, request.SourceDocumentRevision, request.InputField, source.SchemaVersion,
            policyEpoch, sourceClasses, request.Target.Collection, request.Target.Id,
            request.Target.Field, request.Target.Space, target.SchemaVersion, request.ReducerId,
            request.ReducerVersion, request.ReducerGeneration, targetClasses);

    private static MutationReceipt ReuseProjectionEffect(IAtomicTransaction transaction, PartitionRef partition,
        ApplyVectorProjection request, ResourceDefinition sourceResource, ResourceDefinition targetResource,
        System.Collections.Immutable.ImmutableArray<string> sourceClasses,
        System.Collections.Immutable.ImmutableArray<string> targetClasses, string fingerprint,
        VectorProjectionEffect previous)
    {
        if (!string.Equals(previous.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Conflict, ProjectionEffectConflictMessage);
        }
        ValidateEffectReceipt(previous.Receipt, request.Target);
        var lineageKey = VectorProjectionKeys.Lineage(partition, request.Target.Collection,
            request.Target.Field, request.Target.Id);
        var lineage = transaction.GetRecord<VectorProjectionLineage>(lineageKey)
            ?? throw Errors.Fail(ErrorCode.Corruption, ProjectionEffectCorruptMessage);
        if (!LineageMatchesRequest(lineage, request))
        {
            throw Errors.Fail(ErrorCode.Corruption, ProjectionLineageCorruptMessage);
        }
        var replacement = CreateProjectionLineage(request, sourceResource, targetResource,
            lineage.SourcePolicyEpoch, sourceClasses, targetClasses);
        transaction.PutRecord(lineageKey, replacement);
        return previous.Receipt;
    }

    private DocumentRecord ValidateProjectionTarget(IAtomicTransaction transaction, PrincipalRecord principal,
        PartitionRef partition, PutVector vector, out ResourceDefinition resource)
    {
        resource = Resource(transaction, partition, vector.Collection, ResourceKind.Collection);
        Authorization.RequireFieldWrite(principal, resource, vector.Field);
        var document = VisibleVertex(transaction, principal, new(partition, vector.Collection, vector.Id));
        Authorization.RequireWriteRow(principal, document.Access);
        CheckRevision(document.Revision, vector.ExpectedDocumentRevision);
        return document;
    }

    private static void PutCanonicalVector(IAtomicTransaction transaction, PartitionRef partition, PutVector vector,
        DocumentRecord document)
    {
        var record = new VectorRecord(vector.Id, vector.Field, vector.Space, vector.Values, document.Revision);
        var lineageKey = VectorProjectionKeys.Lineage(partition, vector.Collection, vector.Field, vector.Id);
        if (transaction.GetRecord<VectorProjectionLineage>(lineageKey) is { } previousLineage)
        {
            var previousVector = new VectorRecord(vector.Id, vector.Field, previousLineage.TargetSpace!,
                vector.Values, document.Revision);
            VectorProjectionEligibility.ValidateLineage(previousLineage, partition, document, previousVector);
            transaction.Delete(VectorProjectionKeys.Effect(partition, previousLineage));
            transaction.Delete(lineageKey);
        }
        transaction.PutRecord(KeySpace.Partition(VisibleVectorReads.VectorKeySpace, partition, vector.Collection, vector.Field, vector.Id),
            record);
    }

    private static bool LineageMatchesRequest(VectorProjectionLineage lineage, ApplyVectorProjection request)
        => lineage.SourceStream == request.SourceStream
            && lineage.SourceEventRevision == request.SourceEventRevision
            && lineage.SourceEventId == request.SourceEventId
            && lineage.SourceDocument == request.SourceDocument
            && lineage.SourceDocumentRevision == request.SourceDocumentRevision
            && lineage.InputField == request.InputField
            && lineage.TargetCollection == request.Target.Collection
            && lineage.TargetId == request.Target.Id
            && lineage.TargetField == request.Target.Field
            && lineage.TargetSpace == request.Target.Space
            && lineage.ReducerId == request.ReducerId
            && lineage.ReducerVersion == request.ReducerVersion
            && lineage.ReducerGeneration == request.ReducerGeneration;

    private static void ValidateEffectReceipt(MutationReceipt receipt, PutVector target)
    {
        const int RevisionValidationBoundary = 1;

        if (receipt is null || receipt.Kind != ProjectionKind || receipt.Resource != target.Collection
            || receipt.Id != target.Id || receipt.Revision < RevisionValidationBoundary)
        {
            throw Errors.Fail(ErrorCode.Corruption, ProjectionEffectCorruptMessage);
        }
    }
}
