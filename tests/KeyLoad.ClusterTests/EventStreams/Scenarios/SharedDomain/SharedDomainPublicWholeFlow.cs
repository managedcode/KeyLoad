using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.EventStreams;

internal static class SharedDomainPublicWholeFlow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                using var deadline = McpCallerDeadline.Create();
                await ExecuteAsync(fixture, deadline.Token);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await SharedDomainPublicSeed.CreateAsync(fixture, token);
        await SharedDomainPublicProducer.WriteAsync(fixture, state, false, token);
        await SharedDomainPublicModels.AllAsync(fixture, state, token);
        await SharedDomainPublicRefusals.ProducerAsync(fixture, state, token);
        await SharedDomainPublicRefusals.BindingAsync(fixture, state, token);
        await SharedDomainPublicCold.RestartAsync(fixture, token);
        await SharedDomainPublicProducer.ReplayAllAsync(fixture, state, token);
        await SharedDomainPublicModels.AllAsync(fixture, state, token);
        await SharedDomainPublicProcessing.RunAsync(fixture, state, token);
        await SharedDomainPublicAuthority.RunAsync(fixture, state, token);
        await SharedDomainPublicCold.RestartAsync(fixture, token);
        await SharedDomainPublicProducer.ReplayAllAsync(fixture, state, token);
        await SharedDomainPublicSession.RunAsync(fixture, state.Identity.Secret,
            (sdk, official) => SharedDomainPublicProcessing.ReplayAsync(sdk, official, state, token), token);
        await SharedDomainPublicModels.AllAsync(fixture, state, token);
    }
}
