using Microsoft.Extensions.Options;
using Npgsql;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Compares supported PostgreSQL operations through an isolated benchmark schema.</summary>
/// <param name="connectionString">The PostgreSQL connection string used to create the pooled data source.</param>
/// <param name="runId">Guid-formatted run identifier used to isolate the schema name.</param>
/// <param name="image">Database image reference recorded in the target profile.</param>
/// <param name="executionOptions">Centrally validated native connection capacities.</param>
/// <param name="lifecycleOptions">Centrally validated native lifecycle policy.</param>
/// <param name="topology">The expected topology used by replication setup and copy observation.</param>
public sealed class PostgresTarget(string connectionString, string runId, string image,
    IOptions<NativeComparisonExecutionOptions> executionOptions, IOptions<ComparisonLifecycleOptions> lifecycleOptions,
    ComparisonTopology topology = ComparisonTopology.Standalone) : IComparisonTarget
{
    private const string PostgreSQLPgvectorToken = "PostgreSQL + pgvector";
    private const string UnverifiedToken = "unverified";
    private const string SinglePrimaryNoReplicasToken = "single primary, no replicas";
    private const string FsyncOnSynchronousCommitOnLocalWALFlushToken = "fsync=on, synchronous_commit=on; local WAL flush";
    private const string READCOMMITTEDOnPrimaryStreamsEmulateTheNarrowAtomicOneEventContractContractText = "READ COMMITTED on primary; streams emulate the narrow atomic one-event contract";
    private const string PooledPreparedSQLTCPToken = "pooled prepared SQL/TCP";
    private const string DatabaseOwnerNoRLSMaskingToken = "database owner; no RLS/masking";

    private readonly PostgresSchemaIdentity schemaIdentity = PostgresSchemaIdentity.FromRunId(runId);
    private readonly Guid ownerGuid = Guid.NewGuid();
    private string schema => schemaIdentity.Name;
    private NpgsqlDataSource source = null!;
    private bool schemaCommitAttempted;
    private int topK;
    private int graphDepth;
    private int corpusCount;

    /// <summary>Gets the observed PostgreSQL and topology profile.</summary>
    public TargetProfile Profile { get; private set; } = new(PostgreSQLPgvectorToken, UnverifiedToken, SinglePrimaryNoReplicasToken,
        FsyncOnSynchronousCommitOnLocalWALFlushToken, READCOMMITTEDOnPrimaryStreamsEmulateTheNarrowAtomicOneEventContractContractText, PooledPreparedSQLTCPToken, DatabaseOwnerNoRLSMaskingToken, image);

    /// <summary>Reports support for the target's document, vector, queue, graph, and stream scenarios.</summary>
    /// <param name="scenario">The comparison scenario to check.</param>
    /// <returns>Whether this target implements the scenario.</returns>
    public bool Supports(Scenario scenario) => scenario is Scenario.PointRead or Scenario.DocumentWrite or Scenario.DocumentUpdate or Scenario.DocumentDelete or Scenario.VectorExact
        or Scenario.QueueCycle or Scenario.GraphNeighbors or Scenario.GraphTraverse or Scenario.StreamAppend or Scenario.StreamRead;

    /// <summary>Creates the owned schema, verifies settings, seeds data, and observes replication copies.</summary>
    /// <param name="dataset">The deterministic corpus and dimensions, graph, and concurrency options.</param>
    /// <param name="cancellationToken">A token that cancels database setup and data operations.</param>
    /// <returns>A task that completes after schema creation, seeding, and copy observation.</returns>
    public async Task InitializeAsync(IComparisonCorpus dataset, CancellationToken cancellationToken)
    {
        const string DocumentOnlyReadCommittedContract = "READ COMMITTED primary document reads; S1 keeps the native nullable embedding column NULL";
        const string PublicToken = ",public";

        ArgumentNullException.ThrowIfNull(dataset);
        topK = dataset.Settings.TopK;
        graphDepth = dataset.Settings.GraphDepth;
        if (dataset.Settings is ScaledComparisonProfile)
        {
            Profile = Profile with { ReadContract = DocumentOnlyReadCommittedContract };
        }
        corpusCount = dataset.Documents.Count;
        var execution = executionOptions.Value;
        var settings = new NpgsqlConnectionStringBuilder(connectionString)
        {
            MaxAutoPrepare = execution.PostgresMaxAutoPrepare,
            AutoPrepareMinUsages = execution.PostgresAutoPrepareMinUsages,
            MaxPoolSize = Math.Max(execution.PostgresMinimumPoolSize, dataset.Settings.Concurrency),
            SearchPath = schema + PublicToken
        };
        source = NpgsqlDataSource.Create(settings.ConnectionString);
        try
        {
            await using var connection = await source.OpenConnectionAsync(cancellationToken);
            await PostgresSchemaInitialization.InitializeAsync(connection: connection, dataset: dataset, identity: schemaIdentity,
                ownerGuid: ownerGuid, topology: topology, initialProfile: Profile, updateProfile: profile => Profile = profile,
                markCommitAttempted: () => schemaCommitAttempted = true, cancellationToken: cancellationToken,
                lifecycleOptions: lifecycleOptions);
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
        => new PostgresComparisonSession(await source.OpenConnectionAsync(cancellationToken), topK, graphDepth, corpusCount, lifecycleOptions);

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
