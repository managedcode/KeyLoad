using System.Globalization;
using KeyLoad.ComparisonHost.Deployment;
using Npgsql;

namespace KeyLoad.ComparisonHost.Relational;

/// <summary>
/// PostgreSQL through one pooled Npgsql data source on the published primary: positional-parameter SQL, with each load
/// batch inserted by one <c>INSERT ... SELECT FROM unnest(...)</c> statement over five typed array parameters. In the
/// three-node deployment the streaming standbys only replicate; every read and write goes to the primary.
/// </summary>
internal sealed class PostgresRelationalDriver(BenchmarkConnection connection) : IRelationalDriver
{
    // One fixed statement takes any row count; 1,000 rows per INSERT, like the other SQL drivers.
    private const int Rows = 1_000;
    private const int IdOrdinal = 0;
    private const int CustomerOrdinal = 1;
    private const int AmountOrdinal = 2;
    private const int StatusOrdinal = 3;
    private const int CreatedOrdinal = 4;
    private const int SingleRow = 1;
    private const string CreateTable = "CREATE TABLE " + OrderDataset.Table + " (id bigint PRIMARY KEY, customer integer NOT NULL, "
        + "amount numeric(12,2) NOT NULL, status varchar(16) NOT NULL, created timestamptz NOT NULL)";
    private const string InsertSql = "INSERT INTO " + OrderDataset.Table + " (id, customer, amount, status, created) SELECT * FROM "
        + "unnest($1::bigint[], $2::integer[], $3::numeric[], $4::varchar[], $5::timestamptz[])";
    private const string SelectSql = "SELECT id, customer, amount, status, created FROM " + OrderDataset.Table + " WHERE id = $1";
    private const string RangeSql = "SELECT id FROM " + OrderDataset.Table + " WHERE id BETWEEN $1 AND $2";
    private const string UpdateSql = "UPDATE " + OrderDataset.Table + " SET amount = $1 WHERE id = $2";
    private const string DeleteSql = "DELETE FROM " + OrderDataset.Table + " WHERE id = $1";
    private const string Missing = "No order row with id ";
    private const string WrongCount = "PostgreSQL inserted a different number of rows: ";
    private readonly NpgsqlDataSource source = NpgsqlDataSource.Create(connection.RequireConnectionString());

    public int BatchSize => Rows;

    public async Task PrepareAsync(CancellationToken token)
    {
        await PostgresQuorum.RequireAsync(source, connection.Nodes, token);
        await using var command = source.CreateCommand(CreateTable);
        _ = await command.ExecuteNonQueryAsync(token);
    }

    public async Task InsertAsync(IReadOnlyList<OrderRow> rows, CancellationToken token)
    {
        await using var command = source.CreateCommand(InsertSql);
        command.Parameters.Add(new NpgsqlParameter<long[]> { TypedValue = [.. rows.Select(row => row.Id)] });
        command.Parameters.Add(new NpgsqlParameter<int[]> { TypedValue = [.. rows.Select(row => row.Customer)] });
        command.Parameters.Add(new NpgsqlParameter<decimal[]> { TypedValue = [.. rows.Select(row => row.Amount)] });
        command.Parameters.Add(new NpgsqlParameter<string[]> { TypedValue = [.. rows.Select(row => row.Status)] });
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset[]> { TypedValue = [.. rows.Select(row => row.Created)] });
        var inserted = await command.ExecuteNonQueryAsync(token);
        if (inserted != rows.Count)
        {
            throw new InvalidDataException(WrongCount + inserted.ToString(CultureInfo.InvariantCulture));
        }
    }

    public async Task<OrderRow> SelectAsync(long id, CancellationToken token)
    {
        await using var command = source.CreateCommand(SelectSql);
        command.Parameters.Add(Number(id));
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token))
        {
            throw new KeyNotFoundException(Missing + id.ToString(CultureInfo.InvariantCulture));
        }
        return new(reader.GetInt64(IdOrdinal), reader.GetInt32(CustomerOrdinal), reader.GetDecimal(AmountOrdinal),
            reader.GetString(StatusOrdinal), new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(CreatedOrdinal), DateTimeKind.Utc)));
    }

    public async Task<IReadOnlyList<long>> SelectRangeAsync(long from, long to, CancellationToken token)
    {
        await using var command = source.CreateCommand(RangeSql);
        command.Parameters.Add(Number(from));
        command.Parameters.Add(Number(to));
        await using var reader = await command.ExecuteReaderAsync(token);
        var ids = new List<long>();
        while (await reader.ReadAsync(token))
        {
            ids.Add(reader.GetInt64(IdOrdinal));
        }
        return ids;
    }

    public async Task UpdateAmountAsync(long id, decimal amount, CancellationToken token)
    {
        await using var command = source.CreateCommand(UpdateSql);
        command.Parameters.Add(new NpgsqlParameter<decimal> { TypedValue = amount });
        command.Parameters.Add(Number(id));
        RequireOne(await command.ExecuteNonQueryAsync(token), id);
    }

    public async Task DeleteAsync(long id, CancellationToken token)
    {
        await using var command = source.CreateCommand(DeleteSql);
        command.Parameters.Add(Number(id));
        RequireOne(await command.ExecuteNonQueryAsync(token), id);
    }

    public ValueTask DisposeAsync() => source.DisposeAsync();

    private static NpgsqlParameter<long> Number(long value) => new() { TypedValue = value };

    private static void RequireOne(int affected, long id)
    {
        if (affected != SingleRow)
        {
            throw new KeyNotFoundException(Missing + id.ToString(CultureInfo.InvariantCulture));
        }
    }
}
