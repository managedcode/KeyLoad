using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Uses the existing target owner for separate open-loop measurement and proof artifacts.</summary>
internal static class IsolatedOpenLoopHostApplication
{
    private const int NoFailedOperations = 0;

    internal static async Task<int> RunAsync(IsolatedHostTargetOwner owner, IsolatedHostSettings settings,
        IsolatedOpenLoopSettings openLoop, CancellationToken cancellationToken)
    {
        var target = owner.Create(settings);
        if (openLoop.CancellationProof)
        {
            _ = await OpenLoopCancellationProofRunner.RunAsync(openLoop.Profile, openLoop.Rate, target,
                settings.Worker, settings.Storage, settings.OutputDirectory, Console.WriteLine,
                openLoop.ExecutionOptions, cancellationToken).ConfigureAwait(false);
            return ComparisonHostConstants.SuccessfulExitCode;
        }

        var runner = new OpenLoopComparisonRunner(openLoop.Profile, openLoop.Rate,
            openLoop.ExecutionOptions, Console.WriteLine);
        var report = await runner.RunAsync(target, settings.Worker, settings.Storage, cancellationToken)
            .ConfigureAwait(false);
        _ = await OpenLoopEvidenceWriter.WriteAsync(settings.OutputDirectory, report, cancellationToken)
            .ConfigureAwait(false);
        return HasFailedMeasurement(report)
            ? ComparisonHostConstants.FailedExitCode : ComparisonHostConstants.SuccessfulExitCode;
    }

    private static bool HasFailedMeasurement(OpenLoopComparisonReport report)
        => report.CallerCancelled || report.DrainExpired || !report.SessionsClosed || !report.ScheduleComplete
            || report.Accounting.Failed > NoFailedOperations || report.Accounting.HarnessRejected > NoFailedOperations
            || report.Accounting.TimedOutBeforeStart > NoFailedOperations || report.Accounting.TimedOutAfterStart > NoFailedOperations
            || report.Accounting.UnfinishedQueued > NoFailedOperations || report.Accounting.UnfinishedStarted > NoFailedOperations;
}
