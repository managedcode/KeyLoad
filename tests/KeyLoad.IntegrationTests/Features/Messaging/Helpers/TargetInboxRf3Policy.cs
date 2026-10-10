using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class TargetInboxRf3Policy
{
    internal static async Task ExecuteAsync(ClusterFixture fixture, KeyLoadClient client, McpOfficialClient mcp,
        TargetInboxRf3State state, CancellationToken token)
    {
        var principal = state.Identity.Principal;
        var demoted = principal with
        {
            PolicyEpoch = TargetInboxRf3Protocol.DemotedEpoch,
            Grants =
            [new(state.Target.Partition.DatabaseId, state.Target.Queue, Capability.InboxWrite),
             new(state.Source.Partition.DatabaseId, state.Source.Queue, Capability.QueueInspect),
             new(state.Target.Partition.DatabaseId, TargetInboxRf3Protocol.Output, Capability.QueueInspect),
             new(state.Target.Partition.DatabaseId, TargetInboxRf3Protocol.Collection, Capability.DocumentsRead)]
        };
        await MessagingRf3Identity.UpdateAsync(fixture, demoted, token);
        var current = state.Original with { CommandId = Guid.NewGuid() };
        await TargetInboxRf3Assertions.DeniedAsync(await client.CommitInboxAsync(current, token), ErrorCode.PermissionDenied);
        _ = await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(TargetInboxRf3Protocol.Tool, current, token), ErrorCode.PermissionDenied, true);
        await TargetInboxRf3Assertions.UnchangedAsync(client, state, token);
        await MessagingRf3Identity.UpdateAsync(fixture, principal with { PolicyEpoch = TargetInboxRf3Protocol.RestoredEpoch }, token);
        await TargetInboxRf3Replay.ExecuteAsync(client, mcp, state, false, token);
    }
}
