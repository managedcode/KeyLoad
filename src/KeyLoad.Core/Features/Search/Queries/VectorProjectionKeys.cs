namespace KeyLoad.Core.Features.Search;

/// <summary>Builds private canonical keys for vector provenance and idempotent effects.</summary>
internal static class VectorProjectionKeys
{
    internal const string LineageSpace = "vector-projection-lineage";
    internal const string EffectSpace = "vector-projection-effect";

    internal static byte[] Lineage(PartitionRef partition, string collection, string field, string id)
        => KeySpace.Partition(LineageSpace, partition, collection, field, id);

    internal static byte[] Effect(PartitionRef partition, ApplyVectorProjection request)
        => KeySpace.Partition(EffectSpace, partition, request.SourceStream.StreamSet,
            request.SourceStream.StreamId, request.SourceStream.Generation, request.SourceEventRevision,
            request.SourceEventId, request.SourceDocument.Collection, request.SourceDocument.Id,
            request.SourceDocumentRevision, request.InputField, request.ReducerId, request.ReducerVersion,
            request.ReducerGeneration, request.Target.Collection, request.Target.Id, request.Target.Field,
            request.Target.Space.Id, request.Target.Space.Dimension, (int)request.Target.Space.Metric,
            request.Target.Space.Model, request.Target.Space.Version);

    internal static byte[] Effect(PartitionRef partition, VectorProjectionLineage lineage)
        => KeySpace.Partition(EffectSpace, partition, lineage.SourceStream.StreamSet,
            lineage.SourceStream.StreamId, lineage.SourceStream.Generation, lineage.SourceEventRevision,
            lineage.SourceEventId, lineage.SourceDocument.Collection, lineage.SourceDocument.Id,
            lineage.SourceDocumentRevision, lineage.InputField, lineage.ReducerId, lineage.ReducerVersion,
            lineage.ReducerGeneration, lineage.TargetCollection, lineage.TargetId, lineage.TargetField,
            lineage.TargetSpace.Id, lineage.TargetSpace.Dimension, (int)lineage.TargetSpace.Metric,
            lineage.TargetSpace.Model, lineage.TargetSpace.Version);
}
