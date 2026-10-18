using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.IntegrationTests.Features.CodeQuality;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal sealed class ClusterFixtureCleanupCompletion(Func<NativeCoverageClusterFixtureOwner?> readCoverage,
    Action<Exception> rethrow)
{
    internal async Task CompleteAsync(bool appStopped,
        IReadOnlyDictionary<string, ContainerRuntimeInspection>? stoppedNodes,
        NativeCoverageCleanupDeadline? deadline, List<Exception> failures, CancellationToken token)
    {
        if (readCoverage() is not null && appStopped && failures.Count == 0 && stoppedNodes is not null)
        {
            await deadline!.CollectAsync(() => readCoverage()!.PublishReceiptAsync(stoppedNodes, token), failures)
                .ConfigureAwait(false);
        }
        if (failures.Count == 1 && readCoverage() is not null)
        {
            rethrow(failures[0]);
        }
        if (failures.Count > 0)
        {
            throw new AggregateException("Cluster fixture cleanup failed.", failures);
        }
    }
}
