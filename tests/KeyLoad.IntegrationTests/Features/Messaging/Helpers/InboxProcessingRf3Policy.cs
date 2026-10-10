using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class InboxProcessingRf3Policy
{
    internal static async Task ExecuteAsync(ClusterFixture fixture, KeyLoadClient client, McpOfficialClient mcp,
        InboxProcessingRf3State state, CancellationToken token)
    {
        var principal = state.Identity.Principal;
        var demoted = principal with
        {
            PolicyEpoch = InboxProcessingRf3Protocol.DemotedEpoch,
            Grants =
            [new(state.Lane.Partition.DatabaseId, state.Lane.Queue, Capability.QueueInspect),
             new(state.Lane.Partition.DatabaseId, InboxProcessingRf3Protocol.Output, Capability.QueueInspect),
             new(state.Lane.Partition.DatabaseId, InboxProcessingRf3Protocol.Collection, Capability.DocumentsRead)]
        };
        await MessagingRf3Identity.UpdateAsync(fixture, demoted, token);
        var current = state.Original with { CommandId = Guid.NewGuid() };
        await InboxProcessingRf3Assertions.DeniedAsync(await client.CommitProcessingAsync(current, token), ErrorCode.PermissionDenied);
        _ = await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.MessagesProcess,
            current, token), ErrorCode.PermissionDenied, true);
        await InboxProcessingRf3Assertions.UnchangedAsync(client, state, token);
        await MessagingRf3Identity.UpdateAsync(fixture, principal with { PolicyEpoch = InboxProcessingRf3Protocol.RestoredEpoch }, token);
        await InboxProcessingRf3Replay.ExecuteAsync(client, mcp, state, false, token);
    }
}
