
namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal sealed record OpenLoopResourceSelection(int OfferedRatePerSecond, bool CancellationProof);

internal sealed record OpenLoopServerResourceEvidenceV1(string Schema, string ArtifactKind,
    string ArtifactName, string ArtifactSha256, int OfferedRatePerSecond, string SourceRevision,
    long RunId, int Attempt, long JobId, string Target, int NodeCount, string Scenario, string Profile,
    ScaleServerHardware? Hardware, ScaleServerEnvelope? AppHostEnvelope, ScaleServerContainer[] Containers,
    string[] MissingEvidence, bool Qualified, ScaleServerObservationPolicy ObservationPolicy);

internal static class OpenLoopResourceEvidenceContract
{
    internal const string Schema = "open-loop-server-resource-evidence.v1";
    internal const string MeasurementKind = "measurement";
    internal const string CancellationProofKind = "cancellation-proof";
    internal const string MeasurementArtifactName = "open-loop-evidence.v1.json";
    internal const string CancellationProofArtifactName = "open-loop-cancellation-proof.v1.json";
    internal const string SidecarFileName = "open-loop-server-resource-evidence.v1.json";
    internal const string PendingSidecarFileName = ".open-loop-server-resource-evidence.v1.json.pending";
    internal const int ExcessArtifactProbeBytes = 1;
    internal const int MissingEvidenceCategories = 4;
    internal const int NoWritableMounts = 0;
    internal const string InvalidSourceArtifact = "The open-loop source artifact is invalid.";
    internal const string InvalidSourceIdentity = "The open-loop source artifact identity does not match its selected cell.";
    internal const string InvalidObservationSnapshot = "The server resource observation snapshot is invalid.";
}
