using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryHostedSearchDeadline
{
    private const int PastDeadlineSeconds = 1;
    private const int ReceivingNode = 1;
    private const string DeadlineDetail = "The read execution deadline is exceeded.";

    internal static Task RunAsync()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var observer = new QueryHostedReadObserver();
        var receivingClock = new QueryObservedWorkClock();
        return RequestCqrsHostedCluster.RunAsync(cluster => ExecuteAsync(cluster, observer, receivingClock, token),
            token, observer, index => index == ReceivingNode ? receivingClock : TimeProvider.System);
    }

    private static async Task ExecuteAsync(RequestCqrsHostedCluster cluster, QueryHostedReadObserver observer,
        QueryObservedWorkClock clock, CancellationToken token)
    {
        var data = new QueryHostedSearchDataset(cluster);
        await data.SeedAsync(token).ConfigureAwait(false);
        var images = QueryHostedSearchAssertions.Capture(cluster);
        var node = cluster.Nodes[ReceivingNode];
        var requestId = Guid.NewGuid();
        var read = observer.Hold(requestId, node);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var deadline = new CancellationTokenSource(node.Timing.CompletionTimeout, TimeProvider.System);
        using var completion = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        var operation = QueryHostedSearchRead.ExecuteAsync(node, data, requestId, caller.Token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await read.Entered.Task.WaitAsync(completion.Token).ConfigureAwait(false);
            await Assert.That(read.NodeId).IsEqualTo(node.Database.Store.Identity.NodeId);
            await Assert.That(read.ObservedBytes).IsGreaterThan(read.BeforeBytes);
            clock.Arm(() => node.Database.Store.GetReadDiagnostics().RangeExaminedBytes > read.BeforeBytes,
                () => clock.Advance(TimeSpan.FromSeconds(node.Database.Database.Limits.QueryDeadlineSeconds + PastDeadlineSeconds)));
            read.Release.TrySetResult();
            var reply = await operation.WaitAsync(completion.Token).ConfigureAwait(false);
            await read.Disposed.Task.WaitAsync(completion.Token).ConfigureAwait(false);
            clock.Disarm();
            await Assert.That(clock.Triggered).IsTrue();
            await Assert.That(reply.Error).IsEqualTo(ErrorCode.BudgetExceeded);
            await Assert.That(reply.SafeDetail).IsEqualTo(DeadlineDetail);
            await Assert.That(reply.Payload.IsEmpty).IsTrue();
            await QueryHostedSearchAssertions.StateAsync(cluster, images).ConfigureAwait(false);
            await QueryHostedSearchAssertions.HealthyAsync(cluster, data, images, token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        clock.Disarm();
        await caller.CancelAsync().ConfigureAwait(false);
        observer.ReleaseAll();
        using var cleanup = new CancellationTokenSource(node.Timing.ShutdownTimeout, TimeProvider.System);
        await QueryHostedSearchSettlement.JoinAsync(operation, caller, read, failures, cleanup.Token).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
