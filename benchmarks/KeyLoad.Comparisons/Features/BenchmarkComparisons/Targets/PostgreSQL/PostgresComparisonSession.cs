using System.Runtime.CompilerServices;
using Npgsql;

namespace KeyLoad.Comparisons.Targets;

internal sealed class PostgresComparisonSession(NpgsqlConnection connection, int topK, int graphDepth, int corpusCount)
    : IComparisonSession
{
    public async IAsyncEnumerable<FoundDocument> ReadCorpusAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,body::text FROM documents ORDER BY id COLLATE \"C\"";
        await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, cancellationToken);
        var seen = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (seen >= corpusCount)
            {
                throw new ComparisonFailureException("ScaledCorpusReadbackExtraRecord");
            }
            yield return new(reader.GetString(0), reader.GetString(1));
            seen++;
        }
        if (seen != corpusCount)
        {
            throw new ComparisonFailureException("ScaledCorpusReadbackCountMismatch");
        }
    }

    public Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken)
        => PostgresDocumentOperations.ReadAsync(connection, document, cancellationToken);

    public Task<FoundEvent?> ReadEventAsync(BenchmarkDocument document, CancellationToken cancellationToken)
        => PostgresStreamOperations.ReadAsync(connection, document, cancellationToken);

    public async Task<OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document,
        CancellationToken cancellationToken)
    {
        switch (scenario)
        {
            case Scenario.PointRead:
                return new(Document: await ReadAsync(document, cancellationToken));
            case Scenario.DocumentWrite:
                await PostgresDocumentOperations.WriteAsync(connection, document, cancellationToken);
                return new();
            case Scenario.DocumentUpdate:
                await PostgresDocumentOperations.UpdateAsync(connection, document, cancellationToken);
                return new();
            case Scenario.DocumentDelete:
                await PostgresDocumentOperations.DeleteAsync(connection, document, cancellationToken);
                return new();
            case Scenario.VectorExact:
                return new(Neighbors: await PostgresVectorOperations.SearchAsync(connection, document, topK, cancellationToken));
            case Scenario.QueueCycle:
                return await PostgresQueueOperations.ExecuteAsync(connection, document, cancellationToken);
            case Scenario.GraphNeighbors:
            case Scenario.GraphTraverse:
                return await PostgresGraphOperations.ExecuteAsync(connection, scenario, document, graphDepth, cancellationToken);
            case Scenario.StreamAppend:
                await PostgresStreamOperations.AppendAsync(connection, document, cancellationToken);
                return new();
            case Scenario.StreamRead:
                return new(Event: await PostgresStreamOperations.ReadAsync(connection, document, cancellationToken));
            default:
                throw new NotSupportedException();
        }
    }

    public ValueTask DisposeAsync() => connection.DisposeAsync();
}
