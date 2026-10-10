using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionFilterRf3Policy
{
    private const long DeniedEpoch = 2;
    private const long RestoredEpoch = 3;

    internal static async Task RunAsync(ClusterFixture fixture, SubscriptionFilterRf3Original original,
        KeyLoadClient manager, McpOfficialClient mcp, CancellationToken token)
    {
        var seed = original.Seed;
        var denied = seed.Manager.Principal with
        {
            PolicyEpoch = DeniedEpoch,
            Grants = [new(seed.Group.Source.Partition.DatabaseId, SubscriptionFilterRf3Protocol.Topic,
                Capability.TopicsRead | Capability.SubscriptionsConsume)]
        };
        await MessagingRf3Identity.UpdateAsync(fixture, denied, token);
        await SubscriptionFilterRf3Assertions.RefusedAsync(manager, mcp,
            seed.Replacement with { CommandId = Guid.NewGuid() }, ErrorCode.PermissionDenied, token);
        await MessagingRf3Identity.UpdateAsync(fixture, seed.Manager.Principal with { PolicyEpoch = RestoredEpoch }, token);
        var historical = await manager.CommitAsync(seed.Publish, token);
        await Assert.That(historical.Problem!.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit,
            seed.Publish, token), ErrorCode.PermissionDenied, true);
        await SubscriptionFilterRf3Assertions.HistoryAsync(manager, mcp, seed.Group.Source, original.History, token);
    }
}
