using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.ResourceExecution;

internal static class AtomicTriadStagedFlow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var state = new AtomicTriadStagedFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                state.Initialize();
                await AtomicTriadStagedWarm.RunAsync(state, failures);
                var bytes = QueueWholeFlowStorage.Bytes(state.Store);
                var position = state.Store.Position;
                var identity = state.Store.Identity;
                ServerFailureObserver.Observe(state.Harness.JoinOwnedResources, failures);
                for (var cold = 0; cold < AtomicTriadStagedProtocol.ColdCycles; cold++)
                {
                    await ServerFailureObserver.ObserveAsync(() => AtomicTriadStagedCold.RunAsync(state, identity, bytes, position), failures);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
