using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryHostedParallelCancellation
{
    internal static Task RunAsync()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var observer = new QueryHostedReadObserver();
        return RequestCqrsHostedCluster.RunAsync(cluster => ExecuteAsync(cluster, observer, token), token, observer);
    }

    private static async Task ExecuteAsync(RequestCqrsHostedCluster cluster, QueryHostedReadObserver observer,
        CancellationToken token)
    {
        var data = new QueryHostedSearchDataset(cluster);
        await data.SeedAsync(token).ConfigureAwait(false);
        var images = QueryHostedSearchAssertions.Capture(cluster);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = observer.Hold(firstId, cluster.Nodes[0], data);
        var second = observer.Hold(secondId, cluster.Nodes[1], data);
        using var firstCaller = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var secondCaller = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var deadline = new CancellationTokenSource(cluster.Nodes[0].Timing.CompletionTimeout, TimeProvider.System);
        using var completion = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        var firstCall = QueryHostedSearchRead.ExecuteAsync(cluster.Nodes[0], data, firstId, firstCaller.Token);
        var secondCall = QueryHostedSearchRead.ExecuteAsync(cluster.Nodes[1], data, secondId, secondCaller.Token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Task.WhenAll(first.Entered.Task, second.Entered.Task).WaitAsync(completion.Token).ConfigureAwait(false);
            await Assert.That(first.Identity.RequestId).IsEqualTo(firstId);
            await Assert.That(second.Identity.RequestId).IsEqualTo(secondId);
            await Assert.That(first.Identity.RequestId).IsNotEqualTo(second.Identity.RequestId);
            await Assert.That(first.GrainId).IsNotEqualTo(second.GrainId);
            await QueryHostedReadAssertions.ProgressAsync(cluster, first).ConfigureAwait(false);
            await QueryHostedReadAssertions.ProgressAsync(cluster, second).ConfigureAwait(false);
            await Assert.That(firstCall.IsCompleted).IsFalse();
            await Assert.That(secondCall.IsCompleted).IsFalse();
            await firstCaller.CancelAsync().ConfigureAwait(false);
            var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => await firstCall)
                ?? throw new InvalidOperationException("The original execution caller cancellation is unavailable.");
            await Assert.That(error.CancellationToken == firstCaller.Token).IsTrue();
            await first.Disposed.Task.WaitAsync(completion.Token).ConfigureAwait(false);
            await Assert.That(secondCall.IsCompleted).IsFalse();
            await Assert.That(second.Disposed.Task.IsCompleted).IsFalse();
            await QueryHostedSearchAssertions.StateAsync(cluster, images).ConfigureAwait(false);
            second.Release.TrySetResult();
            var page = QueryHostedSearchRead.Page(await secondCall.ConfigureAwait(false));
            await second.Disposed.Task.WaitAsync(completion.Token).ConfigureAwait(false);
            await QueryHostedSearchAssertions.PageAsync(page, data, images[1]).ConfigureAwait(false);
            await QueryHostedSearchAssertions.HealthyAsync(cluster, data, images, token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        await firstCaller.CancelAsync().ConfigureAwait(false);
        await secondCaller.CancelAsync().ConfigureAwait(false);
        observer.ReleaseAll();
        using var cleanup = new CancellationTokenSource(cluster.Nodes[0].Timing.ShutdownTimeout, TimeProvider.System);
        await QueryHostedSearchSettlement.JoinAsync(firstCall, firstCaller, first, failures, cleanup.Token).ConfigureAwait(false);
        await QueryHostedSearchSettlement.JoinAsync(secondCall, secondCaller, second, failures, cleanup.Token).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
