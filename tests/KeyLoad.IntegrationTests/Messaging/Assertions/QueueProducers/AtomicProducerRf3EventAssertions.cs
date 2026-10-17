using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class AtomicProducerRf3EventAssertions
{
    internal static ReadStreamRequest Request(QueueProducerRf3Seed seed, string id)
        => new(new(seed.Lane.Partition, QueueProducerRf3Protocol.StreamSet, id));

    internal static async Task<AtomicProducerRf3Events> ReadAsync(KeyLoadClient client, QueueProducerRf3Seed seed,
        string id, CancellationToken token)
    {
        var request = Request(seed, id);
        var page = await McpCallerAssertions.SdkSuccessAsync(await client.ReadStreamAsync(request, token));
        await ValidateAsync(page, request);
        var image = new AtomicProducerRf3Events(page.Head, page.Events);
        var q1 = await SqlRf3Protocol.SdkAsync<StreamPage>(client,
            SqlRf3Protocol.Call(seed.Lane.Partition, McpCallerTools.StreamsRead, request), token);
        await ValidateAsync(q1, request);
        await QueueProducerRf3Assertions.EqualAsync(new AtomicProducerRf3Events(q1.Head, q1.Events), image);
        return image;
    }

    internal static async Task PublicAsync(McpOfficialClient mcp, QueueProducerRf3Seed seed,
        string id, AtomicProducerRf3Events expected, CancellationToken token)
    {
        var request = Request(seed, id);
        var page = (await McpCallerAssertions.SuccessAsync<StreamPage>(await mcp.CallAsync(McpCallerTools.StreamsRead, request, token))).Value;
        await ValidateAsync(page, request);
        await QueueProducerRf3Assertions.EqualAsync(new AtomicProducerRf3Events(page.Head, page.Events), expected);
        var q1 = await SqlRf3Protocol.McpAsync<StreamPage>(mcp,
            SqlRf3Protocol.Call(seed.Lane.Partition, McpCallerTools.StreamsRead, request), token);
        await ValidateAsync(q1, request);
        await QueueProducerRf3Assertions.EqualAsync(new AtomicProducerRf3Events(q1.Head, q1.Events), expected);
    }

    internal static async Task LiteralAsync(AtomicProducerRf3Events image, QueueProducerRf3Seed seed,
        string id, string payload, long sequence)
    {
        await QueueProducerRf3Assertions.EqualAsync(image.Head, new StreamHead(QueueProducerRf3Protocol.InitialEventRevision,
            QueueProducerRf3Protocol.FirstAvailableEventRevision, QueueProducerRf3Protocol.InitialEventGeneration));
        var record = await Assert.That(image.Records).HasSingleItem();
        await Assert.That(record.Stream).IsEqualTo(Request(seed, id).Stream);
        await Assert.That(record.Revision).IsEqualTo(QueueProducerRf3Protocol.InitialEventRevision);
        await Assert.That(record.EventSequence).IsEqualTo(sequence);
        await QueueProducerRf3Assertions.EqualAsync(record.Data,
            new EventData(id, QueueProducerRf3Protocol.EventType, payload, QueueProducerRf3Protocol.Headers));
        await Assert.That(record.RecordedAt).IsNotEqualTo(DateTimeOffset.MinValue);
    }

    internal static async Task AbsentAsync(KeyLoadClient client, QueueProducerRf3Seed seed, string id, CancellationToken token)
    {
        var image = await ReadAsync(client, seed, id, token);
        await Assert.That(image.Records).IsEmpty();
        await QueueProducerRf3Assertions.EqualAsync(image.Head, new StreamHead(QueueProducerRf3Protocol.EmptyStreamRevision,
            QueueProducerRf3Protocol.FirstAvailableEventRevision, QueueProducerRf3Protocol.InitialEventGeneration));
    }

    private static async Task ValidateAsync(StreamPage page, ReadStreamRequest request)
    {
        await Assert.That(page.Stream).IsEqualTo(request.Stream);
        await Assert.That(page.HasMore).IsFalse();
    }
}
