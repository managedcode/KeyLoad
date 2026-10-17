using Aspire.Hosting;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.IntegrationTests.Features.CodeQuality;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class ClusterFixtureApplicationShutdown
{
    internal static async Task<(bool Stopped, IReadOnlyDictionary<string, ContainerRuntimeInspection>? Nodes)> CollectAsync(
        DistributedApplication? owned, NativeCoverageCleanupDeadline? deadline,
        NativeCoverageRf3FixtureOwner? coverage, bool hasRuntime, List<Exception> failures, CancellationToken token)
    {
        var failurePosition = failures.Count;
        var pending = RunAsync(owned, deadline, coverage, hasRuntime, failures, token);
        await Task.WhenAny(pending).ConfigureAwait(false);
        if (pending.IsCompletedSuccessfully)
        {
            return await pending.ConfigureAwait(false);
        }
        if (pending.Exception is { } aggregate)
        {
            failures.InsertRange(failurePosition, aggregate.InnerExceptions);
        }
        else if (pending.IsCanceled)
        {
            try
            {
                await pending.ConfigureAwait(false);
            }
            catch (OperationCanceledException failure)
            {
                failures.Insert(failurePosition, failure);
            }
        }
        return (false, null);
    }

    internal static async Task<(bool Stopped, IReadOnlyDictionary<string, ContainerRuntimeInspection>? Nodes)> RunAsync(
        DistributedApplication? owned, NativeCoverageCleanupDeadline? deadline,
        NativeCoverageRf3FixtureOwner? coverage, bool hasRuntime, List<Exception> failures, CancellationToken token)
    {
        if (owned is null)
        {
            return (false, null);
        }
        try
        {
            if (deadline is null)
            {
                await owned.StopAsync(token).ConfigureAwait(false);
            }
            else
            {
                await deadline.WaitAsync(() => owned.StopAsync(token)).ConfigureAwait(false);
            }
            IReadOnlyDictionary<string, ContainerRuntimeInspection>? nodes = null;
            if (coverage is not null && hasRuntime)
            {
                nodes = await coverage.VerifyStoppedAsync(deadline!, token).ConfigureAwait(false);
            }
            return (true, nodes);
        }
        finally
        {
            if (deadline is null)
            {
                await ClusterFixtureCleanup.CollectFailureAsync(() => owned.DisposeAsync().AsTask(), failures)
                    .ConfigureAwait(false);
            }
            else
            {
                await deadline.CollectAsync(() => owned.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
            }
        }
    }
}
