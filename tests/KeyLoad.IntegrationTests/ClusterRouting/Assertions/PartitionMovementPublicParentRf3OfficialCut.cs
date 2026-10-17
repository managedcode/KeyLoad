using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementPublicParentRf3OfficialCut
{
    internal static async Task RequireAsync(PartitionMovementPublicParentRf3Seed seed, CancellationToken token)
    {
        var models = seed.Models;
        var expected = seed.OriginalModels;
        await DocumentAsync(seed, models.First, expected.First, token).ConfigureAwait(false);
        await DocumentAsync(seed, models.Second, expected.Second, token).ConfigureAwait(false);
        await DocumentAsync(seed, new(models.Partition, RelationalSqlRf3Tokens.Documents,
            RelationalSqlRf3Tokens.DocumentId), expected.Document, token).ConfigureAwait(false);
        await ResultAsync(seed, McpCallerTools.GraphTraverse,
            new TraverseRequest(models.Partition, RelationalSqlRf3Tokens.Graph, models.First),
            expected.Graph, token).ConfigureAwait(false);
        await ResultAsync(seed, McpCallerTools.GraphTraverse,
            new TraverseRequest(models.Partition, PartitionMovementPublicParentRf3Seed.Graph, models.First),
            expected.CompositionGraph, token).ConfigureAwait(false);
        await ResultAsync(seed, McpCallerTools.SearchExecute, models.Search(), expected.Vectors, token).ConfigureAwait(false);
        await StreamAsync(seed, token).ConfigureAwait(false);
        await MessageAsync(seed, models.Inspect(), expected.Work, token).ConfigureAwait(false);
        await MessageAsync(seed, new(new(models.Partition, PartitionMovementPublicParentRf3Seed.Queue),
            PartitionMovementPublicParentRf3Seed.ForwardMessage), expected.Forward, token).ConfigureAwait(false);
        await MessageAsync(seed, new(new(models.Partition, PartitionMovementPublicParentRf3Seed.Queue),
            PartitionMovementPublicParentRf3Seed.DerivedMessage), expected.Reverse, token).ConfigureAwait(false);
        await ResultAsync(seed, McpCallerTools.SeriesRead,
            new ReadSamplesRequest(models.Partition, RelationalSqlRf3Tokens.SeriesSet, RelationalSqlRf3Tokens.SeriesId,
                RelationalSqlRf3Tokens.SampleAt, RelationalSqlRf3Tokens.SampleAt), expected.Samples, token).ConfigureAwait(false);
        await ResultAsync(seed, KeyLoad.Server.BlobToolNames.ReadRange,
            new BlobReadRequest(seed.Blob.Blob, seed.Blob.Revision, BlobLimits.RawPartBytes - 2,
                PartitionMovementPublicParentRf3Seed.BlobRange.Length), expected.Blob, token).ConfigureAwait(false);
    }

    private static async Task DocumentAsync(PartitionMovementPublicParentRf3Seed seed, EntityRef reference,
        DocumentResult? expected, CancellationToken token)
    {
        var actual = await PublicReadAssertions.DocumentAsync(await seed.Official.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token).ConfigureAwait(false)).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(expected, actual.Value);
    }

    private static async Task MessageAsync(PartitionMovementPublicParentRf3Seed seed, InspectMessageRequest request,
        MessageInspection? expected, CancellationToken token)
    {
        var actual = await PublicReadAssertions.MessageAsync(await seed.Official.CallAsync(
            McpCallerTools.MessagesInspect, request, token).ConfigureAwait(false)).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(expected, actual.Value);
    }

    private static async Task StreamAsync(PartitionMovementPublicParentRf3Seed seed, CancellationToken token)
    {
        var actual = (await McpCallerAssertions.SuccessAsync<StreamPage>(await seed.Official.CallAsync(
            McpCallerTools.StreamsRead, seed.Models.ReadStream(), token).ConfigureAwait(false)).ConfigureAwait(false)).Value;
        await SqlRf3Protocol.EqualAsync(seed.Models.Stream, actual.Stream);
        await SqlRf3Protocol.EqualAsync(seed.OriginalModels.StreamHead, actual.Head);
        await SqlRf3Protocol.EqualAsync(seed.OriginalModels.Events, actual.Events);
        await Assert.That(actual.HasMore).IsFalse();
        await Assert.That(actual.Cursor).IsNull();
        await Assert.That(actual.CutPosition).IsGreaterThan(0L);
        await Assert.That(actual.SnapshotCutPosition).IsEqualTo(actual.CutPosition);
    }

    private static async Task ResultAsync<TRequest, TResult>(PartitionMovementPublicParentRf3Seed seed,
        string tool, TRequest request, TResult expected, CancellationToken token)
    {
        var actual = (await McpCallerAssertions.SuccessAsync<TResult>(await seed.Official.CallAsync(
            tool, request, token).ConfigureAwait(false)).ConfigureAwait(false)).Value;
        await SqlRf3Protocol.EqualAsync(expected, actual);
    }
}
