using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class AtomicProducerClusterEventAssertions
{
    internal static ReadStreamRequest Request(QueueProducerClusterSeed seed, string id)
        => new(new(seed.Lane.Partition, QueueProducerClusterProtocol.StreamSet, id));

    internal static async Task<AtomicProducerClusterEvents> ReadAsync(KeyLoadClient client, QueueProducerClusterSeed seed,
        string id, CancellationToken token)
    {
        var request = Request(seed, id);
        var page = await McpCallerAssertions.SdkSuccessAsync(await client.ReadStreamAsync(request, token));
        await ValidateAsync(page, request);
        var image = new AtomicProducerClusterEvents(page.Head, page.Events);
        var q1 = await SqlClusterProtocol.SdkAsync<StreamPage>(client,
            SqlClusterProtocol.Call(seed.Lane.Partition, McpCallerTools.StreamsRead, request), token);
        await ValidateAsync(q1, request);
        await QueueProducerClusterAssertions.EqualAsync(new AtomicProducerClusterEvents(q1.Head, q1.Events), image);
        return image;
    }

    internal static async Task PublicAsync(McpOfficialClient mcp, QueueProducerClusterSeed seed,
        string id, AtomicProducerClusterEvents expected, CancellationToken token)
    {
        var request = Request(seed, id);
        var page = (await McpCallerAssertions.SuccessAsync<StreamPage>(await mcp.CallAsync(McpCallerTools.StreamsRead, request, token))).Value;
        await ValidateAsync(page, request);
        await QueueProducerClusterAssertions.EqualAsync(new AtomicProducerClusterEvents(page.Head, page.Events), expected);
        var q1 = await SqlClusterProtocol.McpAsync<StreamPage>(mcp,
            SqlClusterProtocol.Call(seed.Lane.Partition, McpCallerTools.StreamsRead, request), token);
        await ValidateAsync(q1, request);
        await QueueProducerClusterAssertions.EqualAsync(new AtomicProducerClusterEvents(q1.Head, q1.Events), expected);
    }

    internal static async Task LiteralAsync(AtomicProducerClusterEvents image, QueueProducerClusterSeed seed,
        string id, string payload, long sequence)
    {
        await QueueProducerClusterAssertions.EqualAsync(image.Head, new StreamHead(QueueProducerClusterProtocol.InitialEventRevision,
            QueueProducerClusterProtocol.FirstAvailableEventRevision, QueueProducerClusterProtocol.InitialEventGeneration));
        var record = await Assert.That(image.Records).HasSingleItem();
        await Assert.That(record.Stream).IsEqualTo(Request(seed, id).Stream);
        await Assert.That(record.Revision).IsEqualTo(QueueProducerClusterProtocol.InitialEventRevision);
        await Assert.That(record.EventSequence).IsEqualTo(sequence);
        await QueueProducerClusterAssertions.EqualAsync(record.Data,
            new EventData(id, QueueProducerClusterProtocol.EventType, payload, QueueProducerClusterProtocol.Headers));
        await Assert.That(record.RecordedAt).IsNotEqualTo(DateTimeOffset.MinValue);
    }

    internal static async Task AbsentAsync(KeyLoadClient client, QueueProducerClusterSeed seed, string id, CancellationToken token)
    {
        var image = await ReadAsync(client, seed, id, token);
        await Assert.That(image.Records).IsEmpty();
        await QueueProducerClusterAssertions.EqualAsync(image.Head, new StreamHead(QueueProducerClusterProtocol.EmptyStreamRevision,
            QueueProducerClusterProtocol.FirstAvailableEventRevision, QueueProducerClusterProtocol.InitialEventGeneration));
    }

    private static async Task ValidateAsync(StreamPage page, ReadStreamRequest request)
    {
        await Assert.That(page.Stream).IsEqualTo(request.Stream);
        await Assert.That(page.HasMore).IsFalse();
    }
}
