using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.ResourceExecution;

internal static class CachePolicyEpochFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var owner = new CachePolicyEpochOwner();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                owner.Open(first: true);
                var state = new CachePolicyEpochState(owner);
                await CachePolicyEpochWarm.RunAsync(state, token);
                await CachePolicyEpochCold.RunAsync(state, token);
            }, failures);
            if (failures.Count != 0)
            {
                owner.Retain();
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
