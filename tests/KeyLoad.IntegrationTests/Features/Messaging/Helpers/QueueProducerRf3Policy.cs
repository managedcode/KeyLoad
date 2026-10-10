using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueProducerRf3Policy
{
    internal static async Task RestoreAsync(ClusterFixture fixture, QueueProducerRf3Original original,
        List<Exception> failures, CancellationToken token)
    {
        var seed = original.Seed;
        var demoted = seed.Identity.Principal with
        {
            PolicyEpoch = checked(seed.Identity.Principal.PolicyEpoch + QueueProducerRf3Protocol.PolicyEpochAdvance),
            Grants = [new(seed.Lane.Partition.DatabaseId, seed.Lane.Queue, Capability.QueueInspect),
                new(seed.Lane.Partition.DatabaseId, QueueProducerRf3Protocol.Collection, Capability.DocumentsRead | Capability.Query),
                new(seed.Lane.Partition.DatabaseId, QueueProducerRf3Protocol.StreamSet, Capability.EventsRead | Capability.Query),
                new(seed.Lane.Partition.DatabaseId, QueueProducerRf3Protocol.ForeignStreamSet, Capability.EventsRead | Capability.Query)]
        };
        await MessagingRf3Identity.UpdateAsync(fixture, demoted, token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var publisher = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Identity.Secret, token);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var refused = QueueProducerRf3Setup.Fresh(seed, QueueProducerRf3Protocol.Refused, QueueProducerRf3Protocol.Refused);
            await QueueProducerRf3Assertions.DeniedAsync(await publisher.CommitAsync(refused, token), ErrorCode.PermissionDenied);
            await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, refused, token), ErrorCode.PermissionDenied, true);
            await AtomicProducerRf3Routes.RefusedAsync(publisher, mcp, refused, ErrorCode.PermissionDenied, token);
            await QueueProducerRf3Assertions.AbsentAsync(publisher, seed, QueueProducerRf3Protocol.Refused, QueueProducerRf3Protocol.Refused, token);
            await QueueProducerRf3Assertions.EqualAsync(await QueueProducerRf3Assertions.ImageAsync(publisher, seed,
                QueueProducerRf3Protocol.Original, token), original.Image);
            await MessagingRf3Identity.UpdateAsync(fixture, seed.Identity.Principal with { PolicyEpoch = checked(demoted.PolicyEpoch + QueueProducerRf3Protocol.PolicyEpochAdvance) }, token);
            await QueueProducerRf3Assertions.DeniedAsync(await publisher.CommitAsync(seed.Original, token), ErrorCode.PermissionDenied);
            await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, seed.Original, token), ErrorCode.PermissionDenied, true);
            await AtomicProducerRf3Routes.RefusedAsync(publisher, mcp, seed.Original, ErrorCode.PermissionDenied, token);
            await QueueProducerRf3Assertions.EqualAsync(await QueueProducerRf3Assertions.ImageAsync(publisher, seed,
                QueueProducerRf3Protocol.Original, token), original.Image);
        }, failures).ConfigureAwait(false);
    }
}
