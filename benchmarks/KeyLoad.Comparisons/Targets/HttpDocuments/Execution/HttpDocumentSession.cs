using System.Runtime.CompilerServices;

namespace KeyLoad.Comparisons.Targets;

internal sealed class HttpDocumentSession(HttpDocumentStore store) : IDocumentComparisonSession
{
    private const int EmptyCount = 0, SingleCount = 1;
    private const string ExtraRows = "HttpDocumentDatabaseExtraRows";
    public Guid ClientIdentity { get; } = Guid.NewGuid();
    public bool SingleOperationTiming => true;

    public async Task<OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken cancellationToken)
    {
        if (scenario == Scenario.PointRead)
        {
            return new(Document: await ReadAsync(document, cancellationToken).ConfigureAwait(false));
        }
        await store.WriteAsync(scenario, document, cancellationToken).ConfigureAwait(false);
        return new();
    }

    public async Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken)
    {
        using var result = await store.ReadAsync(document.Id, null, cancellationToken).ConfigureAwait(false);
        return One(HttpDocumentStore.Documents(result));
    }

    internal static FoundDocument? One(FoundDocument[] rows) => rows.Length switch
    {
        EmptyCount => null,
        SingleCount => rows[EmptyCount],
        _ => throw new ComparisonFailureException(ExtraRows)
    };

    public IAsyncEnumerable<FoundDocument> ReadCorpusAsync(CancellationToken cancellationToken)
        => ReadDocumentsAsync(cancellationToken);

    // One native bulk write per batch with the single create's request settings and reply check.
    public Task CreateDocumentsAsync(IReadOnlyList<BenchmarkDocument> documents, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == EmptyCount || documents.Count > store.BatchRecords)
        {
            throw new ArgumentOutOfRangeException(nameof(documents));
        }
        return store.CreateAsync(documents, cancellationToken);
    }

    public async IAsyncEnumerable<FoundDocument> ReadDocumentsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const string InvalidOrder = "HttpDocumentDatabaseReadbackOrderRejected";
        var after = string.Empty;
        while (true)
        {
            using var result = await store.PageAsync(after, cancellationToken).ConfigureAwait(false);
            var rows = HttpDocumentStore.Documents(result);
            if (rows.Length == EmptyCount)
            {
                yield break;
            }
            foreach (var row in rows)
            {
                if (string.CompareOrdinal(row.Id, after) <= EmptyCount)
                {
                    throw new ComparisonFailureException(InvalidOrder);
                }
                after = row.Id;
                yield return row;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
