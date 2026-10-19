using KeyLoad.Server.Features.OrleansHosting;

namespace KeyLoad.Server;

internal sealed partial class OrleansNode
{
    private async Task StopCoreAsync()
    {
        await Task.Yield();
        var failures = new List<Exception>();
        var startupFailures = new List<Exception>();
        journalAdmission.CloseScheduling();
        await requestLifetime.CancelAndJoinStartupAsync(null, startup, startupFailures, failures);
        await OrleansNodeHostShutdown.JoinAsync(RequestWork, null, failures);
        await requestLifetime.JoinAsync(failures);
        closure.Complete(failures);
        Volatile.Write(ref grains, null);
        failures.AddRange(startupFailures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
