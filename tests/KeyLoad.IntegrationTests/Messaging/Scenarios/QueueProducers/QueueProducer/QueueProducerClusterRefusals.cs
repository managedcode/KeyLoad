using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueProducerClusterRefusals
{
    internal static async Task ExecuteAsync(ClusterFixture fixture, QueueProducerClusterOriginal original,
        List<Exception> failures, CancellationToken token)
    {
        var seed = original.Seed;
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var publisher = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Identity.Secret, token);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await AtomicProducerClusterBoundary.RefuseAsync(publisher, mcp, seed, token);
            var changed = seed.Original with
            {
                Mutations = [new PutDocument(QueueProducerClusterProtocol.Collection,
                QueueProducerClusterProtocol.Original, QueueProducerClusterProtocol.HealthyPayload)]
            };
            await QueueProducerClusterAssertions.DeniedAsync(await publisher.CommitAsync(changed, token), ErrorCode.Conflict);
            await QueueProducerClusterLiteralAssertions.UnchangedAsync(publisher, seed, original.Image, token);
            await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, changed, token), ErrorCode.Conflict, true);
            await QueueProducerClusterLiteralAssertions.UnchangedAsync(publisher, seed, original.Image, token);
            await QueueProducerClusterAssertions.EqualAsync(await QueueProducerClusterAssertions.ImageAsync(publisher, seed,
                QueueProducerClusterProtocol.Original, token), original.Image);
            await AtomicProducerClusterRoutes.RefusedAsync(publisher, mcp, changed, ErrorCode.Conflict, token);
            var duplicate = QueueProducerClusterSetup.Fresh(seed, QueueProducerClusterProtocol.Refused, QueueProducerClusterProtocol.Original);
            await QueueProducerClusterAssertions.DeniedAsync(await publisher.CommitAsync(duplicate, token), ErrorCode.Conflict);
            await QueueProducerClusterLiteralAssertions.UnchangedAsync(publisher, seed, original.Image, token);
            await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, duplicate, token), ErrorCode.Conflict, true);
            await QueueProducerClusterLiteralAssertions.UnchangedAsync(publisher, seed, original.Image, token);
            await QueueProducerClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await publisher.GetAsync(
                QueueProducerClusterAssertions.Document(seed, QueueProducerClusterProtocol.Refused), token)), new DocumentReadResult(null));
            await QueueProducerClusterAssertions.EqualAsync(await QueueProducerClusterAssertions.ImageAsync(publisher, seed,
                QueueProducerClusterProtocol.Original, token), original.Image);
            await AtomicProducerClusterEventAssertions.AbsentAsync(publisher, seed, QueueProducerClusterProtocol.Refused, token);
            await AtomicProducerClusterRoutes.RefusedAsync(publisher, mcp, duplicate, ErrorCode.Conflict, token);
            var quota = QueueProducerClusterSetup.Fresh(seed, QueueProducerClusterProtocol.Refused, QueueProducerClusterProtocol.Refused);
            await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, quota, token), ErrorCode.ResourceExhausted, true);
            await QueueProducerClusterLiteralAssertions.UnchangedAsync(publisher, seed, original.Image, token);
            await QueueProducerClusterAssertions.DeniedAsync(await publisher.CommitAsync(quota, token), ErrorCode.ResourceExhausted);
            await QueueProducerClusterLiteralAssertions.UnchangedAsync(publisher, seed, original.Image, token);
            await AtomicProducerClusterRoutes.RefusedAsync(publisher, mcp, quota, ErrorCode.ResourceExhausted, token);
            await QueueProducerClusterAssertions.AbsentAsync(publisher, seed, QueueProducerClusterProtocol.Refused, QueueProducerClusterProtocol.Refused, token);
            await QueueProducerClusterAssertions.EqualAsync(await QueueProducerClusterAssertions.ImageAsync(publisher, seed,
                QueueProducerClusterProtocol.Original, token), original.Image);
        }, failures).ConfigureAwait(false);
    }
}
