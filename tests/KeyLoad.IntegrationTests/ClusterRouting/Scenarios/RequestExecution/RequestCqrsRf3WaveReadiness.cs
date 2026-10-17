using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsRf3WaveReadiness
{
    internal static async Task WaitForNodesAsync(DistributedApplication app, bool requireHealthy,
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
                await WaitForNodeAsync(app, node, requireHealthy, lifecycle, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                lifecycle?.RecordReadiness(node, ReadinessObserved(app, node)
                    ? RequestCqrsNodeReadinessOutcome.NotReady : RequestCqrsNodeReadinessOutcome.NotObserved);
                throw;
            }
        }
    }

    private static async Task WaitForNodeAsync(DistributedApplication app, string node, bool requireHealthy,
        RequestCqrsLifecycleEvidence? lifecycle, CancellationToken cancellationToken)
    {
        if (requireHealthy)
        {
            await app.ResourceNotifications.WaitForResourceHealthyAsync(node, cancellationToken).ConfigureAwait(false);
            lifecycle?.RecordReadiness(node, RequestCqrsNodeReadinessOutcome.Ready);
            return;
        }
        await app.ResourceNotifications.WaitForResourceAsync(node,
                    resource => resource.Snapshot.State?.Text == KnownResourceStates.Running
                        || resource.Snapshot.State?.Text == KnownResourceStates.FailedToStart,
                    cancellationToken).ConfigureAwait(false);
        if (!app.ResourceNotifications.TryGetCurrentState(node, out var state)
            || state?.Snapshot.State?.Text != KnownResourceStates.Running)
        {
            lifecycle?.RecordReadiness(node, ReadinessObserved(app, node)
                ? RequestCqrsNodeReadinessOutcome.NotReady : RequestCqrsNodeReadinessOutcome.NotObserved);
            throw new InvalidOperationException("An expected Aspire node did not reach Running.");
        }
        lifecycle?.RecordReadiness(node, RequestCqrsNodeReadinessOutcome.Ready);
    }

    private static bool ReadinessObserved(DistributedApplication app, string node)
        => app.ResourceNotifications.TryGetCurrentState(node, out _);

}
