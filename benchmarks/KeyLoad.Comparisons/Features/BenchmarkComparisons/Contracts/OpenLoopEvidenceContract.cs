namespace KeyLoad.Comparisons;

/// <summary>Owns immutable identities and bounds for the separate open-loop evidence format.</summary>
internal static class OpenLoopEvidenceContract
{
    internal const int SchemaVersion = 1;
    internal const int MaximumArtifactBytes = 4 * 1024 * 1024;
    internal const int JsonWriterBufferBytes = 16_384;
    internal const int Sha256HexCharacters = 64;
    internal const int GitRevisionHexCharacters = 40;
    internal const int MaximumWorkerTargetCharacters = 256;
    internal const int MaximumProfileIdCharacters = 64;
    internal const int MaximumWorkerSourceRevisionCharacters = 64;
    internal const int MaximumRepositoryCharacters = 512;
    internal const int MaximumRefCharacters = 512;
    internal const int MaximumWorkflowCharacters = 256;
    internal const int MaximumStorageDescriptionCharacters = 512;
    internal const int MaximumRuntimeDescriptionCharacters = 256;
    internal const int MaximumTargetFieldCharacters = 2_048;
    internal const int MaximumTargetMetadataBytes = 8_192;
    internal const int MaximumClusterObservations = 64;
    internal const int MaximumObservationCharacters = 1_024;
    internal const int MaximumObservationBytes = 8_192;

    internal const string OpenLoopEvidenceFileName = "open-loop-evidence.v1.json";
    internal const string PendingOpenLoopEvidenceFileName = ".open-loop-evidence.v1.json.pending";
    internal const string ProgressMessageFormat = "open-loop {0} {1}/s: {2}/{3} succeeded";
}
