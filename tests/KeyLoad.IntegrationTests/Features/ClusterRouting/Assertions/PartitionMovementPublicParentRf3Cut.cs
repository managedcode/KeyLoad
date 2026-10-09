using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Complete original logical rows; owner-specific read cuts are checked separately, never erased from receipts.</summary>
internal sealed record PartitionMovementPublicParentRf3ModelCut(DocumentResult? First, DocumentResult? Second,
    DocumentResult? Document, global::KeyLoad.GraphTraversal Graph, global::KeyLoad.GraphTraversal CompositionGraph, RankedDocument[] Vectors,
    StreamHead StreamHead, ImmutableArray<EventRecord> Events, MessageInspection? Work,
    MessageInspection? Forward, MessageInspection? Reverse, SampleRecord[] Samples, BlobReadResult Blob);

internal static partial class PartitionMovementPublicParentRf3Cut
{
    internal static async Task<PartitionMovementPublicParentRf3ModelCut> CaptureAsync(
        PartitionMovementPublicParentRf3Seed seed, CancellationToken cancellationToken)
    {
        var models = seed.Models;
        var sdk = seed.Source;
        var first = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(models.First, cancellationToken));
        var second = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(models.Second, cancellationToken));
        var document = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(new(models.Partition,
            RelationalSqlRf3Tokens.Documents, RelationalSqlRf3Tokens.DocumentId), cancellationToken));
        var graph = await McpCallerAssertions.SdkSuccessAsync(await sdk.TraverseAsync(new(models.Partition,
            RelationalSqlRf3Tokens.Graph, models.First), cancellationToken));
        var composition = await McpCallerAssertions.SdkSuccessAsync(await sdk.TraverseAsync(new(models.Partition,
            PartitionMovementPublicParentRf3Seed.Graph, models.First), cancellationToken));
        var vectors = await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchAsync(models.Search(), cancellationToken));
        var stream = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadStreamAsync(models.ReadStream(), cancellationToken));
        var work = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(models.Inspect(), cancellationToken));
        var forward = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(new(new(models.Partition,
            PartitionMovementPublicParentRf3Seed.Queue), PartitionMovementPublicParentRf3Seed.ForwardMessage), cancellationToken));
        var reverse = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(new(new(models.Partition,
            PartitionMovementPublicParentRf3Seed.Queue), PartitionMovementPublicParentRf3Seed.DerivedMessage), cancellationToken));
        var samples = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadSamplesAsync(new(models.Partition,
            RelationalSqlRf3Tokens.SeriesSet, RelationalSqlRf3Tokens.SeriesId,
            RelationalSqlRf3Tokens.SampleAt, RelationalSqlRf3Tokens.SampleAt), cancellationToken));
        var blob = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadBlobRangeAsync(new(seed.Blob.Blob,
            seed.Blob.Revision, BlobLimits.RawPartBytes - 2, PartitionMovementPublicParentRf3Seed.BlobRange.Length), cancellationToken));
        await SqlRf3Protocol.EqualAsync(new BlobReadResult(seed.Blob, BlobLimits.RawPartBytes - 2,
            PartitionMovementPublicParentRf3Seed.BlobRange), blob);
        await Assert.That(stream.HasMore).IsFalse();
        await Assert.That(stream.CutPosition).IsGreaterThan(0L);
        return new(first, second, document, graph, composition, vectors, stream.Head, stream.Events,
            work, forward, reverse, samples, blob);
    }

    internal static async Task RequireModelsAsync(PartitionMovementPublicParentRf3Seed seed,
        CancellationToken cancellationToken)
    {
        var actual = await CaptureAsync(seed, cancellationToken).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(seed.OriginalModels, actual);
        await LiteralAsync(seed, actual);
        foreach (var original in seed.Originals)
        {
            var replay = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.CommitAsync(
                original.Command, cancellationToken).ConfigureAwait(false));
            await SqlRf3Protocol.EqualAsync(original.Receipt, replay);
            await SqlRf3Protocol.EqualAsync(original.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await seed.Official.CallAsync(McpCallerTools.DocumentsCommit, original.Command, cancellationToken)
                    .ConfigureAwait(false))).Value);
            var sql = SqlRf3Protocol.Call(seed.Partition, McpCallerTools.DocumentsCommit, original.Command,
                original.Command.CommandId);
            await SqlRf3Protocol.EqualAsync(original.Receipt,
                await SqlRf3Protocol.SdkAsync<CommitReceipt>(seed.Source, sql, cancellationToken));
            await SqlRf3Protocol.EqualAsync(original.Receipt,
                await SqlRf3Protocol.McpAsync<CommitReceipt>(seed.Official, sql, cancellationToken));
        }
    }

    private static async Task LiteralAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementPublicParentRf3ModelCut actual)
    {
        await SqlRf3Protocol.EqualAsync(new DocumentResult(seed.Models.First, 1, RelationalSqlRf3Tokens.FirstRow, false, []), actual.First);
        await SqlRf3Protocol.EqualAsync(new DocumentResult(seed.Models.Second, 1, RelationalSqlRf3Tokens.SecondRow, false, []), actual.Second);
        await SqlRf3Protocol.EqualAsync(new DocumentResult(new(seed.Partition, RelationalSqlRf3Tokens.Documents,
            RelationalSqlRf3Tokens.DocumentId), 1, "{}", false, []), actual.Document);
        var edge = new EdgeRecord(RelationalSqlRf3Tokens.EdgeId, seed.Models.First, seed.Models.Second,
            RelationalSqlRf3Tokens.EdgeLabel, "{}", 1);
        await SqlRf3Protocol.EqualAsync(new global::KeyLoad.GraphTraversal([seed.Models.First, seed.Models.Second], [edge]), actual.Graph);
        await SqlRf3Protocol.EqualAsync(new global::KeyLoad.GraphTraversal([seed.Models.First, seed.Models.Second],
            [edge with { Id = PartitionMovementPublicParentRf3Seed.ComposedEdge }]), actual.CompositionGraph);
        await SqlRf3Protocol.EqualAsync(new RankedDocument[] { new(actual.First!, 1d) }, actual.Vectors);
        await SqlRf3Protocol.EqualAsync(new StreamHead(1, 1, 1), actual.StreamHead);
        await Assert.That(actual.Events).HasSingleItem();
        await SqlRf3Protocol.EqualAsync(new EventData(RelationalSqlRf3Tokens.EventId,
            RelationalSqlRf3Tokens.EventType, "{}"), actual.Events[0].Data);
        await SqlRf3Protocol.EqualAsync(new SampleRecord[] { new(RelationalSqlRf3Tokens.SeriesId,
            new(RelationalSqlRf3Tokens.SampleId, RelationalSqlRf3Tokens.SampleAt, RelationalSqlRf3Tokens.SampleValue), 1, "{}") }, actual.Samples);
        await Assert.That(actual.Work!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(actual.Forward!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(actual.Reverse!.Metadata.State).IsEqualTo(MessageState.Ready);
    }
}
