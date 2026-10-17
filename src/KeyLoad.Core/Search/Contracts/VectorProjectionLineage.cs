using System.Collections.Immutable;

namespace KeyLoad.Core.Features.Search;

/// <summary>Records only source identity, policy provenance and reducer metadata beside a derived vector.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(VectorProjectionNativeAliases.Lineage)]
internal sealed record VectorProjectionLineage(
    [property: global::Orleans.Id(VectorProjectionLineageFields.SourceStream)] StreamRef SourceStream,
    [property: global::Orleans.Id(VectorProjectionLineageFields.SourceEventRevision)] long SourceEventRevision,
    [property: global::Orleans.Id(VectorProjectionLineageFields.SourceEventId)] string SourceEventId,
    [property: global::Orleans.Id(VectorProjectionLineageFields.SourceDocument)] EntityRef SourceDocument,
    [property: global::Orleans.Id(VectorProjectionLineageFields.SourceDocumentRevision)] long SourceDocumentRevision,
    [property: global::Orleans.Id(VectorProjectionLineageFields.InputField)] string InputField,
    [property: global::Orleans.Id(VectorProjectionLineageFields.SourceSchemaVersion)] long SourceSchemaVersion,
    [property: global::Orleans.Id(VectorProjectionLineageFields.SourcePolicyEpoch)] long SourcePolicyEpoch,
    [property: global::Orleans.Id(VectorProjectionLineageFields.SourceClassifications)] ImmutableArray<string> SourceClassifications,
    [property: global::Orleans.Id(VectorProjectionLineageFields.TargetCollection)] string TargetCollection,
    [property: global::Orleans.Id(VectorProjectionLineageFields.TargetId)] string TargetId,
    [property: global::Orleans.Id(VectorProjectionLineageFields.TargetField)] string TargetField,
    [property: global::Orleans.Id(VectorProjectionLineageFields.TargetSpace)] VectorSpace TargetSpace,
    [property: global::Orleans.Id(VectorProjectionLineageFields.TargetSchemaVersion)] long TargetSchemaVersion,
    [property: global::Orleans.Id(VectorProjectionLineageFields.ReducerId)] string ReducerId,
    [property: global::Orleans.Id(VectorProjectionLineageFields.ReducerVersion)] string ReducerVersion,
    [property: global::Orleans.Id(VectorProjectionLineageFields.ReducerGeneration)] long ReducerGeneration,
    [property: global::Orleans.Id(VectorProjectionLineageFields.TargetClassifications)] ImmutableArray<string> TargetClassifications);
