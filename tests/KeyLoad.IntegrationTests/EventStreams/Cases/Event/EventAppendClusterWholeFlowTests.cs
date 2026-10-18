using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

/// <summary>Original public OCC/dedup/fresh authorization acceptance through actual Aspire Orleans.</summary>
/// <param name="fixture">The existing real Orleans resources and discovered public endpoints.</param>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class EventAppendClusterWholeFlowTests(ClusterFixture fixture)
{
    [Test]
    public async Task AcEvent004005PublicConcurrentExactDedupDeniedReplayAndHealthyAnyAppend()
    {
        using var deadline = McpCallerDeadline.Create();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
            var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
            McpOfficialClient? official = null;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                official = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
                    fixture.AdminKey, deadline.Token);
                await EventAppendClusterFlow.RunAsync(sdk, official, fixture, deadline.Token);
            }, failures).ConfigureAwait(false);
            if (official is { } owned)
            {
                await ServerFailureObserver.ObserveAsync(() => owned.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
            }
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
