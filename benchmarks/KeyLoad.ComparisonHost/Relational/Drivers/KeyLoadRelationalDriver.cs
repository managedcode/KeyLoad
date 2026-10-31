using System.Globalization;
using System.Text.Json;
using KeyLoad.ComparisonHost.Deployment;

namespace KeyLoad.ComparisonHost.Relational;

/// <summary>
/// KeyLoad typed table through the SDK: <c>orders</c> is a collection resource with a closed
/// <see cref="RelationalSchema"/>, rows are written with one parameterized SQL <c>INSERT ... VALUES</c> per batch and read
/// with SQL <c>SELECT</c>. KeyLoad SQL has no UPDATE or DELETE, so the amount change is a native
/// <see cref="PatchDocument"/> at the row's current revision and the delete is a native <see cref="DeleteDocument"/>;
/// the schema validates both like the SQL insert.
/// </summary>
/// <remarks>
/// A typed primary key is a Text column equal to the entity id, so <c>id</c> holds the BIGINT as 19-digit zero-padded
/// decimal text: its byte order is numeric order, which keeps <c>BETWEEN</c> exact. The range select asks for no full scan,
/// as every other database answers it from its primary key; the server answers or refuses it with its own error.
/// </remarks>
internal sealed class KeyLoadRelationalDriver(BenchmarkConnection connection) : IRelationalDriver
{
    // KeyLoad SQL binds at most 256 named parameters per statement; 50 rows of five columns bind 250.
    private const int Rows = 50;
    private const int Columns = 5;
    private const string IdFormat = "D19";
    private const string IdColumn = "id";
    private const string CustomerColumn = "customer";
    private const string AmountColumn = "amount";
    private const string StatusColumn = "status";
    private const string CreatedColumn = "created";
    private const string AmountPath = "/amount";
    private const string IdParameter = "id";
    private const string FromParameter = "from";
    private const string ToParameter = "to";
    private const string ParameterPrefix = "p";
    private const string RowStart = "(@";
    private const string ValueSeparator = ",@";
    private const string RowEnd = ")";
    private const char RowSeparator = ',';
    private const string InsertHead = "INSERT INTO " + OrderDataset.Table + " (id, customer, amount, status, created) VALUES ";
    private const string SelectSql = "SELECT id, customer, amount, status, created FROM " + OrderDataset.Table + " WHERE id = @id";
    private const string RangeSql = "SELECT id FROM " + OrderDataset.Table + " WHERE id BETWEEN @from AND @to ORDER BY id";
    private const string Missing = "No order row with id ";
    private const string NoPage = "KeyLoad returned no query page for ";
    private const string NoStatus = "KeyLoad returned a row without status: ";
    private static readonly string[] Names = [.. Enumerable.Range(0, Rows * Columns)
        .Select(index => ParameterPrefix + index.ToString(CultureInfo.InvariantCulture))];
    private static readonly string[] RowValues = [.. Enumerable.Range(0, Rows).Select(RowText)];
    private static readonly string FullInsert = InsertText(Rows);
    private static readonly RelationalSchema Schema = new(IdColumn, [new(IdColumn, RelationalColumnType.Text),
        new(CustomerColumn, RelationalColumnType.WholeNumber), new(AmountColumn, RelationalColumnType.FixedPoint),
        new(StatusColumn, RelationalColumnType.Text), new(CreatedColumn, RelationalColumnType.UtcTimestamp)]);
    private readonly KeyLoadDatabase database = new(connection);

    public int BatchSize => Rows;

    public Task PrepareAsync(CancellationToken token)
        => database.ConfigureAsync(database.Resource(OrderDataset.Table, ResourceKind.Collection) with { RelationalSchema = Schema },
            token);

