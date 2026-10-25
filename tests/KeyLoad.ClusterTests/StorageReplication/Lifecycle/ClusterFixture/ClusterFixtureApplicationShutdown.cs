using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Diagnostics.ResourceExecution;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.StorageReplication;

internal static class ClusterFixtureApplicationShutdown
{
    internal static async Task<bool> CollectAsync(DistributedApplication? owned, IReadOnlyCollection<string> names,
        string? output, List<Exception> failures, CancellationToken token)
    {
        if (owned is null)
        {
            return true;
        }
        ProcessEvidence[] observed = [];
        if (output is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
                observed = await ClusterFixtureApplicationStartup.CaptureAsync(owned, names, output,
                    "execution-process-before-shutdown.json", token).ConfigureAwait(false), failures).ConfigureAwait(false);
        }
        var stopped = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await owned.StopAsync(token).ConfigureAwait(false);
            stopped = true;
        }, failures).ConfigureAwait(false);
        if (stopped && observed.Length == names.Count && output is not null)
        {
            await ServerFailureObserver.ObserveAsync(
                () => WriteJoinedAsync(owned, observed, output, token), failures).ConfigureAwait(false);
        }
        return stopped;
    }

    internal static Task WriteJoinedAsync(DistributedApplication application, ProcessEvidence[] originals,
        string output, CancellationToken token)
    {
        var resources = originals.Select(original =>
        {
            ProcessRuntimeKilledOwnerValidation.RequireExited(original);
            if (!application.ResourceNotifications.TryGetCurrentState(original.Name, out var actual)
                || actual.Resource is not ProjectResource || actual.Resource.Name != original.Name)
            {
                throw new InvalidDataException("The original execution shutdown resource observation is unavailable.");
            }
            return new
            {
                original.Name,
                original.ProcessId,
                original.StartedAtUtc,
                original.AspireStartedAtUtc,
                original.AspireProcessId,
                original.AspireProcessStartedAtUtc,
                actual.Snapshot.ExitCode,
                actual.Snapshot.StartTimeStamp,
                actual.Snapshot.StopTimeStamp,
                State = actual.Snapshot.State?.Text
            };
        }).ToArray();
        return ClusterFixtureApplicationStartup.WriteAsync(output, "execution-process-shutdown.json",
            new { JoinedAtUtc = TimeProvider.System.GetUtcNow(), Processes = originals, Resources = resources }, token);
    }
}
