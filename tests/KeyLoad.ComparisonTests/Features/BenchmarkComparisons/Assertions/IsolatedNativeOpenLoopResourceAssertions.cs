using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedNativeOpenLoopResourceAssertions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static async Task VerifyAsync(string output, IsolatedNativeCasePlan plan,
        IOptions<ScaleServerResourceOptions> options, IOptions<BenchmarkProvenanceOptions> provenanceOptions,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(provenanceOptions);
        if (!plan.OpenLoop || plan.Selection.OpenLoopRate is not { } rate)
        {
            throw new InvalidOperationException(OpenLoopNativeTestOracle.SidecarInvalid);
        }

        var settings = options.Value;
        settings.Validate();
        var evidence = await ReadEvidenceAsync(output, settings.MaxSidecarBytes,
            settings.NativeReadBufferBytes, cancellationToken).ConfigureAwait(false);
        VerifyCell(evidence, plan.Selection, rate, plan.CancellationProof);
        var name = plan.CancellationProof ? OpenLoopResourceEvidenceContract.CancellationProofArtifactName
            : OpenLoopResourceEvidenceContract.MeasurementArtifactName;
        var bytes = await OpenLoopResourceArtifactBytes.ReadAsync(Path.Combine(output, name),
            settings.NativeReadBufferBytes, cancellationToken).ConfigureAwait(false);
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        await Assert.That(evidence.ArtifactSha256).IsEqualTo(digest);
        var worker = OpenLoopResourceArtifactDecoder.ReadWorker(bytes, plan.Selection,
            new OpenLoopResourceSelection(rate, plan.CancellationProof));
        VerifyWorker(evidence, worker, provenanceOptions);
        await IsolatedNativeServerObservationAssertions.VerifyAsync(evidence, plan.Selection, options);
    }

    private static async Task<OpenLoopServerResourceEvidenceV1> ReadEvidenceAsync(string output,
        int maximumBytes, int bufferBytes, CancellationToken cancellationToken)
    {
        var bytes = await ReadBoundedAsync(Path.Combine(output, OpenLoopResourceEvidenceContract.SidecarFileName),
            maximumBytes, bufferBytes, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<OpenLoopServerResourceEvidenceV1>(bytes, JsonOptions)
            ?? throw new InvalidDataException(OpenLoopNativeTestOracle.SidecarInvalid);
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, int maximumBytes,
        int bufferBytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var length = stream.Length;
        if (length <= OpenLoopNativeTestOracle.NoObservedBytes || length > maximumBytes)
        {
            throw new InvalidDataException(OpenLoopNativeTestOracle.SidecarInvalid);
        }
        var bytes = new byte[checked((int)length)];
        var read = OpenLoopNativeTestOracle.FirstIndex;
        while (read < bytes.Length)
        {
            var count = await stream.ReadAsync(bytes.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (count == OpenLoopNativeTestOracle.NoObservedBytes)
            {
                throw new InvalidDataException(OpenLoopNativeTestOracle.SidecarInvalid);
            }
            read = checked(read + count);
        }
        var probe = new byte[OpenLoopResourceEvidenceContract.ExcessArtifactProbeBytes];
        if (await stream.ReadAsync(probe, cancellationToken).ConfigureAwait(false)
            != OpenLoopNativeTestOracle.NoObservedBytes)
        {
            throw new InvalidDataException(OpenLoopNativeTestOracle.SidecarInvalid);
        }
        return bytes;
    }

    private static void VerifyCell(OpenLoopServerResourceEvidenceV1 evidence,
        ComparisonWorkerSelection selection, int rate, bool cancellationProof)
    {
        var expectedKind = cancellationProof ? OpenLoopResourceEvidenceContract.CancellationProofKind
            : OpenLoopResourceEvidenceContract.MeasurementKind;
        var expectedName = cancellationProof ? OpenLoopResourceEvidenceContract.CancellationProofArtifactName
            : OpenLoopResourceEvidenceContract.MeasurementArtifactName;
        if (evidence.Schema != OpenLoopResourceEvidenceContract.Schema
            || evidence.ArtifactKind != expectedKind || evidence.ArtifactName != expectedName
            || evidence.OfferedRatePerSecond != rate || evidence.Target != selection.Target
            || evidence.NodeCount != selection.NodeCount || evidence.Scenario != selection.Scenario.ToString()
            || evidence.Profile != selection.Profile || evidence.Containers is null || evidence.MissingEvidence is null)
        {
            throw new InvalidDataException(OpenLoopNativeTestOracle.SidecarInvalid);
        }
    }

    private static void VerifyWorker(OpenLoopServerResourceEvidenceV1 evidence, IsolatedComparisonWorker worker,
        IOptions<BenchmarkProvenanceOptions> provenanceOptions)
    {
        var original = provenanceOptions.Value;
        if (evidence.SourceRevision != worker.SourceRevision
            || evidence.RunId != worker.RunId || evidence.Attempt != worker.Attempt || evidence.JobId != worker.JobId
            || evidence.SourceRevision != Required(original.SourceRevision)
            || evidence.RunId != PositiveInt64(original.WorkflowRunId)
            || evidence.Attempt != PositiveInt32(original.RunAttempt)
            || evidence.JobId != PositiveInt64(original.JobId)
            || evidence.Target != worker.Target || evidence.NodeCount != worker.NodeCount
            || evidence.Profile != worker.Profile || evidence.Scenario != worker.Scenario.ToString())
        {
            throw new InvalidDataException(OpenLoopNativeTestOracle.SidecarInvalid);
        }
    }

    private static string Required(string? value)
        => OpenLoopNativeProvenanceValues.IsPresent(value) ? value!
            : throw new InvalidOperationException(OpenLoopNativeTestOracle.SidecarInvalid);

    private static long PositiveInt64(string? value)
        => OpenLoopNativeProvenanceValues.TryPositiveInt64(value, out var parsed) ? parsed
            : throw new InvalidOperationException(OpenLoopNativeTestOracle.SidecarInvalid);

    private static int PositiveInt32(string? value)
        => OpenLoopNativeProvenanceValues.TryPositiveInt32(value, out var parsed) ? parsed
            : throw new InvalidOperationException(OpenLoopNativeTestOracle.SidecarInvalid);
}

internal static class IsolatedNativeServerObservationAssertions
{
    internal static async Task VerifyAsync(OpenLoopServerResourceEvidenceV1 evidence,
        ComparisonWorkerSelection selection, IOptions<ScaleServerResourceOptions> options)
    {
        await VerifyObservationsAsync(evidence.Hardware, evidence.AppHostEnvelope, evidence.Containers,
            evidence.MissingEvidence, evidence.Qualified, evidence.ObservationPolicy, selection, options);
    }

    internal static async Task VerifyAsync(ScaleServerResourceEvidence evidence,
        ComparisonWorkerSelection selection, IOptions<ScaleServerResourceOptions> options)
    {
        await VerifyObservationsAsync(evidence.Hardware, evidence.AppHostEnvelope, evidence.Containers,
            evidence.MissingEvidence, evidence.Qualified, evidence.ObservationPolicy, selection, options);
    }

    private static async Task VerifyObservationsAsync(ScaleServerHardware? hardware,
        ScaleServerEnvelope? envelope, ScaleServerContainer[]? containers, string[]? missing, bool qualified,
        ScaleServerObservationPolicy? observationPolicy, ComparisonWorkerSelection selection,
        IOptions<ScaleServerResourceOptions> options)
    {
        var settings = options.Value;
        var policy = ScaleServerObservationPolicySnapshot.Capture(options);
        if (observationPolicy is null || containers is null || missing is null || observationPolicy != policy
            || missing.Length > OpenLoopNativeTestOracle.MaximumMissingCategories
            || qualified != (missing.Length == OpenLoopNativeTestOracle.NoMissingCategories)
            || !ValidMissing(missing) || containers.Length > selection.NodeCount
            || containers.Length > ScaleServerResourceBounds.MaxContainers
            || containers.Any(item => item is null || item.WritableMounts is null))
        {
            throw new InvalidDataException(OpenLoopNativeTestOracle.ObservationsInvalid);
        }
        await VerifyContainerBoundsAsync(containers, qualified, selection, settings);
        if (containers.Length == selection.NodeCount
            && containers.All(item => item.SampleCount >= ScaleServerResourceBounds.MinimumSamples))
        {
            await Assert.That(missing).DoesNotContain(ScaleServerResourceBounds.SamplingMissing);
        }
        if (qualified)
        {
            await Assert.That(hardware).IsNotNull();
            await Assert.That(envelope).IsNotNull();
            await Assert.That(containers.Length).IsEqualTo(selection.NodeCount);
        }
    }

    private static async Task VerifyContainerBoundsAsync(ScaleServerContainer[] containers, bool qualified,
        ComparisonWorkerSelection selection, ScaleServerResourceOptions settings)
    {
        foreach (var container in containers)
        {
            await Assert.That(container.SampleCount is >= OpenLoopNativeTestOracle.NoObservedSamples && container.SampleCount <= settings.MaxSamples).IsTrue();
            await Assert.That(container.WritableMounts.Length <= settings.MaxMounts).IsTrue();
            if (qualified)
            {
                await Assert.That(container.SampleCount >= ScaleServerResourceBounds.MinimumSamples).IsTrue();
                await Assert.That(container.ContainerId is { Length: OpenLoopEvidenceContract.Sha256HexCharacters }).IsTrue();
                await Assert.That(container.ImageId is { Length: >= OpenLoopNativeTestOracle.MinimumArtifactBytes }).IsTrue();
                await Assert.That(container.WritableMounts.Length > OpenLoopNativeTestOracle.NoWritableMounts).IsTrue();
            }
        }
        await Assert.That(containers.Length <= selection.NodeCount).IsTrue();
    }

    private static bool ValidMissing(string[] values)
    {
        var ordered = values.Order(StringComparer.Ordinal).ToArray();
        return values.Distinct(StringComparer.Ordinal).Count() == values.Length
            && values.SequenceEqual(ordered)
            && values.All(value => value is ScaleServerResourceBounds.HardwareMissing
                or ScaleServerResourceBounds.EnvelopeMissing or ScaleServerResourceBounds.StorageMissing
                or ScaleServerResourceBounds.SamplingMissing);
    }
}
