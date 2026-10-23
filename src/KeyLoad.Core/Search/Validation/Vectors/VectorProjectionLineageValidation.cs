using System.Collections.Immutable;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core.Features.Search;

/// <summary>Validates the complete stored lineage shape and bound source reference without resolving a new authority.</summary>
internal static class VectorProjectionLineageValidation
{
    private const string LineageCorrupt = "The stored vector projection lineage is corrupt.";

    internal static void ValidateLineage(VectorProjectionLineage lineage, PartitionRef partition,
        DocumentRecord targetDocument, VectorRecord vector)
    {
        if (HasInvalidLineageShape(lineage, partition, targetDocument, vector))
        {
            throw Errors.Fail(ErrorCode.Corruption, LineageCorrupt);
        }

        EntityReferenceLifetime.RequireBound(lineage.SourceDocument);
        ValidateIdentifiers(lineage);
        ValidateClassifications(lineage.SourceClassifications);
        ValidateClassifications(lineage.TargetClassifications);
    }

    private static bool HasInvalidLineageShape(VectorProjectionLineage lineage, PartitionRef partition,
        DocumentRecord targetDocument, VectorRecord vector)
        => HasInvalidSourceLineage(lineage, partition) || HasInvalidTargetLineage(lineage, partition, targetDocument, vector);

    private static bool HasInvalidSourceLineage(VectorProjectionLineage lineage, PartitionRef partition)
        => lineage.SourceStream?.Partition != partition || lineage.SourceDocument?.Partition != partition
            || lineage.SourceStream.Generation < 1 || lineage.SourceEventRevision < 1
            || lineage.SourceDocumentRevision < 1 || lineage.SourceSchemaVersion < 1
            || lineage.SourcePolicyEpoch < 0 || lineage.TargetSchemaVersion < 1
            || lineage.ReducerGeneration < 1 || lineage.SourceEventId is null
            || lineage.InputField is null || lineage.ReducerId is null || lineage.ReducerVersion is null
            || lineage.TargetCollection is null || lineage.TargetId is null || lineage.TargetField is null
            || lineage.TargetSpace is null || lineage.SourceClassifications.IsDefault;

    private static bool HasInvalidTargetLineage(VectorProjectionLineage lineage, PartitionRef partition,
        DocumentRecord targetDocument, VectorRecord vector)
        => lineage.TargetClassifications.IsDefault || targetDocument.Reference is null
            || targetDocument.Reference.Partition != partition
            || targetDocument.Reference.Collection != lineage.TargetCollection
            || targetDocument.Reference.Id != vector.DocumentId
            || lineage.TargetCollection != targetDocument.Reference.Collection || lineage.TargetId != vector.DocumentId
            || lineage.TargetField != vector.Field || lineage.TargetSpace != vector.Space;

    private static void ValidateIdentifiers(VectorProjectionLineage lineage)
    {
        const int DimensionFirstCount = 1;
        const int DimensionValidationBound = 4_096;

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

        if (lineage.TargetSpace.Dimension is < DimensionFirstCount or > DimensionValidationBound || !Enum.IsDefined(lineage.TargetSpace.Metric))
        {
            throw Errors.Fail(ErrorCode.Corruption, LineageCorrupt);
        }
    }

    private static void ValidateClassifications(ImmutableArray<string> classifications)
    {
        const int CompareOrdinalValidationBoundary = 0;

        string? previous = null;
        foreach (var classification in classifications)
        {
            if (string.IsNullOrEmpty(classification)
                || previous is not null && string.CompareOrdinal(previous, classification) >= CompareOrdinalValidationBoundary)
            {
                throw Errors.Fail(ErrorCode.Corruption, LineageCorrupt);
            }
            previous = classification;
        }
    }

    internal static void ValidateResource(ResourceDefinition resource, string expectedName, PartitionRef partition)
    {
        const int SchemaVersionValidationBoundary = 1;

        if (resource.Name != expectedName || resource.Kind != ResourceKind.Collection
            || resource.TransactionDomainId != partition.TransactionDomainId || resource.SchemaVersion < SchemaVersionValidationBoundary)
        {
            throw Errors.Fail(ErrorCode.Corruption, LineageCorrupt);
        }
    }

}
