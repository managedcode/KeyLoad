using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.ComparisonHost.Deployment;

namespace KeyLoad.ComparisonHost.Search;

/// <summary>
/// KeyLoad through its SDK: documents with a <c>text</c> field are created with <see cref="PutDocument"/> (a batch is one
/// commit); <c>WaitForIndexAsync</c> publishes the ZoneTree.FullTextSearch lexical generation at the highest acknowledged
/// commit, and <c>SearchAsync</c> runs the text branch with KeyLoad's BM25 ranking. KeyLoad text queries have no AND
/// operator: any document containing at least one query term is a candidate, ranked by BM25.
/// </summary>
internal sealed class KeyLoadSearchDriver(BenchmarkConnection connection) : ISearchDriver
{
    private const string Collection = "documents";
    private const string TextField = "/text";
    private const long NewDocument = 0;
    private const char Separator = ' ';
    private const string NothingInserted = "Insert KeyLoad documents before building the full-text index.";
    private readonly KeyLoadDatabase database = new(connection);
    private CommitToken? latest;

    public int BatchSize => 100;

    public Task PrepareAsync(CancellationToken token)
        => database.ConfigureAsync(database.Resource(Collection, ResourceKind.Collection), token);

    public async Task InsertAsync(IReadOnlyList<SearchDocument> documents, CancellationToken token)
    {
        var mutations = documents.Select(document => (Mutation)new PutDocument(Collection, document.Id,
            JsonSerializer.Serialize(new { text = document.Text }), NewDocument)).ToImmutableArray();
        Observe((await database.CommitAsync(mutations, token)).Token);
    }

    /// <summary>Returns once the lexical index covers every acknowledged insert.</summary>
    public async Task BuildIndexAsync(CancellationToken token)
    {
        var minimum = Volatile.Read(ref latest) ?? throw new InvalidOperationException(NothingInserted);
        var request = new WaitForIndexRequest(database.Partition, Collection, TextField, minimum);
        _ = KeyLoadDatabase.Require(await database.Client.WaitForIndexAsync(request, token), nameof(BuildIndexAsync));
    }

    public async Task<IReadOnlyList<string>> SearchAsync(IReadOnlyList<string> terms, int limit, CancellationToken token)
    {
        var request = new SearchRequest(database.Partition, Collection, TextField, string.Join(Separator, terms), Limit: limit);
        var found = KeyLoadDatabase.Require(await database.Client.SearchAsync(request, token), nameof(SearchAsync));
        return [.. found.Select(item => item.Document.Reference.Id)];
    }

    public ValueTask DisposeAsync() => database.DisposeAsync();

    /// <summary>Keeps the commit token with the highest position acknowledged to any client.</summary>
    private void Observe(CommitToken token)
    {
        var current = Volatile.Read(ref latest);
        while (current is null || token.Position > current.Position)
        {
            var previous = Interlocked.CompareExchange(ref latest, token, current);
            if (ReferenceEquals(previous, current))
            {
                return;
            }
            current = previous;
        }
    }
}
