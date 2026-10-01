using System.Diagnostics;
using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace KeyLoad.Comparisons.Targets;

public sealed class PostgresTarget(string connectionString, string runId, string image) : IComparisonTarget
{
    private readonly string schema = "bench_" + Guid.Parse(runId).ToString("N");
    private NpgsqlDataSource source = null!;
    private int topK;
    private int graphDepth;
    public TargetProfile Profile { get; private set; } = new("PostgreSQL + pgvector", "unverified", "single primary, no replicas",
        "fsync=on, synchronous_commit=on; local WAL flush", "READ COMMITTED on primary", "pooled prepared SQL/TCP", "database owner; no RLS/masking", image);
    public bool Supports(Scenario scenario) => true;
    public async Task InitializeAsync(BenchmarkDataset dataset, CancellationToken cancellationToken)
    {
        topK = dataset.Options.TopK;
        graphDepth = dataset.Options.GraphDepth;
        var settings = new NpgsqlConnectionStringBuilder(connectionString) { MaxAutoPrepare = 32, AutoPrepareMinUsages = 1,
            MaxPoolSize = Math.Max(10, dataset.Options.Concurrency), SearchPath = schema + ",public" };
        source = NpgsqlDataSource.Create(settings.ConnectionString);
        await using var connection = await source.OpenConnectionAsync(cancellationToken);
        await using (var ddl = connection.CreateCommand())
        {
            ddl.CommandText = $"""
                CREATE EXTENSION IF NOT EXISTS vector;
                CREATE SCHEMA {schema};
                CREATE TABLE {schema}.documents(id text COLLATE "C" PRIMARY KEY, body jsonb NOT NULL, embedding vector({dataset.Options.Dimensions}));
                CREATE TABLE {schema}.queue(id text COLLATE "C" PRIMARY KEY, body jsonb NOT NULL, state text NOT NULL DEFAULT 'ready',
                    lease_owner uuid, lease_until timestamptz, attempts integer NOT NULL DEFAULT 0);
                CREATE INDEX queue_ready ON {schema}.queue(state, id);
                CREATE TABLE {schema}.edges(source text COLLATE "C", target text COLLATE "C", PRIMARY KEY(source,target));
                """;
            await ddl.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var verify = connection.CreateCommand())
        {
            verify.CommandText = "SELECT current_setting('server_version'), current_setting('fsync'), current_setting('synchronous_commit'), (SELECT extversion FROM pg_extension WHERE extname = 'vector'), (SELECT ssl FROM pg_stat_ssl WHERE pid=pg_backend_pid())";
            await using var reader = await verify.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            if (reader.GetString(1) != "on" || reader.GetString(2) != "on") throw new ComparisonFailure("PostgresFlushRequired");
            Profile = Profile with { Version = reader.GetString(0) + "; pgvector " + reader.GetString(3),
                Transport = reader.GetBoolean(4) ? "pooled prepared SQL/TLS" : "pooled prepared SQL/TCP" };
        }
        foreach (var document in dataset.Documents)
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO documents(id,body,embedding) VALUES ($1,$2,$3::vector)";
            insert.Parameters.AddWithValue(document.Id); insert.Parameters.AddWithValue(NpgsqlDbType.Jsonb, document.Json);
            insert.Parameters.AddWithValue(VectorLiteral(document.Vector));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var copy = await connection.BeginBinaryImportAsync("COPY edges(source,target) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var edge in dataset.Edges)
            {
                await copy.StartRowAsync(cancellationToken);
                await copy.WriteAsync(edge.From, NpgsqlDbType.Text, cancellationToken);
                await copy.WriteAsync(edge.To, NpgsqlDbType.Text, cancellationToken);
            }
            await copy.CompleteAsync(cancellationToken);
        }
        await using var analyze = connection.CreateCommand(); analyze.CommandText = "ANALYZE documents; ANALYZE queue; ANALYZE edges";
        await analyze.ExecuteNonQueryAsync(cancellationToken);
    }
    private static string VectorLiteral(float[] vector) => "[" + string.Join(",", vector.Select(value => value.ToString("R", CultureInfo.InvariantCulture))) + "]";
    public async Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken)
        => new Session(await source.OpenConnectionAsync(cancellationToken), topK, graphDepth);
    public async ValueTask DisposeAsync()
    {
        if (source is null) return;
        try
        {
            await using var connection = await source.OpenConnectionAsync();
            await using var drop = connection.CreateCommand(); drop.CommandText = $"DROP SCHEMA IF EXISTS {schema} CASCADE";
            await drop.ExecuteNonQueryAsync();
        }
        finally { await source.DisposeAsync(); }
    }
    private sealed class Session(NpgsqlConnection connection, int topK, int graphDepth) : IComparisonSession
    {
        public async Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT body::text FROM documents WHERE id=$1"; command.Parameters.AddWithValue(document.Id);
            var json = await command.ExecuteScalarAsync(cancellationToken) as string;
            return json is null ? null : new(document.Id, json);
        }
        public async Task<OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken cancellationToken)
        {
            switch (scenario)
            {
                case Scenario.PointRead: return new(Document: await ReadAsync(document, cancellationToken));
                case Scenario.DocumentWrite:
                    await using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "INSERT INTO documents(id,body) VALUES ($1,$2)";
                        command.Parameters.AddWithValue(document.Id); command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, document.Json);
                        await command.ExecuteNonQueryAsync(cancellationToken);
                    }
                    return new();
                case Scenario.VectorExact:
                    await using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT id,body::text FROM documents WHERE embedding IS NOT NULL ORDER BY embedding <=> $1::vector, id LIMIT $2";
                        command.Parameters.AddWithValue(VectorLiteral(document.Vector)); command.Parameters.AddWithValue(topK);
                        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                        var found = new List<FoundDocument>();
                        while (await reader.ReadAsync(cancellationToken)) found.Add(new(reader.GetString(0), reader.GetString(1)));
                        return new(Neighbors: found.ToArray());
                    }
                case Scenario.QueueCycle:
                    var begin = Stopwatch.GetTimestamp();
                    await using (var enqueue = connection.CreateCommand())
                    {
                        enqueue.CommandText = "INSERT INTO queue(id,body) VALUES ($1,$2)";
                        enqueue.Parameters.AddWithValue(document.Id); enqueue.Parameters.AddWithValue(NpgsqlDbType.Jsonb, document.Json);
                        await enqueue.ExecuteNonQueryAsync(cancellationToken);
                    }
                    var enqueued = Stopwatch.GetTimestamp();
                    var lease = Guid.NewGuid();
                    FoundDocument? message = null;
                    while (message is null)
                    {
                        await using var claim = connection.CreateCommand();
                        claim.CommandText = """
                            WITH candidate AS (SELECT id FROM queue WHERE state='ready' OR (state='leased' AND lease_until < now())
                                ORDER BY id FOR UPDATE SKIP LOCKED LIMIT 1)
                            UPDATE queue q SET state='leased',lease_owner=$1,lease_until=now()+interval '30 seconds',attempts=attempts+1
                            FROM candidate c WHERE q.id=c.id RETURNING q.id,q.body::text
                            """;
                        claim.Parameters.AddWithValue(lease);
                        await using (var reader = await claim.ExecuteReaderAsync(cancellationToken))
                            if (await reader.ReadAsync(cancellationToken)) message = new(reader.GetString(0), reader.GetString(1));
                        if (message is null) await Task.Delay(1, cancellationToken);
                    }
                    var received = Stopwatch.GetTimestamp();
                    await using (var ack = connection.CreateCommand())
                    {
                        ack.CommandText = "UPDATE queue SET state='acked',lease_until=NULL WHERE id=$1 AND lease_owner=$2 AND state='leased' AND lease_until>now()";
                        ack.Parameters.AddWithValue(message.Id); ack.Parameters.AddWithValue(lease);
                        if (await ack.ExecuteNonQueryAsync(cancellationToken) != 1) throw new ComparisonFailure("PostgresLeaseLost");
                    }
                    return new(Message: message, Queue: new(Stopwatch.GetElapsedTime(begin, enqueued).TotalMilliseconds,
                        Stopwatch.GetElapsedTime(enqueued, received).TotalMilliseconds, Stopwatch.GetElapsedTime(received).TotalMilliseconds));
                case Scenario.GraphNeighbors:
                case Scenario.GraphTraverse:
                    await using (var command = connection.CreateCommand())
                    {
                        command.CommandText = scenario == Scenario.GraphNeighbors ? "SELECT target FROM edges WHERE source=$1 ORDER BY target" : """
                            WITH RECURSIVE reachable(id,depth) AS (
                                SELECT $1::text COLLATE "C",0
                                UNION SELECT e.target,r.depth+1 FROM reachable r JOIN edges e ON e.source=r.id WHERE r.depth<$2)
                            SELECT DISTINCT id FROM reachable WHERE id<>$1 ORDER BY id
                            """;
                        command.Parameters.AddWithValue(document.Id);
                        if (scenario == Scenario.GraphTraverse) command.Parameters.AddWithValue(graphDepth);
                        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                        var vertices = new List<string>();
                        while (await reader.ReadAsync(cancellationToken)) vertices.Add(reader.GetString(0));
                        return new(Vertices: vertices.ToArray());
                    }
                default: throw new NotSupportedException();
            }
        }
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
