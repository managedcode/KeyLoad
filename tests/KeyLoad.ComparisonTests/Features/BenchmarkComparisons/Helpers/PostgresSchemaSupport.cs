using System.Buffers.Binary;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using Npgsql;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class PostgresSchemaSupport
{
    private const string SchemaNamePrefix = "bench_";
    internal const string DocumentsTable = "documents";
    internal const string QueueTable = "queue";
    internal const string EdgesTable = "edges";
    internal const string EventsTable = "events";
    internal const string QueueReadyIndex = "queue_ready";
    internal const string IdColumn = "id";
    internal const string BodyColumn = "body";
    internal const string EmbeddingColumn = "embedding";
    internal const string StateColumn = "state";
    internal const string AttemptsColumn = "attempts";
    internal const string LeaseOwnerColumn = "lease_owner";
    internal const string LeaseUntilColumn = "lease_until";
    internal const string RevisionColumn = "revision";
    internal const string EventIdColumn = "event_id";
    internal const string StreamIdColumn = "stream_id";
    internal const string SourceColumn = "source";
    internal const string TargetColumn = "target";
    internal const string ExpectedKeyCollation = "C";
    private const string ColumnSql = "SELECT a.attname, format_type(a.atttypid,a.atttypmod), a.attnotnull, pg_get_expr(d.adbin,d.adrelid) FROM pg_catalog.pg_attribute a JOIN pg_catalog.pg_class c ON c.oid=a.attrelid JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace LEFT JOIN pg_catalog.pg_attrdef d ON d.adrelid=a.attrelid AND d.adnum=a.attnum WHERE n.nspname=$1 AND c.relname=$2 AND a.attnum>0 AND NOT a.attisdropped ORDER BY a.attnum";
    private const string EventConstraintSql = "SELECT pg_catalog.pg_get_constraintdef(c.oid) FROM pg_catalog.pg_constraint c JOIN pg_catalog.pg_class t ON t.oid=c.conrelid JOIN pg_catalog.pg_namespace n ON n.oid=t.relnamespace WHERE n.nspname=$1 AND t.relname='events' AND c.contype='c'";
    private const string PrimaryKeySql = "SELECT a.attname FROM pg_catalog.pg_index i CROSS JOIN LATERAL unnest(i.indkey) WITH ORDINALITY AS k(attnum,position) JOIN pg_catalog.pg_attribute a ON a.attrelid=i.indrelid AND a.attnum=k.attnum WHERE i.indrelid=pg_catalog.format('%I.%I',$1,$2)::regclass AND i.indisprimary ORDER BY k.position";
    private const string IndexDefinitionSql = "SELECT indexdef FROM pg_catalog.pg_indexes WHERE schemaname=$1 AND indexname=$2";
    private const string CollationSql = "SELECT co.collname FROM pg_catalog.pg_attribute a JOIN pg_catalog.pg_class c ON c.oid=a.attrelid JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace JOIN pg_catalog.pg_collation co ON co.oid=a.attcollation WHERE n.nspname=$1 AND c.relname=$2 AND a.attname=$3";
    private const string HasVectorExtensionSql = "SELECT EXISTS(SELECT 1 FROM pg_catalog.pg_extension WHERE extname='vector')";
    private const string SchemaExistsSql = "SELECT EXISTS(SELECT 1 FROM pg_catalog.pg_namespace WHERE nspname=$1)";

    internal static ComparisonOptions Options(int dimensions) => new()
    {
        Documents = 4,
        Operations = 2,
        Warmup = 0,
        Repetitions = 1,
        Concurrency = 1,
        PayloadBytes = 128,
        Dimensions = dimensions,
        TopK = 2,
        GraphVertices = 4,
        GraphFanOut = 1,
        GraphDepth = 2
    };

    internal static string Schema(string runId) => SchemaNamePrefix + Guid.Parse(runId).ToString("N");

    internal static async Task<NpgsqlConnection> OpenAsync(string connectionString, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch (Exception)
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    internal static Task DisposeTargetAsync(PostgresTarget target)
        => target.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));

    internal static async Task<Dictionary<string, (string Type, bool NotNull, string? Default)>> ColumnsAsync(
        NpgsqlConnection connection, string schema, string table, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(ColumnSql, connection);
        command.Parameters.AddWithValue(schema);
        command.Parameters.AddWithValue(table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new Dictionary<string, (string, bool, string?)>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(0), (reader.GetString(1), reader.GetBoolean(2), await reader.IsDBNullAsync(3, cancellationToken) ? null : reader.GetString(3)));
        }
        return columns;
    }

    internal static async Task<string?> IndexDefinitionAsync(NpgsqlConnection connection, string schema, string index,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(IndexDefinitionSql, connection);
        command.Parameters.AddWithValue(schema);
        command.Parameters.AddWithValue(index);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    internal static async Task<string[]> PrimaryKeyAsync(NpgsqlConnection connection, string schema, string table,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(PrimaryKeySql, connection);
        command.Parameters.AddWithValue(schema);
        command.Parameters.AddWithValue(table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(0));
        }
        return columns.ToArray();
    }

    internal static async Task<string?> CollationAsync(NpgsqlConnection connection, string schema, string table,
        string column, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(CollationSql, connection);
        command.Parameters.AddWithValue(schema);
        command.Parameters.AddWithValue(table);
        command.Parameters.AddWithValue(column);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    internal static async Task<bool> EventRevisionConstraintExistsAsync(NpgsqlConnection connection, string schema,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(EventConstraintSql, connection);
        command.Parameters.AddWithValue(schema);
        return (await command.ExecuteScalarAsync(cancellationToken) as string)?.Contains("revision = 1", StringComparison.Ordinal) == true;
    }

    internal static async Task<bool> VectorExtensionExistsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(HasVectorExtensionSql, connection);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    internal static async Task<bool> SchemaExistsAsync(string connectionString, string schema, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(connectionString, cancellationToken);
        await using var command = new NpgsqlCommand(SchemaExistsSql, connection);
        command.Parameters.AddWithValue(schema);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    internal static long LockKey(Guid runId)
    {
        Span<byte> bytes = stackalloc byte[16];
        runId.TryWriteBytes(bytes, bigEndian: true, out _);
        return BinaryPrimitives.ReadInt64BigEndian(bytes);
    }

}
