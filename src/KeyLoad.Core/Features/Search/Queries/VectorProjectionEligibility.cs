using System.Collections.Immutable;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.Search;

/// <summary>Revalidates the current source authority for a derived vector inside its read cut.</summary>
internal static class VectorProjectionEligibility
{
    private const string LineageCorrupt = "The stored vector projection lineage is corrupt.";
    private const string EffectCorrupt = "The stored vector projection effect is corrupt.";

    internal static bool IsEligible(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, string collection, DocumentRecord targetDocument, VectorRecord vector,
        ReadExecutionBudget budget)
    {
        if (vector.DocumentId is null || vector.Field is null || vector.Space is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, LineageCorrupt);
        }
        var lineage = budget.ReadRecord<VectorProjectionLineage>(view,
            VectorProjectionKeys.Lineage(partition, collection, vector.Field, vector.DocumentId));
        if (lineage is null)
        {
            return true;
        }

        ValidateLineage(lineage, partition, targetDocument, vector);
        ValidateEffect(budget.ReadRecord<VectorProjectionEffect>(view,
            VectorProjectionKeys.Effect(partition, lineage)), lineage, vector);
        var sourceResource = budget.ReadRecord<ResourceDefinition>(view,
            KeySpace.Resource(partition.TenantId, partition.DatabaseId, lineage.SourceDocument.Collection));
        if (sourceResource is null)
        {
            return false;
        }
        ValidateResource(sourceResource, lineage.SourceDocument.Collection, partition);
        if (!CanReadSource(database, principal, partition, sourceResource))
        {
            return false;
        }

        var source = budget.ReadRecord<DocumentRecord>(view,
            DocumentStorageKeys.RecordKey(partition, lineage.SourceDocument.Collection, lineage.SourceDocument.Id));
        if (source is null || source.Deleted || source.Revision != lineage.SourceDocumentRevision)
        {
            return false;
        }
        if (source.Reference is null || source.Reference != lineage.SourceDocument)
        {
            throw Errors.Fail(ErrorCode.Corruption, LineageCorrupt);
        }
        if (!database.Authorization.CanReadRow(principal, source.Access)
            || !CanUseSourceField(database, principal, sourceResource, lineage.InputField))
        {
            return false;
        }

