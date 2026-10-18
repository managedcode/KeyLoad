using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionFilterClusterTrial
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
                var seed = await SubscriptionFilterClusterSetup.CreateAsync(fixture, deadline.Token);
                var original = await SubscriptionFilterClusterOriginalFlow.RunAsync(fixture, seed, deadline.Token);
                var independent = await SubscriptionIndependentCheckpointR52Flow.StartAsync(fixture, seed, deadline.Token);
                await SubscriptionGroupSiloRestartFlow.RunAsync(fixture, independent, deadline.Token);
                await SubscriptionFilterClusterCold.RestartAsync(fixture, deadline.Token);
                var updated = await SubscriptionFilterClusterUpdate.RunAsync(fixture, original, deadline.Token);
                await SubscriptionIndependentCheckpointR52Flow.FirstColdAsync(fixture, independent, deadline.Token);
                await SubscriptionFilterClusterCold.RestartAsync(fixture, deadline.Token);
                await SubscriptionFilterClusterHealthy.RunAsync(fixture, updated, deadline.Token);
                await SubscriptionIndependentCheckpointR52Flow.SecondColdAsync(fixture, independent, deadline.Token);
            }, failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
