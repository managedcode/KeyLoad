using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class AtomicMembershipScopeForeignOwners
{
    private const string Scenario = "logical-scope-membership-cardinality";
    internal static async Task DuringAsync(TwoRf3MembershipWave wave,
        Func<Task> action, CancellationToken token)
    {
        var stopped = new List<string>();
        var failures = new List<Exception>();
        foreach (var node in TwoRf3MembershipProtocol.Nodes[3..])
        {
            var before = failures.Count;
            await ServerFailureObserver.ObserveAsync(() => wave.RemoteRuntime.KillAsync(node, Scenario, token), failures);
            if (failures.Count != before)
            {
                break;
            }
            stopped.Add(node);
        }
        if (stopped.Count == 3)
        {
            await ServerFailureObserver.ObserveAsync(action, failures);
        }
        foreach (var node in stopped)
        {
            await ServerFailureObserver.ObserveAsync(() => wave.RemoteRuntime.BeginRestartAsync(node, token), failures);
        }
        foreach (var node in stopped)
        {
            await ServerFailureObserver.ObserveAsync(() => wave.RemoteRuntime.RestartAsync(node, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await wave.WaitForSixHealthyAsync(token);
    }
}
