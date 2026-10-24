namespace KeyLoad.Core.Search;

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
