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
            var policy = NativeComparisonExecutionRegistration.Read(configuration);
            return await RunNativeAsync(settings, policy, cancellationToken);
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

    private static async Task<int> RunNativeAsync(IsolatedHostSettings settings, Microsoft.Extensions.Options.IOptions<NativeComparisonExecutionOptions> policy, CancellationToken cancellationToken)
    {
        await using var owner = new IsolatedHostTargetOwner(policy);
        if (settings.Selection.VectorProfile is not null)
        {
            return await IsolatedVectorHostApplication.RunAsync(owner, settings, cancellationToken);
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

    private static void ValidateNativeReport(ComparisonReport report, IsolatedHostSettings settings)
    {
        var selection = settings.Selection;
        if (report.Targets.Length != IsolatedHostApplicationValues.SingleElementOffset || report.Targets[IsolatedHostApplicationValues.FirstIndex].Name != selection.Target
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
        if (!HasFailedCases(report) && (report.Targets[IsolatedHostApplicationValues.FirstIndex].Cluster is not { } cluster
            || cluster.Nodes != selection.NodeCount || cluster.DataCopies != selection.NodeCount))
        {
            throw new ComparisonFailureException(IsolatedHostConstants.Failure);
        }
    }

    private static bool HasFailedCases(ComparisonReport? report)
        => report is not null && report.Cases.Any(item => item.Status == ComparisonStatuses.Failed
            || item.Measurement?.Failures > IsolatedHostApplicationValues.FirstIndex || item.Samples.Any(sample => !sample.Success));
}
