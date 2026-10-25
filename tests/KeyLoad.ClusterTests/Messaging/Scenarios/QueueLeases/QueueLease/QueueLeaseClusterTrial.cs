using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueLeaseClusterTrial
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync().ConfigureAwait(false);
                using var deadline = McpCallerDeadline.Create();
                await ExecuteAsync(fixture, failures, deadline.Token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(ClusterFixture fixture, List<Exception> failures, CancellationToken token)
    {
        var seed = await QueueLeaseClusterSetup.CreateAsync(fixture, token).ConfigureAwait(false);
        var original = await QueueLeaseClusterRenew.ExecuteAsync(fixture, seed, failures, token).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        if (original is null)
        {
            throw new InvalidOperationException(QueueLeaseClusterProtocol.MissingSetup);
        }
        await QueueLeaseClusterCold.RestartAsync(fixture, token).ConfigureAwait(false);
        var acknowledged = await QueueLeaseClusterReclaim.ExecuteAsync(fixture, original, failures, token).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        if (acknowledged is null)
        {
            throw new InvalidOperationException(QueueLeaseClusterProtocol.MissingSetup);
        }
        await QueueLeaseClusterCold.RestartAsync(fixture, token).ConfigureAwait(false);
        await QueueLeaseClusterHealthy.ExecuteAsync(fixture, acknowledged, failures, token).ConfigureAwait(false);
    }
}
