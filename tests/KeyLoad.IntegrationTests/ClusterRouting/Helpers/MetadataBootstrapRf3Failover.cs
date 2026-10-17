using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class MetadataBootstrapRf3Failover
{
    internal static async Task RunAsync(MetadataBootstrapRf3State state, CancellationToken token)
    {
        state.BeforeLoss = await MetadataBootstrapRf3Calls.SdkAsync(state, state.Fixture.AdminKey, sdk => sdk.StatusAsync(token));
        var lost = new Uri(state.BeforeLoss.Leader!).Host;
        state.ActiveNode = lost;
        state.BeforeLoss = await MetadataBootstrapRf3Calls.SdkAsync(state, state.Fixture.AdminKey, sdk => sdk.StatusAsync(token));
        await MetadataBootstrapRf3Authority.ValidateAsync(state, state.BeforeLoss);
        await Assert.That(new Uri(state.BeforeLoss.Leader!).Host).IsEqualTo(lost);
        state.Statuses[lost] = state.BeforeLoss;
        await Assert.That(MetadataBootstrapRf3State.Nodes.Contains(lost, StringComparer.Ordinal)).IsTrue();
        state.LostNode = lost;
        state.FaultScenario = state.Cold.Claim([lost]);
        state.Stopped.Add(lost);
        await state.Fixture.KillContainerAsync(lost, state.FaultScenario, token);
        state.ActiveNode = await MetadataBootstrapRf3Authority.ObserveAsync(state, lost, token);
        await MetadataBootstrapRf3Metadata.AllAsync(state, token);
        await MetadataBootstrapRf3Stale.RunAsync(state, token);
        await MetadataBootstrapRf3Metadata.BindNextAsync(state, state.Second, state.Fixture.AdminKey, token);
        await MetadataBootstrapRf3Policy.RepairAsync(state, token);
    }
    internal static async Task RestoreAsync(MetadataBootstrapRf3State state, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var node in state.Stopped.ToArray())
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await state.Fixture.RestartContainerAsync(node, token);
                state.Stopped.Remove(node);
            }, failures);
        }
        if (state.FaultScenario is not null && state.LostNode is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => state.Cold.CaptureAsync(state.FaultScenario, [state.LostNode], token), failures);
        }
        await ServerFailureObserver.ObserveAsync(() => state.Cold.RequireAsync(token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
