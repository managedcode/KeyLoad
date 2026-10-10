using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueProducerRf3Assertions
{
    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    internal static async Task DeniedAsync<T>(Result<T> result, ErrorCode expected)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(expected.ToString());
    }
    internal static EntityRef Document(QueueProducerRf3Seed seed, string id)
        => new(seed.Lane.Partition, QueueProducerRf3Protocol.Collection, id);
    internal static async Task<QueueProducerRf3Image> ImageAsync(KeyLoadClient client, QueueProducerRf3Seed seed,
        string id, CancellationToken token)
    {
        var document = await McpCallerAssertions.SdkSuccessAsync(await client.GetAsync(Document(seed, id), token))
            ?? throw new InvalidOperationException(QueueProducerRf3Protocol.MissingState);
        var ready = await InspectAsync(client, seed, id, token);
        var scheduled = await InspectAsync(client, seed, QueueProducerRf3Protocol.Scheduled, token);
        var image = new QueueProducerRf3Image(document, ready, scheduled, await AtomicProducerRf3EventAssertions.ReadAsync(client, seed, id, token));
        await AtomicProducerRf3PublicImage.SdkAsync(client, seed, id, image, token);
        return image;
    }
    internal static async Task<MessageInspection> InspectAsync(KeyLoadClient client, QueueProducerRf3Seed seed,
        string id, CancellationToken token)
        => await McpCallerAssertions.SdkSuccessAsync(await client.InspectAsync(new(seed.Lane, id), token))
            ?? throw new InvalidOperationException(QueueProducerRf3Protocol.MissingState);
    internal static async Task AbsentAsync(KeyLoadClient client, QueueProducerRf3Seed seed,
        string document, string message, CancellationToken token)
    {
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await client.GetAsync(Document(seed, document), token))).IsNull();
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await client.InspectAsync(new(seed.Lane, message), token))).IsNull();
        await AtomicProducerRf3EventAssertions.AbsentAsync(client, seed, document, token);
    }
    internal static async Task ReplayAsync(KeyLoadClient client, McpOfficialClient mcp,
        CommandRequest command, CommitReceipt receipt, CancellationToken token)
    {
        await EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await client.CommitAsync(command, token)), receipt);
        await AtomicProducerRf3Routes.ReplayAsync(client, mcp, command, receipt, token);
        await EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
            McpCallerTools.DocumentsCommit, command, token))).Value, receipt);
    }
    internal static async Task PublicImageAsync(McpOfficialClient mcp, QueueProducerRf3Seed seed,
        string id, QueueProducerRf3Image image, CancellationToken token)
    {
        await AtomicProducerRf3PublicImage.McpAsync(mcp, seed, id, image, token);
        await AtomicProducerRf3EventAssertions.PublicAsync(mcp, seed, id, image.Events, token);
        await EqualAsync((await McpCallerAssertions.SuccessAsync<DocumentResult?>(await mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(Document(seed, id)), token))).Value, image.Document);
        await EqualAsync((await McpCallerAssertions.SuccessAsync<MessageInspection?>(await mcp.CallAsync(
            McpCallerTools.MessagesInspect, new InspectMessageRequest(seed.Lane, id), token))).Value, image.Ready);
        await EqualAsync((await McpCallerAssertions.SuccessAsync<MessageInspection?>(await mcp.CallAsync(
            McpCallerTools.MessagesInspect, new InspectMessageRequest(seed.Lane, QueueProducerRf3Protocol.Scheduled), token))).Value, image.Scheduled);
    }

}
