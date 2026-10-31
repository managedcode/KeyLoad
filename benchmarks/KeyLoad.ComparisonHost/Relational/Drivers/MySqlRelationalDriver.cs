using System.Buffers;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using KeyLoad.ComparisonHost.Deployment;
using MySqlConnector;

namespace KeyLoad.ComparisonHost.Relational;

/// <summary>
/// MySQL/InnoDB through pooled MySqlConnector connections: parameterized SQL in the driver's own <c>benchmark</c>
/// database, with each load batch inserted by one <c>INSERT ... SELECT FROM JSON_TABLE(@rows, ...)</c> statement.
/// <see cref="PrepareAsync"/> retries opening a connection until the server accepts it.
/// </summary>
internal sealed class MySqlRelationalDriver(BenchmarkConnection connection) : IRelationalDriver
{
    // One fixed statement takes any row count; 1,000 rows (about 100 KB of JSON) stay far below max_allowed_packet.
    private const int Rows = 1_000;
    private const int IdOrdinal = 0;
    private const int CustomerOrdinal = 1;
    private const int AmountOrdinal = 2;
    private const int StatusOrdinal = 3;
    private const int CreatedOrdinal = 4;
    private const int SingleRow = 1;
    private const string DatabaseName = "benchmark";
    private const string CreateDatabase = "CREATE DATABASE " + DatabaseName;
    private const string CreateTable = "CREATE TABLE " + OrderDataset.Table + " (id BIGINT NOT NULL PRIMARY KEY, customer INT NOT NULL, "
        + "amount DECIMAL(12,2) NOT NULL, status VARCHAR(16) NOT NULL, created DATETIME NOT NULL) ENGINE=InnoDB";
    private const string InsertSql = "INSERT INTO " + OrderDataset.Table + " (id, customer, amount, status, created) "
        + "SELECT id, customer, amount, status, created FROM JSON_TABLE(@rows, '$[*]' COLUMNS (id BIGINT PATH '$.id', "
        + "customer INT PATH '$.customer', amount DECIMAL(12,2) PATH '$.amount', status VARCHAR(16) PATH '$.status', "
        + "created DATETIME PATH '$.created')) AS incoming";
    private const string SelectSql = "SELECT id, customer, amount, status, created FROM " + OrderDataset.Table + " WHERE id = @id";
    private const string RangeSql = "SELECT id FROM " + OrderDataset.Table + " WHERE id BETWEEN @from AND @to";
    private const string UpdateSql = "UPDATE " + OrderDataset.Table + " SET amount = @amount WHERE id = @id";
    private const string DeleteSql = "DELETE FROM " + OrderDataset.Table + " WHERE id = @id";
    private const string RowsParameter = "@rows";
    private const string IdParameter = "@id";
    private const string FromParameter = "@from";
    private const string ToParameter = "@to";
    private const string AmountParameter = "@amount";
    private const string IdProperty = "id";
    private const string CustomerProperty = "customer";
    private const string AmountProperty = "amount";
    private const string StatusProperty = "status";
    private const string CreatedProperty = "created";
    private const string CreatedFormat = "yyyy-MM-dd HH:mm:ss";
    private const string Missing = "No order row with id ";
    private const string WrongCount = "MySQL inserted a different number of rows: ";
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReadinessLimit = TimeSpan.FromMinutes(5);
    private readonly string server = connection.RequireConnectionString();
    private readonly string database = new MySqlConnectionStringBuilder(connection.RequireConnectionString()) { Database = DatabaseName }.ConnectionString;

    public int BatchSize => Rows;

    public async Task PrepareAsync(CancellationToken token)
    {
        await WaitForServerAsync(token);
        await using (var root = new MySqlConnection(server))
        {
            await root.OpenAsync(token);
            await using var create = new MySqlCommand(CreateDatabase, root);
            _ = await create.ExecuteNonQueryAsync(token);
        }
        await using var session = new MySqlConnection(database);
        await session.OpenAsync(token);
        await using var command = new MySqlCommand(CreateTable, session);
        _ = await command.ExecuteNonQueryAsync(token);
    }

    public async Task InsertAsync(IReadOnlyList<OrderRow> rows, CancellationToken token)
    {
        await using var session = new MySqlConnection(database);
        await session.OpenAsync(token);
        await using var command = new MySqlCommand(InsertSql, session);
        _ = command.Parameters.AddWithValue(RowsParameter, Json(rows));
        var inserted = await command.ExecuteNonQueryAsync(token);
        if (inserted != rows.Count)
        {
            throw new InvalidDataException(WrongCount + inserted.ToString(CultureInfo.InvariantCulture));
        }
    }

    public async Task<OrderRow> SelectAsync(long id, CancellationToken token)
    {
        await using var session = new MySqlConnection(database);
        await session.OpenAsync(token);
        await using var command = new MySqlCommand(SelectSql, session);
        _ = command.Parameters.AddWithValue(IdParameter, id);
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
        await using var session = new MySqlConnection(database);
        await session.OpenAsync(token);
        await using var command = new MySqlCommand(RangeSql, session);
        _ = command.Parameters.AddWithValue(FromParameter, from);
        _ = command.Parameters.AddWithValue(ToParameter, to);
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
        await using var session = new MySqlConnection(database);
        await session.OpenAsync(token);
        await using var command = new MySqlCommand(UpdateSql, session);
        _ = command.Parameters.AddWithValue(AmountParameter, amount);
        _ = command.Parameters.AddWithValue(IdParameter, id);
        RequireOne(await command.ExecuteNonQueryAsync(token), id);
    }

    public async Task DeleteAsync(long id, CancellationToken token)
    {
        await using var session = new MySqlConnection(database);
        await session.OpenAsync(token);
        await using var command = new MySqlCommand(DeleteSql, session);
        _ = command.Parameters.AddWithValue(IdParameter, id);
        RequireOne(await command.ExecuteNonQueryAsync(token), id);
    }

    public async ValueTask DisposeAsync() => await MySqlConnection.ClearAllPoolsAsync();

    /// <summary>The deployment has no readiness check: retries every 2 s and rethrows the last error after 5 minutes.</summary>
    private async Task WaitForServerAsync(CancellationToken token)
    {
        var clock = TimeProvider.System;
        var started = clock.GetTimestamp();
        while (true)
        {
            try
            {
                await using var probe = new MySqlConnection(server);
                await probe.OpenAsync(token);
                return;
            }
            catch (DbException) when (clock.GetElapsedTime(started) < ReadinessLimit)
            {
                await Task.Delay(RetryInterval, clock, token);
            }
        }
    }

    // Amounts are JSON strings so JSON_TABLE converts the exact decimal text, not a parsed double.
    private static string Json(IReadOnlyList<OrderRow> rows)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var row in rows)
            {
                writer.WriteStartObject();
                writer.WriteNumber(IdProperty, row.Id);
                writer.WriteNumber(CustomerProperty, row.Customer);
                writer.WriteString(AmountProperty, row.Amount.ToString(CultureInfo.InvariantCulture));
                writer.WriteString(StatusProperty, row.Status);
                writer.WriteString(CreatedProperty, row.Created.UtcDateTime.ToString(CreatedFormat, CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void RequireOne(int affected, long id)
    {
        if (affected != SingleRow)
        {
            throw new KeyNotFoundException(Missing + id.ToString(CultureInfo.InvariantCulture));
        }
    }
}
