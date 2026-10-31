using KeyLoad.ComparisonHost.Deployment;
using KeyLoad.Query;

namespace KeyLoad.ComparisonHost.Documents;

/// <summary>
/// KeyLoad documents through the public SDK: a <see cref="ResourceKind.Collection"/> with a secondary index on
/// <c>/number</c>, <see cref="PutDocument"/>/<see cref="DeleteDocument"/> commits, document point reads and a typed AST
/// range query (<c>number &gt;= from AND number &lt;= to</c>) executed by the server.
/// </summary>
/// <remarks>
/// The range query grants no full-scan permission: the server answers it from an index or refuses it with its own error.
/// </remarks>
internal sealed class KeyLoadDocumentDriver(BenchmarkConnection connection) : IDocumentDriver
{
    private const string Collection = "documents";
    private const string IndexName = "number";
    private const string NumberPath = "/" + DocumentDataset.NumberProperty;
    private const string IdPath = "/@id";
    private const string IdAlias = "id";
    private const string AtLeast = ">=";
    private const string AtMost = "<=";
    private const string And = "AND";
    private const string ReadOperation = "read";
    private const string RangeOperation = "range query";
    private const string MissingDocument = "KeyLoad has no document ";
    private const string ReplacedMissing = "KeyLoad created instead of replacing the missing document ";
    private const long NewDocumentRevision = 0;
    private const long CreatedRevision = 1;
    private const int Batch = 256;
    private readonly KeyLoadDatabase database = new(connection);

    public int BatchSize => Batch;

    public Task PrepareAsync(CancellationToken token)
        => database.ConfigureAsync(database.Resource(Collection, ResourceKind.Collection) with
        {
            Indexes = [new IndexDefinition(IndexName, [NumberPath])]
        }, token);

    public Task InsertAsync(IReadOnlyList<DocumentRecord> documents, CancellationToken token)
        => database.CommitAsync([.. documents.Select(Create)], token);

    public async Task<string> ReadAsync(string id, CancellationToken token)
    {
        var read = KeyLoadDatabase.Require(await database.Client.GetAsync(database.Entity(Collection, id), token), ReadOperation);
        return read.Document?.Json ?? throw new KeyNotFoundException(MissingDocument + id);
    }

    /// <summary>One AST query the server plans; every continuation page goes to the node that signed its cursor.</summary>
    public async Task<IReadOnlyList<string>> FindRangeAsync(long from, long to, CancellationToken token)
    {
        var client = database.Client;
        var query = new SelectQuery(Collection, null, [new(IdPath, IdAlias)], Between(from, to), [], checked((int)(to - from + 1)));
        var ids = new List<string>();
        string? cursor = null;
        do
        {
            var page = KeyLoadDatabase.Require(await client.QueryAstAsync(new(database.Partition, query, Cursor: cursor), token),
                RangeOperation);
            ids.AddRange(page.Rows.Select(row => row.EntityId));
            cursor = page.Cursor;
        }
        while (cursor is not null);
        return ids;
    }

    public async Task ReplaceAsync(DocumentRecord document, CancellationToken token)
    {
        var receipt = await database.CommitAsync([new PutDocument(Collection, document.Id, document.Json, ExplicitReplacement: true)],
            token);
        if (receipt.Mutations is not [{ Revision: > CreatedRevision }])
        {
            throw new KeyNotFoundException(ReplacedMissing + document.Id);
        }
    }

    public Task DeleteAsync(string id, CancellationToken token)
        => database.CommitAsync([new DeleteDocument(Collection, id)], token);

    public ValueTask DisposeAsync() => database.DisposeAsync();

    private static Mutation Create(DocumentRecord document)
        => new PutDocument(Collection, document.Id, document.Json, NewDocumentRevision);

    private static Logical Between(long from, long to)
        => new(new Comparison(new FieldOperand(NumberPath), AtLeast, ValueOperand.Create(from)), And,
            new Comparison(new FieldOperand(NumberPath), AtMost, ValueOperand.Create(to)));
}
