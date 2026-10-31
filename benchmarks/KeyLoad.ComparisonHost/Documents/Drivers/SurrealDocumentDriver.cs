using System.Globalization;
using System.Text.Json.Nodes;
using KeyLoad.ComparisonHost.Deployment;

namespace KeyLoad.ComparisonHost.Documents;

/// <summary>
/// SurrealDB documents through SurrealQL over HTTP (<see cref="SurrealSql"/>): a schemaless <c>documents</c> table whose
/// record key is the document id and whose fields are the JSON body, with an index on <c>number</c>. Inserts are one
/// <c>INSERT</c> of the batch, reads <c>SELECT *</c> of the record, the range query is
/// <c>SELECT id ... WITH INDEX documents_number WHERE number &gt;= from AND number &lt;= to</c>, replacements are
/// <c>UPDATE ... CONTENT</c> and deletes <c>DELETE ... RETURN BEFORE</c>.
/// </summary>
internal sealed class SurrealDocumentDriver(BenchmarkConnection connection) : IDocumentDriver
{
    private const string Table = "documents";
    private const string NumberIndex = Table + "_" + DocumentDataset.NumberProperty;
    private const string Define = "DEFINE TABLE " + Table + " SCHEMALESS; DEFINE INDEX " + NumberIndex + " ON TABLE " + Table
        + " FIELDS " + DocumentDataset.NumberProperty + ";";
    private const string Insert = "INSERT INTO " + Table + " [";
    private const string InsertEnd = "] RETURN NONE;";
    private const string SelectRecord = "SELECT * FROM ";
    private const string RangeFrom = "SELECT id FROM " + Table + " WITH INDEX " + NumberIndex + " WHERE "
        + DocumentDataset.NumberProperty + " >= ";
    private const string RangeTo = " AND " + DocumentDataset.NumberProperty + " <= ";
    private const string Update = "UPDATE ";
    private const string Content = " CONTENT ";
    private const string UpdateEnd = " RETURN VALUE id;";
    private const string Delete = "DELETE ";
    private const string DeleteEnd = " RETURN BEFORE;";
    private const string StatementEnd = ";";
    private const string IdProperty = "id";
    private const string RecordStart = "{ id: ";
    private const string Separator = ", ";
    private const string EmptyObject = "{}";
    private const string NotAnObject = "The document body is not a JSON object: ";
    private const string MissingDocument = "SurrealDB has no document ";
    private const int Batch = 256;
    private const int FirstStatement = 0;
    private readonly SurrealSql sql = new(connection);

    public int BatchSize => Batch;

    public Task PrepareAsync(CancellationToken token) => sql.ExecuteAsync(Define, token);

    public Task InsertAsync(IReadOnlyList<DocumentRecord> documents, CancellationToken token)
        => sql.ExecuteAsync(Insert + string.Join(Separator, documents.Select(Created)) + InsertEnd, token);

    public async Task<string> ReadAsync(string id, CancellationToken token)
    {
        using var response = await sql.QueryAsync(SelectRecord + SurrealSql.Record(Table, id) + StatementEnd, token);
        var rows = SurrealSql.Result(response, FirstStatement);
        if (rows.GetArrayLength() == 0)
        {
            throw new KeyNotFoundException(MissingDocument + id);
        }
        return rows[0].GetRawText();
    }

    public async Task<IReadOnlyList<string>> FindRangeAsync(long from, long to, CancellationToken token)
    {
        var statement = RangeFrom + from.ToString(CultureInfo.InvariantCulture) + RangeTo + to.ToString(CultureInfo.InvariantCulture)
            + StatementEnd;
        using var response = await sql.QueryAsync(statement, token);
        return [.. SurrealSql.Result(response, FirstStatement).EnumerateArray()
            .Select(row => SurrealSql.Key(Table, row.GetProperty(IdProperty)))];
    }

    public Task ReplaceAsync(DocumentRecord document, CancellationToken token)
        => ChangeAsync(Update + SurrealSql.Record(Table, document.Id) + Content + Fields(document.Json) + UpdateEnd, document.Id, token);

    public Task DeleteAsync(string id, CancellationToken token)
        => ChangeAsync(Delete + SurrealSql.Record(Table, id) + DeleteEnd, id, token);

    public ValueTask DisposeAsync()
    {
        sql.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>Runs a statement on one existing record; SurrealDB returns no record when the id is missing.</summary>
    private async Task ChangeAsync(string statement, string id, CancellationToken token)
    {
        using var response = await sql.QueryAsync(statement, token);
        if (SurrealSql.Result(response, FirstStatement).GetArrayLength() == 0)
        {
            throw new KeyNotFoundException(MissingDocument + id);
        }
    }

    /// <summary>The record literal: the record id followed by every body field other than the body's own <c>id</c>.</summary>
    private static string Created(DocumentRecord document)
    {
        var fields = Fields(document.Json);
        return RecordStart + SurrealSql.Record(Table, document.Id)
            + (fields.Length > EmptyObject.Length ? Separator + fields[1..] : fields[1..]);
    }

    /// <summary>The body as a SurrealQL object without its <c>id</c>, which SurrealDB keeps as the record key.</summary>
    private static string Fields(string json)
    {
        var body = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException(NotAnObject + json);
        _ = body.Remove(IdProperty);
        return body.ToJsonString();
    }
}