        return HasCompatiblePolicy(database, view, partition, collection, lineage, sourceResource, budget);
    }

    private static bool HasCompatiblePolicy(DatabaseEngine database, IKeyValueView view, PartitionRef partition,
        string collection, VectorProjectionLineage lineage, ResourceDefinition sourceResource,
        ReadExecutionBudget budget)
    {
        var targetResource = budget.ReadRecord<ResourceDefinition>(view,
            KeySpace.Resource(partition.TenantId, partition.DatabaseId, collection))
            ?? throw Errors.Fail(ErrorCode.Corruption, LineageCorrupt);
        ValidateResource(targetResource, collection, partition);
        var sourceClasses = VectorProjectionPolicy.Classifications(database.Authorization, sourceResource,
            lineage.InputField);
        var targetClasses = VectorProjectionPolicy.Classifications(database.Authorization, targetResource,
            lineage.TargetField);
        return VectorProjectionPolicy.Same(sourceClasses, lineage.SourceClassifications)
            && VectorProjectionPolicy.Same(targetClasses, lineage.TargetClassifications)
            && VectorProjectionPolicy.TargetIsAtLeastAsRestrictive(sourceClasses, targetClasses);
    }

    private static bool CanReadSource(DatabaseEngine database, PrincipalRecord principal, PartitionRef partition,
        ResourceDefinition resource)
    {
        try
        {
            database.Authorization.Require(principal, partition, resource.Name, Capability.DocumentsRead);
            return true;
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.PermissionDenied)
        {
            return false;
        }
    }

    private static bool CanUseSourceField(DatabaseEngine database, PrincipalRecord principal,
        ResourceDefinition resource, string field)
    {
        try
        {
            database.Authorization.RequireFieldUse(principal, resource, field);
            return true;
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.PermissionDenied)
        {
            return false;
        }
    }

    internal static void ValidateLineage(VectorProjectionLineage lineage, PartitionRef partition,
        DocumentRecord targetDocument, VectorRecord vector)
    {
        if (lineage.SourceStream?.Partition != partition || lineage.SourceDocument?.Partition != partition
            || lineage.SourceStream.Generation < 1 || lineage.SourceEventRevision < 1
            || lineage.SourceDocumentRevision < 1 || lineage.SourceSchemaVersion < 1
            || lineage.SourcePolicyEpoch < 0 || lineage.TargetSchemaVersion < 1
            || lineage.ReducerGeneration < 1 || lineage.SourceEventId is null
            || lineage.InputField is null || lineage.ReducerId is null || lineage.ReducerVersion is null
            || lineage.TargetCollection is null || lineage.TargetId is null || lineage.TargetField is null
            || lineage.TargetSpace is null || lineage.SourceClassifications.IsDefault
            || lineage.TargetClassifications.IsDefault || targetDocument.Reference is null
            || targetDocument.Reference.Partition != partition
            || targetDocument.Reference.Collection != lineage.TargetCollection
            || targetDocument.Reference.Id != vector.DocumentId
            || lineage.TargetCollection != targetDocument.Reference.Collection || lineage.TargetId != vector.DocumentId
            || lineage.TargetField != vector.Field || lineage.TargetSpace != vector.Space)
        {
            throw Errors.Fail(ErrorCode.Corruption, LineageCorrupt);
        }

        ValidateIdentifiers(lineage);
        ValidateClassifications(lineage.SourceClassifications);
        ValidateClassifications(lineage.TargetClassifications);
    }

    private static void ValidateIdentifiers(VectorProjectionLineage lineage)
    {
        try
        {
            JsonData.Identifier(lineage.SourceStream.StreamSet);
            JsonData.Identifier(lineage.SourceStream.StreamId);
            JsonData.Identifier(lineage.SourceEventId);
            JsonData.Identifier(lineage.SourceDocument.Collection);
            JsonData.Identifier(lineage.SourceDocument.Id);
            _ = JsonData.PathSegments(lineage.InputField);
            JsonData.Identifier(lineage.TargetCollection);
            JsonData.Identifier(lineage.TargetId);
            _ = JsonData.PathSegments(lineage.TargetField);
            JsonData.Identifier(lineage.ReducerId);
            JsonData.Identifier(lineage.ReducerVersion);
            JsonData.Identifier(lineage.TargetSpace.Id);
            JsonData.Identifier(lineage.TargetSpace.Model);
            JsonData.Identifier(lineage.TargetSpace.Version);
        }
        catch (KeyLoadException)
        {
            throw Errors.Fail(ErrorCode.Corruption, LineageCorrupt);
        }

        if (lineage.TargetSpace.Dimension is < 1 or > 4_096 || !Enum.IsDefined(lineage.TargetSpace.Metric))
        {
            throw Errors.Fail(ErrorCode.Corruption, LineageCorrupt);
        }
    }

    private static void ValidateClassifications(ImmutableArray<string> classifications)
    {
        string? previous = null;
        foreach (var classification in classifications)
        {
            if (string.IsNullOrEmpty(classification)
                || previous is not null && string.CompareOrdinal(previous, classification) >= 0)
            {
                throw Errors.Fail(ErrorCode.Corruption, LineageCorrupt);
            }
            previous = classification;
        }
    }

    private static void ValidateResource(ResourceDefinition resource, string expectedName, PartitionRef partition)
    {
        if (resource.Name != expectedName || resource.Kind != ResourceKind.Collection
            || resource.TransactionDomainId != partition.TransactionDomainId || resource.SchemaVersion < 1)
        {
            throw Errors.Fail(ErrorCode.Corruption, LineageCorrupt);
        }
    }

    private static void ValidateEffect(VectorProjectionEffect? effect, VectorProjectionLineage lineage,
        VectorRecord vector)
    {
        if (effect is null || effect.Fingerprint is null || effect.Fingerprint.Length != 64
            || effect.Fingerprint.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            || effect.Receipt is null || effect.Receipt.Kind != "applyVectorProjection"
            || effect.Receipt.Resource != lineage.TargetCollection || effect.Receipt.Id != lineage.TargetId
            || effect.Receipt.Revision != vector.DocumentRevision)
        {
            throw Errors.Fail(ErrorCode.Corruption, EffectCorrupt);
        }
    }
}
