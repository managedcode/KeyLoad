using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsRf3WaveReadiness
{
    internal static async Task WaitForNodesAsync(DistributedApplication app, bool requireHealthy, bool captureDiscovery,
        CancellationToken cancellationToken, RequestCqrsLifecycleEvidence? lifecycle = null)
    {
        lifecycle?.SetStage(RequestCqrsLifecycleStage.NodeReadiness);
        foreach (var node in new[]
        {
            RequestCqrsRf3Protocol.Node1,
            RequestCqrsRf3Protocol.Node2,
            RequestCqrsRf3Protocol.Node3
        })
        {
            try
            {
                if (requireHealthy)
                {
                    await app.ResourceNotifications.WaitForResourceHealthyAsync(node, cancellationToken)
                        .ConfigureAwait(false);
                    lifecycle?.RecordReadiness(node, RequestCqrsNodeReadinessOutcome.Ready);
                    continue;
                }
                await app.ResourceNotifications.WaitForResourceAsync(node,
                    resource => resource.Snapshot.State?.Text == KnownResourceStates.Running
                        || resource.Snapshot.State?.Text == KnownResourceStates.FailedToStart
                        || captureDiscovery && node == RequestCqrsRf3Protocol.Node1
                            && IsTerminal(resource.Snapshot.State?.Text),
                    cancellationToken).ConfigureAwait(false);
                if (!app.ResourceNotifications.TryGetCurrentState(node, out var state)
                    || state?.Snapshot.State?.Text != KnownResourceStates.Running
                        && !(captureDiscovery && node == RequestCqrsRf3Protocol.Node1
                            && IsTerminal(state?.Snapshot.State?.Text)))
                {
                    lifecycle?.RecordReadiness(node, ReadinessObserved(app, node)
                        ? RequestCqrsNodeReadinessOutcome.NotReady : RequestCqrsNodeReadinessOutcome.NotObserved);
                    throw new InvalidOperationException("An expected mixed-protocol Aspire node did not reach Running.");
                }
                lifecycle?.RecordReadiness(node, RequestCqrsNodeReadinessOutcome.Ready);
            }
            catch
            {
                lifecycle?.RecordReadiness(node, ReadinessObserved(app, node)
                    ? RequestCqrsNodeReadinessOutcome.NotReady : RequestCqrsNodeReadinessOutcome.NotObserved);
                throw;
            }
        }
    }

    private static bool ReadinessObserved(DistributedApplication app, string node)
        => app.ResourceNotifications.TryGetCurrentState(node, out _);

    private static bool IsTerminal(string? state) => state == KnownResourceStates.Exited
        || state == KnownResourceStates.FailedToStart || state == KnownResourceStates.Finished;
}
