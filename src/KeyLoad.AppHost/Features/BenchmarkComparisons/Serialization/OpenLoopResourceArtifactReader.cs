using System.Security.Cryptography;
using KeyLoad.Comparisons;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class OpenLoopResourceArtifactReader
{
    internal static async Task<OpenLoopResourceArtifact> ReadAsync(ComparisonWorkerSelection selection,
        OpenLoopResourceSelection openLoop, string output, BenchmarkProvenanceOptions provenance,
        int bufferBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(openLoop);
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        ArgumentNullException.ThrowIfNull(provenance);
        cancellationToken.ThrowIfCancellationRequested();

        OpenLoopResourceArtifactDecoder.ValidateSelection(selection, openLoop);
        var (kind, name) = ArtifactIdentity(openLoop);
        var bytes = await OpenLoopResourceArtifactBytes.ReadAsync(Path.Combine(output, name), bufferBytes,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var worker = OpenLoopResourceArtifactDecoder.ReadWorker(bytes, selection, openLoop);
        OpenLoopResourceArtifactDecoder.ValidateProvenance(worker, provenance);
        return new(kind, name, digest, openLoop.OfferedRatePerSecond, worker);
    }

    private static (string Kind, string Name) ArtifactIdentity(OpenLoopResourceSelection selection)
        => selection.CancellationProof
            ? (OpenLoopResourceEvidenceContract.CancellationProofKind,
                OpenLoopResourceEvidenceContract.CancellationProofArtifactName)
            : (OpenLoopResourceEvidenceContract.MeasurementKind,
                OpenLoopResourceEvidenceContract.MeasurementArtifactName);
}
