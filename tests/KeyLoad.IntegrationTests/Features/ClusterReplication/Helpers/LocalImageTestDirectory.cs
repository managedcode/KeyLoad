using System.Runtime.ExceptionServices;
using KeyLoad.IntegrationTests.Features.ClusterReplication.Processes;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class LocalImageTestDirectory
{
    internal static async Task RunAsync(string prefix, Func<string, Task> action)
    {
        DirectoryInfo? directory = null;
        async Task InvokeAsync()
        {
            directory = Directory.CreateTempSubdirectory(prefix);
            await action(directory.FullName).ConfigureAwait(false);
        }
        var primary = await OwnedProcessFailureObserver.CaptureAsync(InvokeAsync()).ConfigureAwait(false);
        var cleanupFailures = new List<Exception>();
        if (directory is not null)
        {
            OwnedProcessFailureObserver.Observe(() => Directory.Delete(directory.FullName, recursive: true),
                cleanupFailures);
        }
        var cleanup = cleanupFailures.Count == 0 ? null : cleanupFailures[0];
        RethrowFailures(primary, cleanup);
    }

    private static void RethrowFailures(Exception? primary, Exception? cleanup)
    {
        if (primary is not null && cleanup is not null)
        {
            throw new AggregateException("The local RF3 image test and its temporary-directory cleanup failed.",
                primary, cleanup);
        }
        if (primary is not null)
        {
            ExceptionDispatchInfo.Capture(primary).Throw();
        }

        if (cleanup is not null)
        {
            ExceptionDispatchInfo.Capture(cleanup).Throw();
        }
    }
}
