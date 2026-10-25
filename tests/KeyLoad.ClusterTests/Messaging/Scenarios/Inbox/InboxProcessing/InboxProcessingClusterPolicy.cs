using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
namespace KeyLoad.ClusterTests.Messaging;

internal static class InboxProcessingClusterPolicy
{
    internal static async Task ExecuteAsync(ClusterFixture fixture, KeyLoadClient client, McpOfficialClient mcp,
        InboxProcessingClusterState state, CancellationToken token)
    {
        var principal = state.Identity.Principal;
        var demoted = principal with
        {
            PolicyEpoch = InboxProcessingClusterProtocol.DemotedEpoch,
            Grants =
            [new(state.Lane.Partition.DatabaseId, state.Lane.Queue, Capability.QueueInspect),
             new(state.Lane.Partition.DatabaseId, InboxProcessingClusterProtocol.Output, Capability.QueueInspect),
             new(state.Lane.Partition.DatabaseId, InboxProcessingClusterProtocol.Collection, Capability.DocumentsRead)]
        };
        await MessagingClusterIdentity.UpdateAsync(fixture, demoted, token);
        var current = state.Original with
        {
            CommandId = Guid.NewGuid()
        };
        await InboxProcessingClusterAssertions.DeniedAsync(await client.CommitProcessingAsync(current, token), ErrorCode.PermissionDenied);
        _ = (await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.MessagesProcess,
            current, token), ErrorCode.PermissionDenied, true)).Value;
        await InboxProcessingClusterAssertions.UnchangedAsync(client, state, token);
        await MessagingClusterIdentity.UpdateAsync(fixture, principal with
        {
            PolicyEpoch = InboxProcessingClusterProtocol.RestoredEpoch
        }, token);
        await InboxProcessingClusterReplay.ExecuteAsync(client, mcp, state, false, token);
    }
}
