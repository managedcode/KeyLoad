using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Complete independent literal model values and full captured rows through every native public caller.</summary>
internal static class ClusterRestoreRf3ModelOracle
{
    private const long FirstRevision = 1;
    private const long BlobRangeAdjustment = 2;
    private const double UnitRank = 1d;
    private const string BlobRangeTool = "keyload_blobs_read_range";

    internal static async Task RequireAsync(KeyLoadClient sdk, McpOfficialClient official,
        PartitionMovementPublicParentRf3Seed seed, long originalCanonicalPosition, CancellationToken cancellationToken)
    {
        var models = seed.Models;
        var first = new DocumentResult(models.First, FirstRevision, RelationalSqlRf3Tokens.FirstRow, false, []);
        await ReadAsync(sdk, official, models.Partition, McpCallerTools.DocumentsGet,
            new GetDocumentRequest(models.First), first, () => sdk.GetAsync(models.First, cancellationToken), cancellationToken);
        await ReadAsync(sdk, official, models.Partition, McpCallerTools.DocumentsGet,
            new GetDocumentRequest(models.Second), new DocumentResult(models.Second, FirstRevision,
                RelationalSqlRf3Tokens.SecondRow, false, []), () => sdk.GetAsync(models.Second, cancellationToken), cancellationToken);
        var document = new EntityRef(models.Partition, RelationalSqlRf3Tokens.Documents, RelationalSqlRf3Tokens.DocumentId);
        await ReadAsync(sdk, official, models.Partition, McpCallerTools.DocumentsGet,
            new GetDocumentRequest(document), new DocumentResult(document, FirstRevision, RelationalSqlRf3Tokens.EmptyJson, false, []),
            () => sdk.GetAsync(document, cancellationToken), cancellationToken);
        await GraphsAsync(sdk, official, seed, cancellationToken).ConfigureAwait(false);
        await ReadAsync(sdk, official, models.Partition, McpCallerTools.SearchExecute, models.Search(),
            new RankedDocument[] { new(first, UnitRank) }, () => sdk.SearchAsync(models.Search(), cancellationToken), cancellationToken);
        await ReadAsync(sdk, official, models.Partition, McpCallerTools.SeriesRead,
            new ReadSamplesRequest(models.Partition, RelationalSqlRf3Tokens.SeriesSet, RelationalSqlRf3Tokens.SeriesId,
                RelationalSqlRf3Tokens.SampleAt, RelationalSqlRf3Tokens.SampleAt),
            new SampleRecord[] { new(RelationalSqlRf3Tokens.SeriesId, new(RelationalSqlRf3Tokens.SampleId,
                RelationalSqlRf3Tokens.SampleAt, RelationalSqlRf3Tokens.SampleValue), FirstRevision, RelationalSqlRf3Tokens.EmptyJson) },
            () => sdk.ReadSamplesAsync(new(models.Partition, RelationalSqlRf3Tokens.SeriesSet,
                RelationalSqlRf3Tokens.SeriesId, RelationalSqlRf3Tokens.SampleAt, RelationalSqlRf3Tokens.SampleAt), cancellationToken), cancellationToken);
        await StreamAsync(sdk, official, seed, originalCanonicalPosition, cancellationToken).ConfigureAwait(false);
        await QueuesAndBlobAsync(sdk, official, seed, cancellationToken).ConfigureAwait(false);
    }

    private static async Task GraphsAsync(KeyLoadClient sdk, McpOfficialClient official,
        PartitionMovementPublicParentRf3Seed seed, CancellationToken cancellationToken)
    {
        var models = seed.Models;
        var edge = new EdgeRecord(RelationalSqlRf3Tokens.EdgeId, models.First, models.Second,
            RelationalSqlRf3Tokens.EdgeLabel, RelationalSqlRf3Tokens.EmptyJson, FirstRevision);
        var request = new TraverseRequest(models.Partition, RelationalSqlRf3Tokens.Graph, models.First);
        await ReadAsync(sdk, official, models.Partition, McpCallerTools.GraphTraverse, request,
            new global::KeyLoad.GraphTraversal([models.First, models.Second], [edge]),
            () => sdk.TraverseAsync(request, cancellationToken), cancellationToken);
        var composed = request with { Graph = PartitionMovementPublicParentRf3Seed.Graph };
        await ReadAsync(sdk, official, models.Partition, McpCallerTools.GraphTraverse, composed,
            new global::KeyLoad.GraphTraversal([models.First, models.Second],
                [edge with { Id = PartitionMovementPublicParentRf3Seed.ComposedEdge }]),
            () => sdk.TraverseAsync(composed, cancellationToken), cancellationToken);
    }

