using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Runs a single selected native workload and emits its exact source/run/job envelope.</summary>
internal static class IsolatedHostApplication
{
    internal static async Task<int> RunAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        try
        {
            return await RunSelectedAsync(configuration, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync(IsolatedHostConstants.Cancelled);
            return ComparisonHostConstants.FailedExitCode;
        }
        catch (ComparisonFailureException)
        {
            await Console.Error.WriteLineAsync(IsolatedHostConstants.Failure);
            return ComparisonHostConstants.FailedExitCode;
        }
    }

    private static async Task<int> RunSelectedAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var settings = IsolatedHostSettings.Read(configuration);
            IsolatedHostReportWriter.ValidateDestination(settings.OutputDirectory);
            if (settings.UnsupportedReason is { } reason)
            {
                var report = new IsolatedComparisonReport(IsolatedComparisonContract.Current.WorkerSchemaVersion, settings.Worker,
                    IsolatedHostConstants.UnsupportedTopology, reason, null);
                await IsolatedHostReportWriter.WriteAsync(report, settings.OutputDirectory, cancellationToken);
                return ComparisonHostConstants.SuccessfulExitCode;
            }
            return await RunNativeAsync(settings, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new ComparisonFailureException(IsolatedHostConstants.Failure);
        }
    }

    private static async Task<int> RunNativeAsync(IsolatedHostSettings settings, CancellationToken cancellationToken)
    {
        await using var owner = new IsolatedHostTargetOwner();
        if (settings.Selection.VectorProfile is { } vectorProfile)
        {
            var vectorTarget = owner.CreateVector(settings);
            var vectorReport = await new VectorComparisonRunner(vectorProfile)
                .RunAsync(vectorTarget, settings.Worker.SourceRevision, settings.Storage, cancellationToken);
            vectorReport = vectorReport with
            {
                Provenance = settings.Identity.Provenance,
                LoadGeneratorImage = settings.Identity.LoadGeneratorImage
            };
            ValidateNativeVectorReport(vectorReport, settings);
            var vectorEnvelope = new IsolatedComparisonReport(IsolatedComparisonContract.Current.WorkerSchemaVersion,
                settings.Worker, IsolatedHostConstants.Measured, null, vectorReport);
            await IsolatedHostReportWriter.WriteAsync(vectorEnvelope, settings.OutputDirectory, cancellationToken);
            return HasFailedCases(vectorReport) ? ComparisonHostConstants.FailedExitCode : ComparisonHostConstants.SuccessfulExitCode;
        }

        var target = owner.Create(settings);
        var runner = settings.Selection.ScaledProfile is { } scaledProfile
            ? ComparisonRunner.ForScaled(scaledProfile, Console.WriteLine)
            : new ComparisonRunner(settings.Selection.Options, Console.WriteLine);
        var report = await runner.RunAsync([target], settings.Worker.SourceRevision, cancellationToken,
            settings.Storage, settings.Selection.Scenario);
        report = report with { Provenance = settings.Identity.Provenance, LoadGeneratorImage = settings.Identity.LoadGeneratorImage };
        ValidateNativeReport(report, settings);
        var envelope = new IsolatedComparisonReport(IsolatedComparisonContract.Current.WorkerSchemaVersion,
            settings.Worker, IsolatedHostConstants.Measured, null, report);
        await IsolatedHostReportWriter.WriteAsync(envelope, settings.OutputDirectory, cancellationToken);
        return HasFailedCases(report) ? ComparisonHostConstants.FailedExitCode : ComparisonHostConstants.SuccessfulExitCode;
    }

    private static void ValidateNativeVectorReport(ComparisonReport report, IsolatedHostSettings settings)
    {
        var selection = settings.Selection;
        if (selection.VectorProfile is not { } profile || report.Targets.Length != 1
            || report.Targets[0].Name != selection.Target || report.Cases.Length != 1
            || report.Cases[0].Target != selection.Target || report.Cases[0].VectorMetrics is null
            || report.VectorProfile?.Id != profile.Id || report.Options is not null || report.ScaledProfile is not null
            || report.Targets[0].Cluster is not { } cluster || cluster.Nodes != selection.NodeCount
            || cluster.DataCopies != selection.NodeCount)
        {
            throw new ComparisonFailureException(IsolatedHostConstants.Failure);
        }
    }

    private static void ValidateNativeReport(ComparisonReport report, IsolatedHostSettings settings)
    {
        var selection = settings.Selection;
        if (report.Targets.Length != 1 || report.Targets[0].Name != selection.Target
            || report.Cases.Length != (selection.ScaledProfile?.Repetitions ?? selection.Options.Repetitions)
            || report.Cases.Any(item => item.Target != selection.Target || item.Scenario != selection.Scenario))
        {
            throw new ComparisonFailureException(IsolatedHostConstants.Failure);
        }
        if (selection.ScaledProfile is { } scaledProfile
            && (report.Options is not null || report.ScaledProfile?.Id != scaledProfile.Id))
        {
            throw new ComparisonFailureException(IsolatedHostConstants.Failure);
        }
        if (!HasFailedCases(report) && (report.Targets[0].Cluster is not { } cluster
            || cluster.Nodes != selection.NodeCount || cluster.DataCopies != selection.NodeCount))
        {
            throw new ComparisonFailureException(IsolatedHostConstants.Failure);
        }
    }

    private static bool HasFailedCases(ComparisonReport? report)
        => report is not null && report.Cases.Any(item => item.Status == ComparisonStatuses.Failed
            || item.Measurement?.Failures > 0 || item.Samples.Any(sample => !sample.Success));
}
