using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventFeedReferencedOutcomeRf3Flow
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave, string credential,
        EventFeedControlRequest request, EventFeedControlResult original, CancellationToken token)
    {
        _ = await wave.StopForDirectoryReadAsync().ConfigureAwait(false);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            var root = Path.Combine(wave.OwnedDataRoot, node);
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, "node.owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, "database", "owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, "replica", "owner.lock"));
        }
        foreach (var node in TwoRf3MembershipProtocol.Nodes.Take(TwoRf3MembershipProtocol.MembersPerGroup))
        {
            await EventFeedReferencedOutcomeRf3Store.RunAsync(wave, node, request, original, token).ConfigureAwait(false);
        }
        await wave.RestartJoinedAsync(token).ConfigureAwait(false);
        await wave.WaitForSixHealthyAsync(token).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(original, await EventFeedOfferRf3Flow.RoutesAsync(
            wave, credential, request, token).ConfigureAwait(false));
        await EventFeedTailRf3Flow.RunAsync(wave, credential, request, original, token).ConfigureAwait(false);
    }
}
