using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueLeaseRf3Trial
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
        var seed = await QueueLeaseRf3Setup.CreateAsync(fixture, token).ConfigureAwait(false);
        var original = await QueueLeaseRf3Renew.ExecuteAsync(fixture, seed, failures, token).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        if (original is null)
        { throw new InvalidOperationException(QueueLeaseRf3Protocol.MissingSetup); }
        await QueueLeaseRf3Cold.RestartAsync(fixture, token).ConfigureAwait(false);
        var acknowledged = await QueueLeaseRf3Reclaim.ExecuteAsync(fixture, original, failures, token).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        if (acknowledged is null)
        { throw new InvalidOperationException(QueueLeaseRf3Protocol.MissingSetup); }
        await QueueLeaseRf3Cold.RestartAsync(fixture, token).ConfigureAwait(false);
        await QueueLeaseRf3Healthy.ExecuteAsync(fixture, acknowledged, failures, token).ConfigureAwait(false);
    }
}
