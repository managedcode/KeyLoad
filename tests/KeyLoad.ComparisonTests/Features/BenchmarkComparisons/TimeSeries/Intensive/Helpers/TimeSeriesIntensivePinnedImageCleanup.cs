using System.Text;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed record TimeSeriesIntensivePinnedImageCleanupResult(bool Completed, IReadOnlyList<string> Failures);

internal static class TimeSeriesIntensivePinnedImageCleanup
{
    internal static async Task<TimeSeriesIntensivePinnedImageCleanupResult> RunAsync(
        TimeSeriesIntensivePinnedImageEvidence evidence, string name, string nonce, Exception? primary)
    {
        var failures = new List<string>();
        var inspection = await AttemptAsync(evidence, TimeSeriesIntensivePinnedImageFields.CleanupInspect,
            [TimeSeriesIntensivePinnedImageFields.ContainerCommand, TimeSeriesIntensivePinnedImageFields.InspectCommand,
                TimeSeriesIntensivePinnedImageFields.Format, TimeSeriesIntensivePinnedImageFields.JsonFormat, name], failures, primary);
        var id = OwnedId(inspection, name, nonce, failures, primary);
        if (id is not null)
        {
            await AttemptAsync(evidence, TimeSeriesIntensivePinnedImageFields.CleanupLogs,
                [TimeSeriesIntensivePinnedImageFields.LogsCommand, id], failures, primary);
            await AttemptAsync(evidence, TimeSeriesIntensivePinnedImageFields.CleanupRemove,
                [TimeSeriesIntensivePinnedImageFields.RemoveCommand, TimeSeriesIntensivePinnedImageFields.ForceFlag,
                    TimeSeriesIntensivePinnedImageFields.VolumesFlag, id], failures, primary);
        }

        var absence = await AttemptAsync(evidence, TimeSeriesIntensivePinnedImageFields.CleanupAbsence,
            [TimeSeriesIntensivePinnedImageFields.ContainerCommand, TimeSeriesIntensivePinnedImageFields.ListCommand,
                TimeSeriesIntensivePinnedImageFields.AllFlag, TimeSeriesIntensivePinnedImageFields.QuietFlag,
                TimeSeriesIntensivePinnedImageFields.FilterFlag, TimeSeriesIntensivePinnedImageFields.LabelFilter
                + TimeSeriesIntensivePinnedImageProtocol.OwnershipLabel + '=' + nonce], failures, primary);
        var completed = absence?.ExitCode == 0 && Encoding.UTF8.GetString(absence.Output).Trim().Length == 0;
        if (!completed)
        {
            failures.Add(TimeSeriesIntensivePinnedImageFields.CleanupAbsence);
        }

        return new(completed && failures.Count == 0, failures);
    }

    private static string? OwnedId(TimeSeriesIntensivePinnedImageCommand? command, string name,
        string nonce, List<string> failures, Exception? primary)
    {
        if (command is null || command.ExitCode != 0)
        {
            return null;
        }

        try
        {
            return TimeSeriesIntensivePinnedImageInspection.ReadContainer(command.Output, name, nonce);
        }
        catch (Exception error) when (primary is not null || TimeSeriesIntensivePinnedImageFailure.IsNative(error))
        {
            failures.Add(error.GetType().FullName ?? nameof(Exception));
            return null;
        }
    }

    private static async Task<TimeSeriesIntensivePinnedImageCommand?> AttemptAsync(
        TimeSeriesIntensivePinnedImageEvidence evidence, string label, IReadOnlyList<string> arguments,
        List<string> failures, Exception? primary)
    {
        try
        {
            var result = await TimeSeriesIntensivePinnedImageProcess.RunAsync(TimeSeriesIntensivePinnedImageProtocol.Docker,
                arguments, TimeSeriesIntensivePinnedImageProtocol.OperationSeconds, CancellationToken.None, evidence, label);
            if (result.ExitCode != 0 && label != TimeSeriesIntensivePinnedImageFields.CleanupInspect)
            {
                failures.Add(label);
            }

            return result;
        }
        catch (Exception error) when (primary is not null || TimeSeriesIntensivePinnedImageFailure.IsNative(error))
        {
            failures.Add(label + ':' + (error.GetType().FullName ?? nameof(Exception)));
            return null;
        }
    }
}
