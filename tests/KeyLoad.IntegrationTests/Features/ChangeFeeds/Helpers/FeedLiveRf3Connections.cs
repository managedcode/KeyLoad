using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

/// <summary>Joins every real caller while retaining initiating and disposal failures in original order.</summary>
internal sealed class FeedLiveRf3Connections(ClusterFixture fixture)
{
    private const int NoCallers = 0;
    private readonly List<RequestCqrsRf3Callers> callers = [];
    internal ClusterFixture Fixture => fixture;

    internal async Task<RequestCqrsRf3Callers> ConnectAsync(string credential, CancellationToken token)
    {
        var caller = await RequestCqrsRf3Callers.ConnectAsync(fixture.App,
            McpCallerProtocol.Node1, credential, token);
        callers.Add(caller);
        return caller;
    }

    internal async Task CloseAsync(RequestCqrsRf3Callers caller)
    {
        if (!callers.Remove(caller))
        { throw new InvalidOperationException(nameof(FeedLiveRf3Connections)); }
        await caller.DisposeAsync();
    }

    internal static async Task RunAsync(ClusterFixture fixture, Func<FeedLiveRf3Connections, Task> operation)
    {
        var owner = new FeedLiveRf3Connections(fixture);
        var failures = new List<Exception>();
        try
        { await ServerFailureObserver.ObserveAsync(() => operation(owner), failures); }
        finally
        {
            for (var index = owner.callers.Count; index > NoCallers;)
            {
                var caller = owner.callers[--index];
                await ServerFailureObserver.ObserveAsync(() => caller.DisposeAsync().AsTask(), failures);
            }
            owner.callers.Clear();
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
