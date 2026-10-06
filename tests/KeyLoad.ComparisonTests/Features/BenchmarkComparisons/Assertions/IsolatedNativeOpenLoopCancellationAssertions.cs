using System.Text.Json;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedNativeOpenLoopCancellationAssertions
{
    internal static async Task VerifyAsync(string output, ComparisonWorkerSelection selection,
        int expectedRate, OpenLoopNativeCompletionV1 observedMarker,
        IOptions<BenchmarkProvenanceOptions> provenanceOptions, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(observedMarker);
        var file = new FileInfo(Path.Combine(output, OpenLoopCancellationProofContract.ProofFileName));
        await Assert.That(file.Exists && file.Length is >= OpenLoopNativeTestOracle.MinimumArtifactBytes
            and <= OpenLoopCancellationProofContract.MaximumProofBytes)
            .IsTrue();
        await Assert.That(File.Exists(Path.Combine(output, OpenLoopEvidenceContract.OpenLoopEvidenceFileName))).IsFalse();
        await Assert.That((File.GetAttributes(file.FullName) & FileAttributes.ReparsePoint) == OpenLoopNativeTestOracle.NoFileAttributes).IsTrue();
        await using var stream = file.OpenRead();
        var proof = await JsonSerializer.DeserializeAsync<OpenLoopCancellationProofV1>(stream,
            ReportWriter.JsonOptions, cancellationToken) ?? throw new InvalidDataException(OpenLoopNativeTestOracle.ProofMissing);
        OpenLoopCancellationProofValidationForTests.Validate(proof, selection, expectedRate, observedMarker, provenanceOptions);
    }
}
