using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using KeyLoad.Client;

namespace KeyLoad.Comparisons.Targets;

internal sealed class KeyLoadComparisonSession(KeyLoadClient client, PartitionRef partition, VectorSpace space,
    int topK, int graphDepth, int graphVertices, int graphEdges, int corpusCount) : IComparisonSession
{
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
        string? cursor = null;
        var observedCount = 0;
        do
        {
            var query = KeyLoadQuery.From<CorpusQueryMarker>(partition, "documents")
                .OrderBy(row => QueryFunctions.DocumentId(row)).Take(256);
            var page = KeyLoadClientResults.Success(await client.QueryAsync(query, allowFullScan: true,
                cursor: cursor, cancellationToken: cancellationToken), "ScaledCorpusReadback");
            foreach (var row in page.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++observedCount > corpusCount)
                {
                    throw new ComparisonFailureException("ScaledCorpusReadbackExtraRecord");
                }

                yield return new(row.EntityId, row.Json);
            }
            if (page.Rows.IsEmpty && page.Cursor is not null)
            {
                throw new ComparisonFailureException("ScaledCorpusReadbackCursorDidNotAdvance");
            }
            cursor = page.Cursor;
        } while (cursor is not null);

        if (observedCount != corpusCount)
        {
            throw new ComparisonFailureException("ScaledCorpusReadbackCountMismatch");
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed record CorpusQueryMarker;

    public async Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken)
    {
        var found = KeyLoadClientResults.Success(await client.GetAsync(
            new(partition, "documents", document.Id), cancellationToken));
        return found is null ? null : new(found.Reference.Id, found.Json);
    }

    public Task<FoundEvent?> ReadEventAsync(BenchmarkDocument document, CancellationToken cancellationToken)
        => KeyLoadEventOperations.ReadAsync(client, partition, document, cancellationToken);

    private async Task<OperationResult> SearchVectorAsync(BenchmarkDocument document,
        CancellationToken cancellationToken)
    {
        var neighbors = KeyLoadClientResults.Success(await client.SearchAsync(new(partition, "documents",
            VectorField: "/embedding", Vector: document.Vector, Space: space, Limit: topK), cancellationToken));
        var owned = neighbors.Select(item => new FoundDocument(item.Document.Reference.Id, item.Document.Json)).ToArray();
        return new(Neighbors: ImmutableCollectionsMarshal.AsImmutableArray(owned));
    }

    private async Task<OperationResult> TraverseGraphAsync(Scenario scenario, BenchmarkDocument document,
        CancellationToken cancellationToken)
    {
        var graph = KeyLoadClientResults.Success(await client.TraverseAsync(new(partition, "links",
            new(partition, "documents", document.Id), scenario == Scenario.GraphNeighbors ? 1 : graphDepth,
            graphVertices, graphEdges, ["links"]), cancellationToken));
        var owned = graph.Vertices.Where(vertex => vertex.Id != document.Id).Select(vertex => vertex.Id)
            .Order(StringComparer.Ordinal).ToArray();
        return new(Vertices: ImmutableCollectionsMarshal.AsImmutableArray(owned));
    }

    private async Task<OperationResult> ExecuteQueueCycleAsync(BenchmarkDocument document,
        CancellationToken cancellationToken)
    {
        var lane = new QueueLaneRef(partition, "jobs");
        var begin = Stopwatch.GetTimestamp();
        KeyLoadClientResults.Success(await client.CommitAsync(new(Guid.NewGuid(), partition,
            [new EnqueueMessage("jobs", document.Id, document.Json)]), cancellationToken));
        var enqueued = Stopwatch.GetTimestamp();
        Delivery? delivery = null;
        while (delivery is null)
        {
            delivery = KeyLoadClientResults.Success(await client.ReceiveAsync(new(Guid.NewGuid(), lane),
                cancellationToken)).Deliveries.SingleOrDefault();
            if (delivery is null)
            {
                await Task.Delay(1, cancellationToken);
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
