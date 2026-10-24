using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueProducerClusterAssertions
{
    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    internal static async Task DeniedAsync<T>(Result<T> result, ErrorCode expected)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(expected.ToString());
    }
    internal static async Task DeniedAsync(Result<JsonElement> result, ErrorCode expected)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(expected.ToString());
    }
    internal static EntityRef Document(QueueProducerClusterSeed seed, string id)
        => new(seed.Lane.Partition, QueueProducerClusterProtocol.Collection, id);
    internal static EntityRef StoredDocument(QueueProducerClusterSeed seed, string id)
        => Document(seed, id) with { Lifetime = seed.CollectionLifetime };
    internal static async Task<QueueProducerClusterImage> ImageAsync(KeyLoadClient client, QueueProducerClusterSeed seed,
        string id, CancellationToken token)
    {
        var document = await PublicReadAssertions.DocumentAsync(await client.GetAsync(Document(seed, id), token))
            ?? throw new InvalidOperationException(QueueProducerClusterProtocol.MissingState);
        var ready = await InspectAsync(client, seed, id, token);
        var scheduled = await InspectAsync(client, seed, QueueProducerClusterProtocol.Scheduled, token);
        var image = new QueueProducerClusterImage(document, ready, scheduled, await AtomicProducerClusterEventAssertions.ReadAsync(client, seed, id, token));
        await AtomicProducerClusterPublicImage.SdkAsync(client, seed, id, image, token);
        return image;
    }
    internal static async Task<MessageInspection> InspectAsync(KeyLoadClient client, QueueProducerClusterSeed seed,
        string id, CancellationToken token)
        => await PublicReadAssertions.MessageAsync(await client.InspectAsync(new(seed.Lane, id), token))
            ?? throw new InvalidOperationException(QueueProducerClusterProtocol.MissingState);
    internal static async Task AbsentAsync(KeyLoadClient client, QueueProducerClusterSeed seed,
        string document, string message, CancellationToken token)
    {
        await Assert.That(await PublicReadAssertions.DocumentAsync(await client.GetAsync(Document(seed, document), token))).IsNull();
        await Assert.That(await PublicReadAssertions.MessageAsync(await client.InspectAsync(new(seed.Lane, message), token))).IsNull();
        await AtomicProducerClusterEventAssertions.AbsentAsync(client, seed, document, token);
    }
    internal static async Task ReplayAsync(KeyLoadClient client, McpOfficialClient mcp,
        CommandRequest command, CommitReceipt receipt, CancellationToken token)
    {
        await EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await client.CommitAsync(command, token)), receipt);
        await AtomicProducerClusterRoutes.ReplayAsync(client, mcp, command, receipt, token);
        await EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
            McpCallerTools.DocumentsCommit, command, token))).Value, receipt);
    }
    internal static async Task PublicImageAsync(McpOfficialClient mcp, QueueProducerClusterSeed seed,
        string id, QueueProducerClusterImage image, CancellationToken token)
    {
        await AtomicProducerClusterPublicImage.McpAsync(mcp, seed, id, image, token);
        await AtomicProducerClusterEventAssertions.PublicAsync(mcp, seed, id, image.Events, token);
        await EqualAsync((await PublicReadAssertions.DocumentAsync(await mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(Document(seed, id)), token))).Value, image.Document);
        await EqualAsync((await PublicReadAssertions.MessageAsync(await mcp.CallAsync(
            McpCallerTools.MessagesInspect, new InspectMessageRequest(seed.Lane, id), token))).Value, image.Ready);
        await EqualAsync((await PublicReadAssertions.MessageAsync(await mcp.CallAsync(
            McpCallerTools.MessagesInspect, new InspectMessageRequest(seed.Lane, QueueProducerClusterProtocol.Scheduled), token))).Value, image.Scheduled);
    }

}
