using Aspire.Hosting.ApplicationModel;
using KeyLoad.Server;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class InboxProcessingRf3Cold
{
    internal static async Task RestartAsync(ClusterFixture fixture, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var node in InboxProcessingRf3Protocol.Nodes)
        { await ServerFailureObserver.ObserveAsync(() => fixture.KillContainerAsync(node, InboxProcessingRf3Protocol.Cold, token), failures).ConfigureAwait(false); }
        foreach (var node in InboxProcessingRf3Protocol.Nodes)
        { await ServerFailureObserver.ObserveAsync(() => fixture.RestartContainerAsync(node, token), failures).ConfigureAwait(false); }
        foreach (var node in InboxProcessingRf3Protocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () => await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(
            node, WaitBehavior.WaitOnResourceUnavailable, token), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
