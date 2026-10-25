using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueProducerClusterPolicy
{
    internal static async Task RestoreAsync(ClusterFixture fixture, QueueProducerClusterOriginal original,
        List<Exception> failures, CancellationToken token)
    {
        var seed = original.Seed;
        var demoted = seed.Identity.Principal with
        {
            PolicyEpoch = checked(seed.Identity.Principal.PolicyEpoch + QueueProducerClusterProtocol.PolicyEpochAdvance),
            Grants = [new(seed.Lane.Partition.DatabaseId, seed.Lane.Queue, Capability.QueueInspect),
                new(seed.Lane.Partition.DatabaseId, QueueProducerClusterProtocol.Collection, Capability.DocumentsRead | Capability.Query),
                new(seed.Lane.Partition.DatabaseId, QueueProducerClusterProtocol.StreamSet, Capability.EventsRead | Capability.Query),
                new(seed.Lane.Partition.DatabaseId, QueueProducerClusterProtocol.ForeignStreamSet, Capability.EventsRead | Capability.Query)]
        };
        await MessagingClusterIdentity.UpdateAsync(fixture, demoted, token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var publisher = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Identity.Secret, token);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var refused = QueueProducerClusterSetup.Fresh(seed, QueueProducerClusterProtocol.Refused, QueueProducerClusterProtocol.Refused);
            await QueueProducerClusterAssertions.DeniedAsync(await publisher.CommitAsync(refused, token), ErrorCode.PermissionDenied);
            _ = (await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, refused, token), ErrorCode.PermissionDenied, true)).Value;
            await AtomicProducerClusterRoutes.RefusedAsync(publisher, mcp, refused, ErrorCode.PermissionDenied, token);
            await QueueProducerClusterAssertions.AbsentAsync(publisher, seed, QueueProducerClusterProtocol.Refused, QueueProducerClusterProtocol.Refused, token);
            await QueueProducerClusterAssertions.EqualAsync(await QueueProducerClusterAssertions.ImageAsync(publisher, seed,
                QueueProducerClusterProtocol.Original, token), original.Image);
            await MessagingClusterIdentity.UpdateAsync(fixture, seed.Identity.Principal with
            {
                PolicyEpoch = checked(demoted.PolicyEpoch + QueueProducerClusterProtocol.PolicyEpochAdvance)
            }, token);
            await QueueProducerClusterAssertions.DeniedAsync(await publisher.CommitAsync(seed.Original, token), ErrorCode.PermissionDenied);
            _ = (await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, seed.Original, token), ErrorCode.PermissionDenied, true)).Value;
            await AtomicProducerClusterRoutes.RefusedAsync(publisher, mcp, seed.Original, ErrorCode.PermissionDenied, token);
            await QueueProducerClusterAssertions.EqualAsync(await QueueProducerClusterAssertions.ImageAsync(publisher, seed,
                QueueProducerClusterProtocol.Original, token), original.Image);
        }, failures).ConfigureAwait(false);
    }
}
