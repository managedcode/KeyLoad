using KeyLoad.Comparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class OpenLoopResourceEvidenceWriter
{
    private const int NoMissingEvidence = 0;
    private const int NoRecordedSamples = 0;

    internal static async Task<string> WriteAsync(ComparisonWorkerSelection selection,
        OpenLoopResourceSelection openLoop, string output, ScaleServerObservationSnapshot observations,
        IOptions<ScaleServerResourceOptions> resourceOptions,
        IOptions<BenchmarkProvenanceOptions> provenanceOptions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(openLoop);
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(resourceOptions);
        ArgumentNullException.ThrowIfNull(provenanceOptions);
        cancellationToken.ThrowIfCancellationRequested();

        var settings = resourceOptions.Value;
        settings.Validate();
        var provenance = provenanceOptions.Value;
        var snapshot = Snapshot(observations);
        ValidateSnapshot(snapshot, selection, settings, resourceOptions);
        var artifact = await OpenLoopResourceArtifactReader.ReadAsync(selection, openLoop, output,
            provenance, settings.NativeReadBufferBytes, cancellationToken).ConfigureAwait(false);
        var document = CreateEvidence(artifact, snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(output);
        var finalPath = Path.Combine(output, OpenLoopResourceEvidenceContract.SidecarFileName);
        var pendingPath = Path.Combine(output, OpenLoopResourceEvidenceContract.PendingSidecarFileName);
        return await OpenLoopEvidenceArtifactWriter.WriteAsync(pendingPath, finalPath, document,
            settings.MaxSidecarBytes, settings.NativeReadBufferBytes, cancellationToken).ConfigureAwait(false);
    }

    private static ScaleServerObservationSnapshot Snapshot(ScaleServerObservationSnapshot value)
    {
        if (value.Containers is null || value.MissingEvidence is null || value.ObservationPolicy is null
            || value.Containers.Any(container => container is null || container.WritableMounts is null))
        {
            throw InvalidSnapshot();
        }
        return value with
        {
            Containers = value.Containers.Select(container => container with
                { WritableMounts = container.WritableMounts.ToArray() }).ToArray(),
            MissingEvidence = value.MissingEvidence.ToArray()
        };
    }

    private static void ValidateSnapshot(ScaleServerObservationSnapshot snapshot,
        ComparisonWorkerSelection selection, ScaleServerResourceOptions settings,
        IOptions<ScaleServerResourceOptions> resourceOptions)
    {
        var policy = ScaleServerObservationPolicySnapshot.Capture(resourceOptions);
        if (snapshot.Containers.Length > ScaleServerResourceBounds.MaxContainers
            || snapshot.Containers.Length > selection.NodeCount
            || snapshot.MissingEvidence.Length > OpenLoopResourceEvidenceContract.MissingEvidenceCategories
            || snapshot.ObservationPolicy != policy
            || snapshot.Qualified != (snapshot.MissingEvidence.Length == NoMissingEvidence)
            || snapshot.MissingEvidence.Distinct(StringComparer.Ordinal).Count() != snapshot.MissingEvidence.Length
            || !snapshot.MissingEvidence.SequenceEqual(snapshot.MissingEvidence.Order(StringComparer.Ordinal))
            || snapshot.MissingEvidence.Any(category => !IsMissingCategory(category)))
        {
            throw InvalidSnapshot();
        }

        foreach (var container in snapshot.Containers)
        {
            if (container.WritableMounts.Length > settings.MaxMounts
                || container.SampleCount < NoRecordedSamples || container.SampleCount > settings.MaxSamples
                || (snapshot.Qualified && (container.SampleCount < ScaleServerResourceBounds.MinimumSamples
                    || container.WritableMounts.Length == OpenLoopResourceEvidenceContract.NoWritableMounts)))
            {
                throw InvalidSnapshot();
            }
        }

        if (snapshot.Qualified && (snapshot.Hardware is null || snapshot.AppHostEnvelope is null
            || snapshot.Containers.Length != selection.NodeCount))
        {
            throw InvalidSnapshot();
        }
    }

    private static bool IsMissingCategory(string value)
        => value is ScaleServerResourceBounds.HardwareMissing or ScaleServerResourceBounds.EnvelopeMissing
            or ScaleServerResourceBounds.StorageMissing or ScaleServerResourceBounds.SamplingMissing;

    private static OpenLoopServerResourceEvidenceV1 CreateEvidence(OpenLoopResourceArtifact artifact,
        ScaleServerObservationSnapshot observations)
    {
        var worker = artifact.Worker;
        return new(OpenLoopResourceEvidenceContract.Schema, artifact.Kind, artifact.Name, artifact.Sha256,
            artifact.OfferedRatePerSecond, worker.SourceRevision, worker.RunId, worker.Attempt, worker.JobId,
            worker.Target, worker.NodeCount, worker.Scenario.ToString(), worker.Profile, observations.Hardware,
            observations.AppHostEnvelope, observations.Containers, observations.MissingEvidence,
            observations.Qualified, observations.ObservationPolicy);
    }

    private static InvalidDataException InvalidSnapshot()
        => new(OpenLoopResourceEvidenceContract.InvalidObservationSnapshot);
}
