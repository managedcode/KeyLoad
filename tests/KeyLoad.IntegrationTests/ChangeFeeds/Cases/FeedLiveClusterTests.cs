using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

/// <summary>REQ/AC-FEED-002/003/005: genuine Orleans persisted privacy, cursor and live-query continuation.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class FeedLiveClusterTests(ClusterFixture fixture)
{
    [Test]
    public async Task OriginalCursorReconnectsAcrossColdClusterAndRechecksRevokedPersistedAccess()
    {
        using var deadline = McpCallerDeadline.Create();
        await FeedReconnectClusterTrial.RunAsync(fixture, deadline.Token);
    }

    [Test]
    public async Task ConcurrentSnapshotAndCommittedTailRemainGapFreePrivateAndRevocable()
    {
        using var deadline = McpCallerDeadline.Create();
        await LiveTailClusterTrial.RunAsync(fixture, deadline.Token);
    }
}
