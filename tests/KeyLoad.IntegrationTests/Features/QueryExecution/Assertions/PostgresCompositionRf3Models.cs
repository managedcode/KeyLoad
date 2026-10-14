using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DatabaseComposition;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresCompositionRf3Models
{
    internal static async Task<PostgresCompositionRf3Image> ReadAsync(KeyLoadClient sdk, CancellationToken token)
    {
        var partition = PostgresRf3State.Partition;
        var left = await PublicReadAssertions.DocumentAsync(await sdk.GetAsync(PostgresRf3State.Left, token));
        var right = await PublicReadAssertions.DocumentAsync(await sdk.GetAsync(PostgresRf3State.Right, token));
        await Assert.That(left).IsNotNull();
        await Assert.That(right).IsNotNull();
        var graph = await McpCallerAssertions.SdkSuccessAsync(await sdk.TraverseAsync(
            new(partition, PostgresCompositionRf3Protocol.Graph, PostgresRf3State.Left), token));
        var source = await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(
            new(new(partition, PostgresCompositionRf3Protocol.SourceQueue), PostgresCompositionRf3Protocol.Message), token));
        await Assert.That(source).IsNotNull();
        var target = await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(
            new(new(partition, PostgresCompositionRf3Protocol.TargetQueue), TargetId), token));
        return new(left!, right!, graph, source!, target);
    }

    internal static async Task LiteralAsync(PostgresCompositionRf3Image actual, bool reverse)
    {
        await SqlRf3Protocol.EqualAsync(new DocumentResult(PostgresRf3State.Left, PostgresRf3Protocol.SeedRevision,
            PostgresRf3State.LeftJson, false, []), actual.Left);
        await SqlRf3Protocol.EqualAsync(new DocumentResult(PostgresRf3State.Right, PostgresRf3Protocol.SeedRevision,
            PostgresRf3State.RightJson, false, []), actual.Right);
        var graph = new global::KeyLoad.GraphTraversal([PostgresRf3State.Left, PostgresRf3State.Right],
            [new EdgeRecord(EdgeId, PostgresRf3State.Left, PostgresRf3State.Right,
                PostgresCompositionRf3Protocol.Label, PostgresCompositionRf3Protocol.Attributes,
                PostgresCompositionRf3Protocol.FirstRevision)]);
        await SqlRf3Protocol.EqualAsync(graph, actual.Graph);
        await SourceAsync(actual.Source, PostgresCompositionRf3Protocol.Message);
        if (reverse)
        {
            await Assert.That(actual.Target).IsNotNull();
            await SourceAsync(actual.Target!, TargetId);
        }
        else
        { await Assert.That(actual.Target).IsNull(); }
    }

    private static async Task SourceAsync(MessageInspection message, string id)
    {
        var payload = DatabaseCompositionRf3Payload.OrdinalGolden(new QueueGraphLink(PostgresRf3State.Left,
            PostgresRf3State.Right, PostgresCompositionRf3Protocol.Label, PostgresCompositionRf3Protocol.Attributes));
        var expected = new MessageInspection(new MessageMetadata(id, MessageState.Ready, NoAttempts,
            PostgresCompositionRf3Protocol.FirstRevision, FirstSequence, null, null),
            payload, PostgresCompositionRf3Protocol.Attributes);
        await SqlRf3Protocol.EqualAsync(expected, message);
    }

    private const long FirstSequence = 1;
    private const int NoAttempts = 0;
    private const string EdgeId = PostgresCompositionRf3Protocol.EdgePrefix + PostgresCompositionRf3Protocol.Message;
    private const string TargetId = PostgresCompositionRf3Protocol.TargetPrefix + EdgeId;
}
