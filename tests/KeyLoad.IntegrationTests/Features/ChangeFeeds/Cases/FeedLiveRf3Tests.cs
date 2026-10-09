using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

/// <summary>REQ/AC-FEED-002/003/005: genuine RF3 persisted privacy, cursor and live-query continuation.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class FeedLiveRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task OriginalCursorReconnectsAcrossColdRf3AndRechecksRevokedPersistedAccess()
    {
        using var deadline = McpCallerDeadline.Create();
        await FeedReconnectRf3Trial.RunAsync(fixture, deadline.Token);
    }

    [Test]
    public async Task ConcurrentSnapshotAndCommittedTailRemainGapFreePrivateAndRevocable()
    {
        using var deadline = McpCallerDeadline.Create();
        await LiveTailRf3Trial.RunAsync(fixture, deadline.Token);
    }
}
