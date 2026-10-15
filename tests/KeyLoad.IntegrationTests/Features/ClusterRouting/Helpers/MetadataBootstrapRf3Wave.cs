using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class MetadataBootstrapRf3Wave
{
    internal static async Task RunAsync(MetadataBootstrapRf3State state, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var node in MetadataBootstrapRf3State.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                state.ActiveNode = node;
                var status = await MetadataBootstrapRf3Calls.SdkAsync(state, state.Fixture.AdminKey, sdk => sdk.StatusAsync(token));
                await MetadataBootstrapRf3Authority.ValidateAsync(state, status);
                if (state.Statuses.TryGetValue(node, out var prior))
                {
                    await Assert.That(status.NodeId).IsEqualTo(prior.NodeId);
                    await Assert.That(status.ReadGeneration).IsGreaterThanOrEqualTo(prior.ReadGeneration);
                    await Assert.That(status.Applied).IsGreaterThanOrEqualTo(prior.Applied);
                    await Assert.That(status.ConsensusTerm).IsGreaterThanOrEqualTo(prior.ConsensusTerm);
                }
                state.Statuses[node] = status;
                await MetadataBootstrapRf3Metadata.AllAsync(state, token);
                await MetadataBootstrapRf3Metadata.RequirePartitionAsync(state, state.Third, 3L, true, state.Identity.Secret, token);
                foreach (var bind in state.Binds)
                {
                    await MetadataBootstrapRf3Calls.BindAsync(state, bind.Credential, bind.Id, bind.Request, token);
                }
                await MetadataBootstrapRf3Data.RequireAsync(state, token);
            }, failures);
        }
        await ServerFailureObserver.ObserveAsync(() => state.Cold.RequireAsync(token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
