using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using KeyLoad.Client;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal sealed class KeyLoadComparisonSession(KeyLoadClient client, PartitionRef partition, VectorSpace space,
    int topK, int graphDepth, int graphVertices, int graphEdges, int corpusCount,
    IOptions<ComparisonLifecycleOptions> lifecycleOptions, IOptions<NativeComparisonExecutionOptions> nativeExecutionOptions,
    IOptions<QueryTranslationOptions> translationOptions)
    : IComparisonSession, IOpenLoopCancellationHealthSession, IOpenLoopComparisonSession
{
    private readonly NativeComparisonExecutionOptions execution = NativeComparisonExecutionOptions.Require(nativeExecutionOptions).Value;
    private readonly ComparisonLifecycleOptions lifecycle = lifecycleOptions.Value;

    public async Task<OpenLoopSessionResult> ExecuteOpenLoopAsync(Scenario scenario,
        BenchmarkDocument document, CancellationToken cancellationToken)
    {
        if (scenario == Scenario.PointRead)
        {
            var response = await client.GetAsync(new(partition, OpenLoopProtocolIdentities.DocumentsCollection, document.Id), cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                return KeyLoadOpenLoopResults.RejectOrThrow(response.Problem?.ErrorCode);
            }
            var found = response.Value;
            var result = found is null ? null : new FoundDocument(found.Reference.Id, found.Json);
            return new(OpenLoopSessionDisposition.Succeeded, new(Document: result));
        }

        return await KeyLoadDocumentOperations.ExecuteOpenLoopAsync(client, partition, scenario,
            document, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document,
        CancellationToken cancellationToken)
    {
        switch (scenario)
        {
            case Scenario.PointRead:
                return new(Document: await ReadAsync(document, cancellationToken));
            case Scenario.StreamRead:
                return new(Event: await KeyLoadEventOperations.ReadAsync(client, partition, document, cancellationToken));
            case Scenario.StreamAppend:
                await KeyLoadEventOperations.AppendAsync(client, partition, document, cancellationToken);
                return new();
            case Scenario.DocumentWrite:
            case Scenario.DocumentUpdate:
            case Scenario.DocumentDelete:
                return await KeyLoadDocumentOperations.ExecuteAsync(client, partition, scenario, document, cancellationToken);
            case Scenario.VectorExact:
                return await SearchVectorAsync(document, cancellationToken);
            case Scenario.GraphNeighbors:
            case Scenario.GraphTraverse:
                return await TraverseGraphAsync(scenario, document, cancellationToken);
            case Scenario.QueueCycle:
                return await ExecuteQueueCycleAsync(document, cancellationToken);
            default:
                throw new NotSupportedException();
        }
    }

    public async IAsyncEnumerable<FoundDocument> ReadCorpusAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const int FirstElementIndex = 0;

        string? cursor = null;
        var observedCount = FirstElementIndex;
        do
        {
            var query = KeyLoadQuery.From<CorpusQueryMarker>(partition,
                OpenLoopProtocolIdentities.DocumentsCollection, translationOptions)
                .OrderBy(row => QueryFunctions.DocumentId(row))
                .Take(execution.ReadbackBatchCapacity);
            var page = KeyLoadClientResults.Success(await client.QueryAsync(query, allowFullScan: true,
                cursor: cursor, cancellationToken: cancellationToken), ScaledCorpusReadbackFailureCodes.Operation);
            foreach (var row in page.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++observedCount > corpusCount)
                {
                    throw new ComparisonFailureException(ScaledCorpusReadbackFailureCodes.ExtraRecord);
                }

                yield return new(row.EntityId, row.Json);
            }
            if (page.Rows.IsEmpty && page.Cursor is not null)
            {
                throw new ComparisonFailureException(ScaledCorpusReadbackFailureCodes.CursorDidNotAdvance);
            }
            cursor = page.Cursor;
        } while (cursor is not null);

        if (observedCount != corpusCount)
        {
            throw new ComparisonFailureException(ScaledCorpusReadbackFailureCodes.CountMismatch);
        }
    }

    async Task<OpenLoopCancellationHealthRead> IOpenLoopCancellationHealthSession.ReadActualAsync(
        BenchmarkDocument document, CancellationToken cancellationToken)
    {
        var requested = new EntityRef(partition, OpenLoopProtocolIdentities.DocumentsCollection, document.Id);
        var actual = KeyLoadClientResults.Success(await client.GetAsync(requested, cancellationToken)
            .ConfigureAwait(false), OpenLoopProtocolIdentities.CancellationHealthyReadOperation);
        return actual is null
            ? throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopCancellationHealthyReadMissing)
            : new(requested, actual);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed record CorpusQueryMarker;

    public async Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken)
    {
        var found = KeyLoadClientResults.Success(await client.GetAsync(
            new(partition, OpenLoopProtocolIdentities.DocumentsCollection, document.Id), cancellationToken));
        return found is null ? null : new(found.Reference.Id, found.Json);
    }

    public Task<FoundEvent?> ReadEventAsync(BenchmarkDocument document, CancellationToken cancellationToken)
        => KeyLoadEventOperations.ReadAsync(client, partition, document, cancellationToken);

    private async Task<OperationResult> SearchVectorAsync(BenchmarkDocument document,
        CancellationToken cancellationToken)
    {
        var request = new SearchRequest(partition, OpenLoopProtocolIdentities.DocumentsCollection,
            VectorField: KeyLoadWorkloadIdentities.VectorEmbeddingField, Vector: document.Vector,
            Space: space, Limit: topK);
        var neighbors = KeyLoadClientResults.Success(await client.SearchAsync(request, cancellationToken));
        var owned = neighbors.Select(item => new FoundDocument(item.Document.Reference.Id, item.Document.Json)).ToArray();
        return new(Neighbors: ImmutableCollectionsMarshal.AsImmutableArray(owned));
    }

    private async Task<OperationResult> TraverseGraphAsync(Scenario scenario, BenchmarkDocument document,
        CancellationToken cancellationToken)
    {
        var request = new TraverseRequest(partition, KeyLoadWorkloadIdentities.GraphName,
            new(partition, OpenLoopProtocolIdentities.DocumentsCollection, document.Id),
            scenario == Scenario.GraphNeighbors ? KeyLoadWorkloadIdentities.NeighborTraversalDepth : graphDepth,
            graphVertices, graphEdges, [KeyLoadWorkloadIdentities.GraphName]);
        var graph = KeyLoadClientResults.Success(await client.TraverseAsync(request, cancellationToken));
        var owned = graph.Vertices.Where(vertex => vertex.Id != document.Id).Select(vertex => vertex.Id)
            .Order(StringComparer.Ordinal).ToArray();
        return new(Vertices: ImmutableCollectionsMarshal.AsImmutableArray(owned));
    }

    private async Task<OperationResult> ExecuteQueueCycleAsync(BenchmarkDocument document,
        CancellationToken cancellationToken)
    {
        var lane = new QueueLaneRef(partition, KeyLoadWorkloadIdentities.QueueName);
        var begin = Stopwatch.GetTimestamp();
        KeyLoadClientResults.Success(await client.CommitAsync(new(Guid.NewGuid(), partition,
            [new EnqueueMessage(KeyLoadWorkloadIdentities.QueueName, document.Id, document.Json)]), cancellationToken));
        var enqueued = Stopwatch.GetTimestamp();
        Delivery? delivery = null;
        while (delivery is null)
        {
            var request = new ReceiveRequest(Guid.NewGuid(), lane,
                LeaseSeconds: checked((int)Math.Ceiling(lifecycle.QueueLeaseDuration.TotalSeconds)));
            delivery = KeyLoadClientResults.Success(await client.ReceiveAsync(request, cancellationToken))
                .Deliveries.SingleOrDefault();
            if (delivery is null)
            {
                await Task.Delay(lifecycle.QueueClaimPollInterval, cancellationToken);
            }
        }
        var received = Stopwatch.GetTimestamp();
        KeyLoadClientResults.Success(await client.CompleteAsync(new(Guid.NewGuid(), lane, delivery.Token,
            DeliveryAction.Ack), cancellationToken));
        return new(Message: new(delivery.Id, delivery.PayloadJson), Queue: new(
            Stopwatch.GetElapsedTime(begin, enqueued).TotalMilliseconds,
            Stopwatch.GetElapsedTime(enqueued, received).TotalMilliseconds,
            Stopwatch.GetElapsedTime(received).TotalMilliseconds));
    }
}
