using System.Globalization;
using System.Text.Json;
using KeyLoad.Comparisons;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class OpenLoopResourceArtifactDecoder
{
    internal static void ValidateSelection(ComparisonWorkerSelection selection, OpenLoopResourceSelection openLoop)
    {
        if (selection.ScaledProfile is null || selection.Profile != selection.ScaledProfile.Id
            || selection.OpenLoopRate != openLoop.OfferedRatePerSecond
            || !OpenLoopRateSelection.IsSupported(openLoop.OfferedRatePerSecond)
            || (openLoop.CancellationProof && selection.Scenario != Scenario.PointRead))
        {
            throw InvalidIdentity();
        }
        try
        {
            if (ScaledComparisonProfileParser.Parse(selection.Profile) != selection.ScaledProfile)
            {
                throw InvalidIdentity();
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            throw InvalidIdentity();
        }
        selection.Validate();
    }

    internal static IsolatedComparisonWorker ReadWorker(byte[] bytes, ComparisonWorkerSelection selection,
        OpenLoopResourceSelection openLoop)
        => openLoop.CancellationProof ? ReadProof(bytes, selection, openLoop)
            : ReadMeasurement(bytes, selection, openLoop);

    private static IsolatedComparisonWorker ReadMeasurement(byte[] bytes, ComparisonWorkerSelection selection,
        OpenLoopResourceSelection openLoop)
    {
        OpenLoopComparisonReport? report;
        try
        {
            report = JsonSerializer.Deserialize<OpenLoopComparisonReport>(bytes, ReportWriter.JsonOptions);
        }
        catch (JsonException)
        {
            throw InvalidArtifact();
        }
        catch (NotSupportedException)
        {
            throw InvalidArtifact();
        }

        if (report is null || report.Worker is null || report.Target is null || report.Accounting is null
            || report.Timing is null || report.ExecutionPolicy is null || report.Samples.IsDefault
            || report.DatasetSha256 is null || report.Target.Cluster?.Observations.IsDefault == true)
        {
            throw InvalidArtifact();
        }

        try
        {
            OpenLoopEvidenceValidation.Validate(report);
        }
        catch (ComparisonFailureException)
        {
            throw InvalidIdentity();
        }
        catch (NullReferenceException)
        {
            throw InvalidArtifact();
        }

        var worker = report.Worker;
        if (report.OfferedRatePerSecond != openLoop.OfferedRatePerSecond
            || report.ProfileId != selection.Profile || report.Scenario != selection.Scenario
            || report.Target.Name != selection.Target || worker.NodeCount != selection.NodeCount
            || worker.Target != selection.Target || worker.Profile != selection.Profile
            || worker.Scenario != selection.Scenario)
        {
            throw InvalidIdentity();
        }
        return worker;
    }

    private static IsolatedComparisonWorker ReadProof(byte[] bytes, ComparisonWorkerSelection selection,
        OpenLoopResourceSelection openLoop)
    {
        OpenLoopCancellationProofV1? proof;
        try
        {
            proof = JsonSerializer.Deserialize<OpenLoopCancellationProofV1>(bytes, ReportWriter.JsonOptions);
        }
        catch (JsonException)
        {
            throw InvalidArtifact();
        }
        catch (NotSupportedException)
        {
            throw InvalidArtifact();
        }

        if (proof is null)
        {
            throw InvalidArtifact();
        }
        try
        {
            OpenLoopCancellationProofValidation.Validate(proof);
        }
        catch (ComparisonFailureException)
        {
            throw InvalidIdentity();
        }
        catch (ArgumentOutOfRangeException)
        {
            throw InvalidArtifact();
        }
        catch (NullReferenceException)
        {
            throw InvalidArtifact();
        }

        var worker = proof.Worker;
        if (!openLoop.CancellationProof || proof.Rate != openLoop.OfferedRatePerSecond
            || proof.ProfileId != selection.Profile || proof.Scenario != selection.Scenario
            || worker.Target != selection.Target || worker.NodeCount != selection.NodeCount
            || worker.Profile != selection.Profile || worker.Scenario != selection.Scenario)
        {
            throw InvalidIdentity();
        }
        return worker;
    }

    internal static void ValidateProvenance(IsolatedComparisonWorker worker, BenchmarkProvenanceOptions provenance)
    {
        if (string.IsNullOrWhiteSpace(provenance.SourceRevision)
            || provenance.SourceRevision.Length != OpenLoopEvidenceContract.GitRevisionHexCharacters
            || !provenance.SourceRevision.All(Uri.IsHexDigit)
            || provenance.SourceRevision != worker.SourceRevision
            || !Matches(provenance.WorkflowRunId, worker.RunId)
            || !Matches(provenance.RunAttempt, worker.Attempt)
            || !Matches(provenance.JobId, worker.JobId))
        {
            throw InvalidIdentity();
        }
    }

    private static bool Matches(string value, long expected)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            && parsed == expected && value == expected.ToString(CultureInfo.InvariantCulture);

    private static InvalidDataException InvalidArtifact()
        => new(OpenLoopResourceEvidenceContract.InvalidSourceArtifact);

    private static InvalidDataException InvalidIdentity()
        => new(OpenLoopResourceEvidenceContract.InvalidSourceIdentity);
}
