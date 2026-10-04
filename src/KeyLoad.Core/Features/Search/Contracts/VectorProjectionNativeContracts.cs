using System.Collections.Immutable;

namespace KeyLoad.Core.Features.Search;

/// <summary>Names stable native identities for persisted vector projection records.</summary>
internal static class VectorProjectionNativeAliases
{
    internal const string Lineage = "keyload.core.vector-projection-lineage.v1";
    internal const string Effect = "keyload.core.vector-projection-effect.v1";
}

/// <summary>Stable field identities for one vector's private projection lineage.</summary>
internal static class VectorProjectionLineageFields
{
    internal const uint SourceStream = 0;
    internal const uint SourceEventRevision = 1;
    internal const uint SourceEventId = 2;
    internal const uint SourceDocument = 3;
    internal const uint SourceDocumentRevision = 4;
    internal const uint InputField = 5;
    internal const uint SourceSchemaVersion = 6;
    internal const uint SourcePolicyEpoch = 7;
    internal const uint SourceClassifications = 8;
    internal const uint TargetCollection = 9;
    internal const uint TargetId = 10;
    internal const uint TargetField = 11;
    internal const uint TargetSpace = 12;
    internal const uint TargetSchemaVersion = 13;
    internal const uint ReducerId = 14;
    internal const uint ReducerVersion = 15;
    internal const uint ReducerGeneration = 16;
    internal const uint TargetClassifications = 17;
}

/// <summary>Stable field identities for idempotent projection effects.</summary>
internal static class VectorProjectionEffectFields
{
    internal const uint Fingerprint = 0;
    internal const uint Receipt = 1;
}

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

/// <summary>Retains the exact effect fingerprint and stable mutation receipt for an active projection.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(VectorProjectionNativeAliases.Effect)]
internal sealed record VectorProjectionEffect(
    [property: global::Orleans.Id(VectorProjectionEffectFields.Fingerprint)] string Fingerprint,
    [property: global::Orleans.Id(VectorProjectionEffectFields.Receipt)] MutationReceipt Receipt);
