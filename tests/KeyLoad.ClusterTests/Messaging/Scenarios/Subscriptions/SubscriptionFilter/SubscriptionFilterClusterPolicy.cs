using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Messaging;

internal static class SubscriptionFilterClusterPolicy
{
    private const long DeniedEpoch = 2;
    private const long RestoredEpoch = 3;

    internal static async Task RunAsync(ClusterFixture fixture, SubscriptionFilterClusterOriginal original,
        KeyLoadClient manager, McpOfficialClient mcp, CancellationToken token)
    {
        var seed = original.Seed;
        var denied = seed.Manager.Principal with
        {
            PolicyEpoch = DeniedEpoch,
            Grants = [new(seed.Group.Source.Partition.DatabaseId, SubscriptionFilterClusterProtocol.Topic,
                Capability.TopicsRead | Capability.SubscriptionsConsume)]
        };
        await MessagingClusterIdentity.UpdateAsync(fixture, denied, token);
        await SubscriptionFilterClusterAssertions.RefusedAsync(manager, mcp,
            seed.Replacement with
            {
                CommandId = Guid.NewGuid()
            }, ErrorCode.PermissionDenied, token);
        await MessagingClusterIdentity.UpdateAsync(fixture, seed.Manager.Principal with
        {
            PolicyEpoch = RestoredEpoch
        }, token);
        var historical = await manager.CommitAsync(seed.Publish, token);
        await Assert.That(historical.Problem!.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        _ = (await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit,
            seed.Publish, token), ErrorCode.PermissionDenied, true)).Value;
        await SubscriptionFilterClusterAssertions.HistoryAsync(manager, mcp, seed.Group.Source, original.History, token);
    }
}