    public async Task InsertAsync(IReadOnlyList<OrderRow> rows, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfZero(rows.Count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rows.Count, Rows);
        var parameters = new Dictionary<string, JsonElement>(rows.Count * Columns, StringComparer.Ordinal);
        var name = 0;
        foreach (var row in rows)
        {
            parameters[Names[name++]] = JsonSerializer.SerializeToElement(Key(row.Id));
            parameters[Names[name++]] = JsonSerializer.SerializeToElement(row.Customer);
            parameters[Names[name++]] = JsonSerializer.SerializeToElement(row.Amount);
            parameters[Names[name++]] = JsonSerializer.SerializeToElement(row.Status);
            parameters[Names[name++]] = JsonSerializer.SerializeToElement(row.Created);
        }
        var sql = rows.Count == Rows ? FullInsert : InsertText(rows.Count);
        _ = KeyLoadDatabase.Require(await database.Client.ExecuteSqlAsync(
            new SqlOperationRequest(database.Partition, sql, parameters, CommandId: Guid.NewGuid()), token), nameof(InsertAsync));
    }

    public async Task<OrderRow> SelectAsync(long id, CancellationToken token)
    {
        var parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [IdParameter] = JsonSerializer.SerializeToElement(Key(id))
        };
        var page = await QueryAsync(SelectSql, parameters, false, null, token);
        return page.Rows.Length > 0 ? Read(page.Rows[0])
            : throw new KeyNotFoundException(Missing + id.ToString(CultureInfo.InvariantCulture));
    }

    public async Task<IReadOnlyList<long>> SelectRangeAsync(long from, long to, CancellationToken token)
    {
        var parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [FromParameter] = JsonSerializer.SerializeToElement(Key(from)),
            [ToParameter] = JsonSerializer.SerializeToElement(Key(to))
        };
        var page = await QueryAsync(RangeSql, parameters, false, null, token);
        List<long> ids = [.. page.Rows.Select(row => Id(row.EntityId))];
        while (page.Cursor is { } cursor)
        {
            page = await QueryAsync(RangeSql, parameters, false, cursor, token);
            ids.AddRange(page.Rows.Select(row => Id(row.EntityId)));
        }
        return ids;
    }

    public async Task UpdateAmountAsync(long id, decimal amount, CancellationToken token)
    {
        var key = Key(id);
        var read = KeyLoadDatabase.Require(await database.Client.GetAsync(database.Entity(OrderDataset.Table, key), token),
            nameof(UpdateAmountAsync));
        var current = read.Document ?? throw new KeyNotFoundException(Missing + key);
        _ = await database.CommitAsync([new PatchDocument(OrderDataset.Table, key,
            [new FieldPatch(AmountPath, PatchKind.Set, JsonSerializer.Serialize(amount))], current.Revision)], token);
    }

    public async Task DeleteAsync(long id, CancellationToken token)
    {
        _ = await database.CommitAsync([new DeleteDocument(OrderDataset.Table, Key(id))], token);
    }

    public ValueTask DisposeAsync() => database.DisposeAsync();

    private async Task<QueryPage> QueryAsync(string sql, Dictionary<string, JsonElement> parameters, bool fullScan,
        string? cursor, CancellationToken token)
    {
        var result = KeyLoadDatabase.Require(await database.Client.ExecuteSqlAsync(new SqlOperationRequest(database.Partition,
            sql, parameters, AllowFullScan: fullScan, Cursor: cursor), token), sql);
        return result.Deserialize<QueryPage>(JsonDefaults.Options) ?? throw new InvalidDataException(NoPage + sql);
    }

    private static OrderRow Read(QueryRow row)
    {
        using var json = JsonDocument.Parse(row.Json);
        var root = json.RootElement;
        return new(Id(row.EntityId), root.GetProperty(CustomerColumn).GetInt32(), root.GetProperty(AmountColumn).GetDecimal(),
            root.GetProperty(StatusColumn).GetString() ?? throw new InvalidDataException(NoStatus + row.EntityId),
            root.GetProperty(CreatedColumn).GetDateTimeOffset());
    }

    private static string Key(long id) => id.ToString(IdFormat, CultureInfo.InvariantCulture);

    private static long Id(string key) => long.Parse(key, NumberStyles.None, CultureInfo.InvariantCulture);

    private static string RowText(int row) => RowStart + string.Join(ValueSeparator, Names, row * Columns, Columns) + RowEnd;

    private static string InsertText(int count) => InsertHead + string.Join(RowSeparator, RowValues, 0, count);
}
