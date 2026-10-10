using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueProducerRf3Refusals
{
    internal static async Task ExecuteAsync(ClusterFixture fixture, QueueProducerRf3Original original,
        List<Exception> failures, CancellationToken token)
    {
        var seed = original.Seed;
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var publisher = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Identity.Secret, token);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var changed = seed.Original with
            {
                Mutations = [new PutDocument(QueueProducerRf3Protocol.Collection,
                QueueProducerRf3Protocol.Original, QueueProducerRf3Protocol.HealthyPayload)]
            };
            await QueueProducerRf3Assertions.DeniedAsync(await publisher.CommitAsync(changed, token), ErrorCode.Conflict);
            await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, changed, token), ErrorCode.Conflict, true);
            await QueueProducerRf3Assertions.EqualAsync(await QueueProducerRf3Assertions.ImageAsync(publisher, seed,
                QueueProducerRf3Protocol.Original, token), original.Image);
            var duplicate = QueueProducerRf3Setup.Fresh(seed, QueueProducerRf3Protocol.Refused, QueueProducerRf3Protocol.Original);
            await QueueProducerRf3Assertions.DeniedAsync(await publisher.CommitAsync(duplicate, token), ErrorCode.Conflict);
            await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, duplicate, token), ErrorCode.Conflict, true);
            await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await publisher.GetAsync(
                QueueProducerRf3Assertions.Document(seed, QueueProducerRf3Protocol.Refused), token))).IsNull();
            await QueueProducerRf3Assertions.EqualAsync(await QueueProducerRf3Assertions.ImageAsync(publisher, seed,
                QueueProducerRf3Protocol.Original, token), original.Image);
            var quota = QueueProducerRf3Setup.Fresh(seed, QueueProducerRf3Protocol.Refused, QueueProducerRf3Protocol.Refused);
            await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, quota, token), ErrorCode.ResourceExhausted, true);
            await QueueProducerRf3Assertions.DeniedAsync(await publisher.CommitAsync(quota, token), ErrorCode.ResourceExhausted);
            await QueueProducerRf3Assertions.AbsentAsync(publisher, seed, QueueProducerRf3Protocol.Refused, QueueProducerRf3Protocol.Refused, token);
            await QueueProducerRf3Assertions.EqualAsync(await QueueProducerRf3Assertions.ImageAsync(publisher, seed,
                QueueProducerRf3Protocol.Original, token), original.Image);
        }, failures).ConfigureAwait(false);
    }
}
