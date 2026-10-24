using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
namespace KeyLoad.IntegrationTests.Messaging;

internal static class TargetInboxClusterPolicy
{
    internal static async Task ExecuteAsync(ClusterFixture fixture, KeyLoadClient client, McpOfficialClient mcp,
        TargetInboxClusterState state, CancellationToken token)
    {
        var principal = state.Identity.Principal;
        var demoted = principal with
        {
            PolicyEpoch = TargetInboxClusterProtocol.DemotedEpoch,
            Grants =
            [new(state.Target.Partition.DatabaseId, state.Target.Queue, Capability.InboxWrite),
             new(state.Source.Partition.DatabaseId, state.Source.Queue, Capability.QueueInspect),
             new(state.Target.Partition.DatabaseId, TargetInboxClusterProtocol.Output, Capability.QueueInspect),
             new(state.Target.Partition.DatabaseId, TargetInboxClusterProtocol.Collection, Capability.DocumentsRead)]
        };
        await MessagingClusterIdentity.UpdateAsync(fixture, demoted, token);
        var current = state.Original with
        {
            CommandId = Guid.NewGuid()
        };
        await TargetInboxClusterAssertions.DeniedAsync(await client.CommitInboxAsync(current, token), ErrorCode.PermissionDenied);
        _ = await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(TargetInboxClusterProtocol.Tool, current, token), ErrorCode.PermissionDenied, true);
        await TargetInboxClusterAssertions.UnchangedAsync(client, state, token);
        await MessagingClusterIdentity.UpdateAsync(fixture, principal with
        {
            PolicyEpoch = TargetInboxClusterProtocol.RestoredEpoch
        }, token);
        await TargetInboxClusterReplay.ExecuteAsync(client, mcp, state, false, token);
    }
}
