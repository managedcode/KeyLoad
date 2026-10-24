using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Creates one batch of new documents in one bulk operation with each database's own API and limits.</summary>
internal sealed record RelationalDocumentBatch(string Name)
{
    // Microsoft keeps bulk-copy batches below 1,000 rows without the TABLOCK hint, which concurrent clients cannot share:
    // https://learn.microsoft.com/sql/relational-databases/native-client-odbc-bulk-copy-operations/managing-bulk-copy-batch-sizes
    private const int SqlServerRows = 999;
    // MySQL recommends multi-row inserts but names no row count; 10,000 rows (~10 MB) stay far below the 64 MB max_allowed_packet:
    // https://dev.mysql.com/doc/refman/8.4/en/insert-optimization.html
    private const int MySqlRows = 10_000;
    // TimescaleDB documents multi-row INSERTs of thousands of rows; its own loader inserts 5,000 rows per batch:
    // https://github.com/timescale/timescaledb-parallel-copy
    private const int TimescaleRows = 5_000;
    private const int NoRecords = 0;
    private const string Table = "documents", IdColumn = "id", BodyColumn = "body", NumberColumn = "number";
    private const string JsonInsert = "INSERT INTO documents(id,body,number) SELECT id,body,number FROM JSON_TABLE(@rows,'$[*]' COLUMNS(id text PATH '$.id',body text PATH '$.body',number int PATH '$.number')) AS incoming";
    private const string ArrayInsert = "INSERT INTO documents(id,body,number,at) SELECT id,body,number,TIMESTAMPTZ '2026-01-01' + number * INTERVAL '1 microsecond' FROM unnest(@ids::text[],@bodies::text[],@numbers::integer[]) AS incoming(id,body,number)";
    private const string RowsParameter = "@rows", IdsParameter = "@ids", BodiesParameter = "@bodies", NumbersParameter = "@numbers";
    private const string ProviderRejected = "RelationalBulkCreateProviderRejected";

    internal int Records => Name == BenchmarkAdditionalTargets.SqlServer ? SqlServerRows
        : Name == BenchmarkAdditionalTargets.MySql ? MySqlRows : TimescaleRows;

    // Returns the rows the database acknowledged for the batch; the caller requires exactly one per document.
    internal async Task<long> CreateAsync(DbConnection connection, IReadOnlyList<BenchmarkDocument> documents,
        TimeSpan timeout, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == NoRecords || documents.Count > Records)
        {
            throw new ArgumentOutOfRangeException(nameof(documents));
        }
        var seconds = checked((int)Math.Ceiling(timeout.TotalSeconds));
        if (Name == BenchmarkAdditionalTargets.SqlServer)
        {
            return await CopyAsync(connection, documents, seconds, token).ConfigureAwait(false);
        }
        await using var command = connection.CreateCommand();
        command.CommandTimeout = seconds;
        if (Name == BenchmarkAdditionalTargets.MySql)
        {
            BindJson(command, documents);
        }
        else
        {
            BindArrays(command, documents);
        }
        return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    // SqlBulkCopy always enforces the primary key and sends the request as one batch under the connection's autocommit.
    private static async Task<long> CopyAsync(DbConnection connection, IReadOnlyList<BenchmarkDocument> documents,
        int seconds, CancellationToken token)
    {
        if (connection is not SqlConnection server)
        {
            throw new InvalidOperationException(ProviderRejected);
        }
        using var rows = new DataTable();
        rows.Columns.Add(IdColumn, typeof(string));
        rows.Columns.Add(BodyColumn, typeof(string));
        rows.Columns.Add(NumberColumn, typeof(int));
        foreach (var document in documents)
        {
            rows.Rows.Add(document.Id, document.Json, document.Number);
        }
        using var copy = new SqlBulkCopy(server);
        copy.DestinationTableName = Table;
        copy.BulkCopyTimeout = seconds;
        copy.ColumnMappings.Add(IdColumn, IdColumn);
        copy.ColumnMappings.Add(BodyColumn, BodyColumn);
        copy.ColumnMappings.Add(NumberColumn, NumberColumn);
        await copy.WriteToServerAsync(rows, token).ConfigureAwait(false);
        return copy.RowsCopied64;
    }

    // One JSON array keeps a single multi-row INSERT statement constant for every batch size.
    private static void BindJson(DbCommand command, IReadOnlyList<BenchmarkDocument> documents)
    {
        command.CommandText = JsonInsert;
        Add(command, RowsParameter, JsonSerializer.Serialize(
            documents.Select(document => new { id = document.Id, body = document.Json, number = document.Number })));
    }

    // Three typed arrays keep one statement within PostgreSQL's parameter limit and derive the hypertable time like the single create.
    private static void BindArrays(DbCommand command, IReadOnlyList<BenchmarkDocument> documents)
    {
        command.CommandText = ArrayInsert;
        Add(command, IdsParameter, documents.Select(document => document.Id).ToArray());
        Add(command, BodiesParameter, documents.Select(document => document.Json).ToArray());
        Add(command, NumbersParameter, documents.Select(document => document.Number).ToArray());
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
