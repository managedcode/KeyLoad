using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server;

internal sealed partial class OrleansNode
{
    private async Task StopCoreAsync()
    {
        await Task.Yield();
        var failures = new List<Exception>();
        var startupFailures = new List<Exception>();
        journalAdmission.CloseScheduling();
        await requestLifetime.CancelAndJoinStartupAsync(null, startup, startupFailures, failures).ConfigureAwait(false);
        await OrleansNodeHostShutdown.JoinAsync(RequestWork, null, failures).ConfigureAwait(false);
        await requestLifetime.JoinAsync(failures).ConfigureAwait(false);
        closure.Complete(failures);
        Volatile.Write(ref grains, null);
        failures.AddRange(startupFailures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
