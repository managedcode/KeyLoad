using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

/// <summary>Joins every real caller while retaining initiating and disposal failures in original order.</summary>
internal sealed class FeedLiveClusterConnections(ClusterFixture fixture)
{
    private readonly List<RequestCqrsClusterCallers> callers = [];
    internal ClusterFixture Fixture => fixture;

    internal async Task<RequestCqrsClusterCallers> ConnectAsync(string credential, CancellationToken token)
    {
        var caller = await RequestCqrsClusterCallers.ConnectAsync(fixture.App,
            McpCallerProtocol.Node1, credential, token);
        callers.Add(caller);
        return caller;
    }

    internal async Task CloseAsync(RequestCqrsClusterCallers caller)
    {
        if (!callers.Remove(caller))
        {
            throw new InvalidOperationException(nameof(FeedLiveClusterConnections));
        }
        await caller.DisposeAsync();
    }

    internal static async Task RunAsync(ClusterFixture fixture, Func<FeedLiveClusterConnections, Task> operation)
    {
        var owner = new FeedLiveClusterConnections(fixture);
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(() => operation(owner), failures);
        }
        finally
        {
            for (var index = owner.callers.Count; index > 0;)
            {
                var caller = owner.callers[--index];
                await ServerFailureObserver.ObserveAsync(() => caller.DisposeAsync().AsTask(), failures);
            }
            owner.callers.Clear();
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
