using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class NativePolicyEpochFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var owner = new NativePolicyEpochOwner();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                owner.Open(first: true);
                var state = new NativePolicyEpochState(owner);
                await NativePolicyEpochWarm.RunAsync(state, token);
                await NativePolicyEpochCold.RunAsync(state, token);
            }, failures);
            if (failures.Count != 0)
            {
                owner.Retain();
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
