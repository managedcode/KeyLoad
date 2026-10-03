using Npgsql;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Compares supported PostgreSQL operations through an isolated benchmark schema.</summary>
/// <param name="connectionString">The PostgreSQL connection string used to create the pooled data source.</param>
/// <param name="runId">Guid-formatted run identifier used to isolate the schema name.</param>
/// <param name="image">Database image reference recorded in the target profile.</param>
/// <param name="topology">The expected topology used by replication setup and copy observation.</param>
public sealed class PostgresTarget(string connectionString, string runId, string image,
    ComparisonTopology topology = ComparisonTopology.Standalone) : IComparisonTarget
{
    private readonly PostgresSchemaIdentity schemaIdentity = PostgresSchemaIdentity.FromRunId(runId);
    private readonly Guid ownerGuid = Guid.NewGuid();
    private string schema => schemaIdentity.Name;
    private NpgsqlDataSource source = null!;
    private bool schemaCommitAttempted;
    private int topK;
    private int graphDepth;

    /// <summary>Gets the observed PostgreSQL and topology profile.</summary>
    public TargetProfile Profile { get; private set; } = new("PostgreSQL + pgvector", "unverified", "single primary, no replicas",
        "fsync=on, synchronous_commit=on; local WAL flush", "READ COMMITTED on primary; streams emulate the narrow atomic one-event contract", "pooled prepared SQL/TCP", "database owner; no RLS/masking", image);

    /// <summary>Reports support for the target's document, vector, queue, graph, and stream scenarios.</summary>
    /// <param name="scenario">The comparison scenario to check.</param>
    /// <returns>Whether this target implements the scenario.</returns>
    public bool Supports(Scenario scenario) => scenario is Scenario.PointRead or Scenario.DocumentWrite or Scenario.DocumentUpdate or Scenario.DocumentDelete or Scenario.VectorExact
        or Scenario.QueueCycle or Scenario.GraphNeighbors or Scenario.GraphTraverse or Scenario.StreamAppend or Scenario.StreamRead;

    /// <summary>Creates the owned schema, verifies settings, seeds data, and observes replication copies.</summary>
    /// <param name="dataset">The deterministic corpus and dimensions, graph, and concurrency options.</param>
    /// <param name="cancellationToken">A token that cancels database setup and data operations.</param>
    /// <returns>A task that completes after schema creation, seeding, and copy observation.</returns>
    public async Task InitializeAsync(BenchmarkDataset dataset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        topK = dataset.Options.TopK;
        graphDepth = dataset.Options.GraphDepth;
        var settings = new NpgsqlConnectionStringBuilder(connectionString)
        {
            MaxAutoPrepare = 32,
            AutoPrepareMinUsages = 1,
            MaxPoolSize = Math.Max(10, dataset.Options.Concurrency),
            SearchPath = schema + ",public"
        };
        source = NpgsqlDataSource.Create(settings.ConnectionString);
        try
        {
            await using var connection = await source.OpenConnectionAsync(cancellationToken);
            await PostgresSchemaInitialization.InitializeAsync(connection, dataset, schemaIdentity, ownerGuid,
                topology, Profile, profile => Profile = profile, () => schemaCommitAttempted = true, cancellationToken);
        }
        catch (Exception)
        {
            await DisposeAfterInitializationFailureAsync();
            throw;
        }
    }

    private async Task DisposeAfterInitializationFailureAsync()
    {
        if (schemaCommitAttempted)
        {
            await DisposeAsync();
            return;
        }

        await DisposeSourceAsync();
    }

    /// <summary>Opens an independent pooled connection for one comparison session.</summary>
    /// <param name="cancellationToken">A token that cancels opening the session connection.</param>
    /// <returns>A session whose disposal returns its connection to the data source.</returns>
    public async Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken)
        => new PostgresComparisonSession(await source.OpenConnectionAsync(cancellationToken), topK, graphDepth);

    /// <summary>Drops only this target's marked schema and disposes its owned data source.</summary>
    /// <returns>A value task that completes after schema cleanup and data-source disposal.</returns>
    public async ValueTask DisposeAsync()
    {
        if (source is null)
        {
            return;
        }

        try
        {
            await using var connection = await source.OpenConnectionAsync();
            await PostgresSchemaLifecycle.DropIfOwnedAsync(connection, schemaIdentity, ownerGuid);
        }
        finally
        {
            await DisposeSourceAsync();
        }
    }

    private async ValueTask DisposeSourceAsync()
    {
        var ownedSource = source;
        await ownedSource.DisposeAsync();
        source = null!;
    }
}