    private static async Task StreamAsync(KeyLoadClient sdk, McpOfficialClient official,
        PartitionMovementPublicParentRf3Seed seed, long originalCanonicalPosition, CancellationToken cancellationToken)
    {
        var request = seed.Models.ReadStream();
        var original = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadStreamAsync(request, cancellationToken));
        await Assert.That(original.CutPosition).IsGreaterThanOrEqualTo(originalCanonicalPosition);
        await SqlRf3Protocol.EqualAsync(new StreamPage(seed.Models.Stream, new StreamHead(FirstRevision,
            FirstRevision, FirstRevision), seed.OriginalModels.Events, original.CutPosition, false), original);
        await Assert.That(original.Events).HasSingleItem();
        await SqlRf3Protocol.EqualAsync(new EventData(RelationalSqlRf3Tokens.EventId,
            RelationalSqlRf3Tokens.EventType, RelationalSqlRf3Tokens.EmptyJson), original.Events[0].Data);
        await ReadAsync(sdk, official, seed.Partition, McpCallerTools.StreamsRead, request, original,
            () => sdk.ReadStreamAsync(request, cancellationToken), cancellationToken);
    }

    private static async Task QueuesAndBlobAsync(KeyLoadClient sdk, McpOfficialClient official,
        PartitionMovementPublicParentRf3Seed seed, CancellationToken cancellationToken)
    {
        var work = seed.Models.Inspect();
        var forward = new InspectMessageRequest(new(seed.Partition, PartitionMovementPublicParentRf3Seed.Queue),
            PartitionMovementPublicParentRf3Seed.ForwardMessage);
        var reverse = forward with { Id = PartitionMovementPublicParentRf3Seed.DerivedMessage };
        await ReadAsync(sdk, official, seed.Partition, McpCallerTools.MessagesInspect, work, seed.OriginalModels.Work,
            () => sdk.InspectAsync(work, cancellationToken), cancellationToken);
        await ReadAsync(sdk, official, seed.Partition, McpCallerTools.MessagesInspect, forward, seed.OriginalModels.Forward,
            () => sdk.InspectAsync(forward, cancellationToken), cancellationToken);
        await ReadAsync(sdk, official, seed.Partition, McpCallerTools.MessagesInspect, reverse, seed.OriginalModels.Reverse,
            () => sdk.InspectAsync(reverse, cancellationToken), cancellationToken);
        var blob = new BlobReadRequest(seed.Blob.Blob, seed.Blob.Revision,
            BlobLimits.RawPartBytes - BlobRangeAdjustment, PartitionMovementPublicParentRf3Seed.BlobRange.Length);
        await ReadAsync(sdk, official, seed.Partition, BlobRangeTool, blob,
            new BlobReadResult(seed.Blob, blob.Offset, PartitionMovementPublicParentRf3Seed.BlobRange),
            () => sdk.ReadBlobRangeAsync(blob, cancellationToken), cancellationToken);
    }

    private static async Task ReadAsync<TRequest, TValue>(KeyLoadClient sdk, McpOfficialClient official,
        PartitionRef partition, string operation, TRequest request, TValue expected,
        Func<Task<Result<TValue>>> sdkCall, CancellationToken cancellationToken)
    {
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await sdkCall().ConfigureAwait(false)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<TValue>(await official
            .CallAsync(operation, request, cancellationToken).ConfigureAwait(false))).Value);
        var sql = SqlRf3Protocol.Call(partition, operation, request);
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<TValue>(sdk, sql, cancellationToken));
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.McpAsync<TValue>(official, sql, cancellationToken));
    }
}
