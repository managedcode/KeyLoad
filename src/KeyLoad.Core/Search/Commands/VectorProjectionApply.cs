using KeyLoad.Core.ResourceExecution;
using KeyLoad.Core.Search;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes VectorProjectionApply operations against the caller's database context.</summary>
public static class VectorProjectionApply
{
    internal const string InvalidProjectionMessage = "The vector projection identity or target is invalid.";
    internal const string SourceEventMissingMessage = "The vector projection source event is unavailable.";
    internal const string SourceEventMismatchMessage = "The vector projection source event changed.";
    internal const string ProjectionEffectConflictMessage = "The projection identity was reused with different vector content.";
    internal const string ProjectionEffectCorruptMessage = "The stored vector projection effect is corrupt.";
    internal const string ProjectionLineageCorruptMessage = "The stored vector projection lineage is corrupt.";
    internal const string ProjectionClassificationMessage = "The target vector field does not preserve source classification.";
    internal const string ProjectionKind = "applyVectorProjection";
    internal static MutationReceipt ApplyVectorProjection(this DatabaseEngine engine, IAtomicTransaction transaction, PrincipalRecord principal, PartitionRef partition, ApplyVectorProjection request)
    {
        VectorProjectionValidation.ValidateProjectionRequest(partition, request);
        engine.Authorization.Require(principal, partition, request.SourceStream.StreamSet, Capability.EventsRead);
        engine.Authorization.Require(principal, partition, request.SourceDocument.Collection, Capability.DocumentsRead);
        engine.Authorization.Require(principal, partition, request.Target.Collection, Capability.DocumentsWrite);
        _ = engine.Resource(transaction, partition, request.SourceStream.StreamSet, ResourceKind.StreamSet);
        var sourceResource = engine.Resource(transaction, partition, request.SourceDocument.Collection, ResourceKind.Collection);
        engine.Authorization.RequireFieldUse(principal, sourceResource, request.InputField);
        var sourceDocument = engine.VisibleVertex(transaction, principal, request.SourceDocument);
        request = request with
        {
            SourceDocument = sourceDocument.Reference
        };
        if (sourceDocument.Revision != request.SourceDocumentRevision)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, SourceEventMismatchMessage);
        }

        VectorProjectionValidation.ValidateProjectionEvent(transaction, partition, request);
        var targetDocument = engine.ValidateProjectionTarget(transaction, principal, partition, request.Target, out var targetResource);
        var sourceClasses = VectorProjectionPolicy.Classifications(engine.Authorization, sourceResource, request.InputField);
        var targetClasses = VectorProjectionPolicy.Classifications(engine.Authorization, targetResource, request.Target.Field);
        VectorProjectionValidation.RequireCompatibleClassifications(sourceClasses, targetClasses);
        var fingerprint = JsonData.Fingerprint(request.Target);
        var effectKey = VectorProjectionKeys.Effect(partition, request);
        if (transaction.GetRecord<VectorProjectionEffect>(effectKey) is { } previous)
        {
            return ReuseProjectionEffect(transaction, partition, request, sourceResource, targetResource, sourceClasses, targetClasses, fingerprint, previous);
        }

        var receipt = new MutationReceipt(ProjectionKind, request.Target.Collection, request.Target.Id, targetDocument.Revision);
        PutCanonicalVector(transaction, partition, request.Target, targetDocument);
        var lineage = CreateProjectionLineage(request, sourceResource, targetResource, principal.PolicyEpoch, sourceClasses, targetClasses);
        transaction.PutRecord(VectorProjectionKeys.Lineage(partition, request.Target.Collection, request.Target.Field, request.Target.Id), lineage);
        transaction.PutRecord(effectKey, new VectorProjectionEffect(fingerprint, receipt));
        return receipt;
    }

    internal static void ReauthorizeVectorProjection(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, ApplyVectorProjection request)
    {
        VectorProjectionValidation.ValidateProjectionRequest(partition, request);
        engine.Authorization.Require(principal, partition, request.SourceStream.StreamSet, Capability.EventsRead);
        engine.Authorization.Require(principal, partition, request.SourceDocument.Collection, Capability.DocumentsRead);
        engine.Authorization.Require(principal, partition, request.Target.Collection, Capability.DocumentsWrite);
        _ = engine.Resource(view, partition, request.SourceStream.StreamSet, ResourceKind.StreamSet);
        var sourceResource = engine.Resource(view, partition, request.SourceDocument.Collection, ResourceKind.Collection);
        engine.Authorization.RequireFieldUse(principal, sourceResource, request.InputField);
        _ = engine.VisibleVertex(view, principal, request.SourceDocument);
        var targetResource = engine.Resource(view, partition, request.Target.Collection, ResourceKind.Collection);
        engine.Authorization.RequireFieldUse(principal, targetResource, request.Target.Field);
        engine.Authorization.RequireFieldWrite(principal, targetResource, request.Target.Field);
        var target = engine.VisibleVertex(view, principal, new(partition, request.Target.Collection, request.Target.Id));
        engine.Authorization.RequireWriteRow(principal, target.Access);
        var sourceClasses = VectorProjectionPolicy.Classifications(engine.Authorization, sourceResource, request.InputField);
        var targetClasses = VectorProjectionPolicy.Classifications(engine.Authorization, targetResource, request.Target.Field);
        VectorProjectionValidation.RequireCompatibleClassifications(sourceClasses, targetClasses);
    }

    internal static VectorProjectionLineage CreateProjectionLineage(ApplyVectorProjection request, ResourceDefinition source, ResourceDefinition target, long policyEpoch, System.Collections.Immutable.ImmutableArray<string> sourceClasses, System.Collections.Immutable.ImmutableArray<string> targetClasses) => new(request.SourceStream, request.SourceEventRevision, request.SourceEventId, request.SourceDocument, request.SourceDocumentRevision, request.InputField, source.SchemaVersion, policyEpoch, sourceClasses, request.Target.Collection, request.Target.Id, request.Target.Field, request.Target.Space, target.SchemaVersion, request.ReducerId, request.ReducerVersion, request.ReducerGeneration, targetClasses);
    internal static MutationReceipt ReuseProjectionEffect(IAtomicTransaction transaction, PartitionRef partition, ApplyVectorProjection request, ResourceDefinition sourceResource, ResourceDefinition targetResource, System.Collections.Immutable.ImmutableArray<string> sourceClasses, System.Collections.Immutable.ImmutableArray<string> targetClasses, string fingerprint, VectorProjectionEffect previous)
    {
        if (!string.Equals(previous.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Conflict, ProjectionEffectConflictMessage);
        }

        VectorProjectionValidation.ValidateEffectReceipt(previous.Receipt, request.Target);
        VectorProjectionValidation.RequireCanonicalProjectionEffect(transaction, partition, request.Target, previous);
        var lineageKey = VectorProjectionKeys.Lineage(partition, request.Target.Collection, request.Target.Field, request.Target.Id);
        var lineage = transaction.GetRecord<VectorProjectionLineage>(lineageKey) ?? throw Errors.Fail(ErrorCode.Corruption, ProjectionEffectCorruptMessage);
        EntityReferenceLifetime.RequireStored(transaction, lineage.SourceDocument, sourceResource);
        if (!LineageMatchesRequest(lineage, request))
        {
            throw Errors.Fail(ErrorCode.Corruption, ProjectionLineageCorruptMessage);
        }

        var replacement = CreateProjectionLineage(request, sourceResource, targetResource, lineage.SourcePolicyEpoch, sourceClasses, targetClasses);
        transaction.PutRecord(lineageKey, replacement);
        return previous.Receipt;
    }

    internal static void PutCanonicalVector(IAtomicTransaction transaction, PartitionRef partition, PutVector vector, DocumentRecord document)
    {
        var record = new VectorRecord(vector.Id, vector.Field, vector.Space, vector.Values, document.Revision);
        var lineageKey = VectorProjectionKeys.Lineage(partition, vector.Collection, vector.Field, vector.Id);
        if (transaction.GetRecord<VectorProjectionLineage>(lineageKey) is { } previousLineage)
        {
            var previousVector = new VectorRecord(vector.Id, vector.Field, previousLineage.TargetSpace!, vector.Values, document.Revision);
            VectorProjectionEligibility.ValidateLineage(previousLineage, partition, document, previousVector);
            transaction.Delete(VectorProjectionKeys.Effect(partition, previousLineage));
            transaction.Delete(lineageKey);
        }

        transaction.PutRecord(KeySpace.Partition(VisibleVectorReads.VectorKeySpace, partition, vector.Collection, vector.Field, vector.Id), record);
    }

    internal static bool LineageMatchesRequest(VectorProjectionLineage lineage, ApplyVectorProjection request) => lineage.SourceStream == request.SourceStream && lineage.SourceEventRevision == request.SourceEventRevision && lineage.SourceEventId == request.SourceEventId && lineage.SourceDocument == request.SourceDocument && lineage.SourceDocumentRevision == request.SourceDocumentRevision && lineage.InputField == request.InputField && lineage.TargetCollection == request.Target.Collection && lineage.TargetId == request.Target.Id && lineage.TargetField == request.Target.Field && lineage.TargetSpace == request.Target.Space && lineage.ReducerId == request.ReducerId && lineage.ReducerVersion == request.ReducerVersion && lineage.ReducerGeneration == request.ReducerGeneration;
}
