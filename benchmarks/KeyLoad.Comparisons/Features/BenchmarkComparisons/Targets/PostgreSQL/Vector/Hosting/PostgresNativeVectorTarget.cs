using Npgsql;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Owns the PostgreSQL schema and lifecycle for one scaled native vector cell.</summary>
public sealed class PostgresNativeVectorTarget : IVectorComparisonTarget
{
    private const string VersionSeparator = "; pgvector ";
    private const string FlushEnabled = "on";
    private const string FlushRequired = "PostgresFlushRequired";
    private readonly NpgsqlDataSource source;
    private readonly NativeComparisonExecutionOptions execution;
    private readonly PostgresSchemaIdentity identity;
    private readonly Guid ownerGuid = Guid.NewGuid();
    private readonly ComparisonTopology topology;
    private bool schemaCommitAttempted;
    private VectorComparisonProfile? profile;

    /// <summary>Creates an isolated native vector target for one Aspire-owned PostgreSQL service.</summary>
    /// <param name="connectionString">The native Aspire-discovered connection.</param>
    /// <param name="runId">The unique isolated schema identity.</param>
    /// <param name="image">The immutable native image reference.</param>
    /// <param name="topology">The actual native PostgreSQL topology.</param>
    /// <param name="executionOptions">The validated native execution limits.</param>
    public PostgresNativeVectorTarget(string connectionString, string runId, string image,
        ComparisonTopology topology, IOptions<NativeComparisonExecutionOptions> executionOptions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(executionOptions);
        execution = executionOptions.Value;
        execution.Validate();
        identity = PostgresSchemaIdentity.FromRunId(runId);
        this.topology = topology;
        source = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(connectionString)
        {
            MaxAutoPrepare = execution.PostgresMaxAutoPrepare,
            AutoPrepareMinUsages = execution.PostgresAutoPrepareMinUsages,
            MaxPoolSize = execution.PostgresMaxPoolSize,
            CommandTimeout = checked((int)Math.Ceiling(execution.OperationTimeout.TotalSeconds)),
            SearchPath = identity.Name + PostgresNativeVectorTargetValues.Public
        }.ConnectionString);
        Profile = new(PostgresNativeVectorTargetValues.PostgreSQLPgvector, PostgresNativeVectorTargetValues.Unverified, PostgresNativeVectorTargetValues.UnverifiedNativePostgreSQLTopology,
            PostgresNativeVectorTargetValues.SynchronousCommitOnLocalPostgreSQLWAL, PostgresNativeVectorTargetValues.READCOMMITTEDPrimaryVectorReadback,
            PostgresNativeVectorTargetValues.NpgsqlPooledSQLTCP, PostgresNativeVectorTargetValues.IsolatedMarkedBenchmarkSchema, image);
    }

    /// <inheritdoc />
    public string Name => PostgresNativeVectorTargetValues.PostgreSQLPgvector;
    /// <inheritdoc />
    public TargetProfile Profile { get; private set; }

    /// <inheritdoc />
    public bool Supports(VectorIndexKind indexKind, VectorQueryMode queryMode)
        => indexKind is VectorIndexKind.Exact or VectorIndexKind.Hnsw or VectorIndexKind.IvfFlat;

    /// <inheritdoc />
    public async Task<int> IngestAsync(IAsyncEnumerable<VectorDocument> documents, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documents);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        return await PostgresNativeVectorStorage.IngestAsync(source, documents, execution, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<VectorIndexReceipt> BuildIndexAsync(VectorComparisonProfile selected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selected);
        profile = selected;
        return await PostgresNativeVectorIndex.BuildAsync(source, selected, execution, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<VectorReadback> ReadbackAsync(CancellationToken cancellationToken)
        => PostgresNativeVectorStorage.ReadbackAsync(source, execution, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<VectorNeighbor>> SearchAsync(ReadOnlyMemory<float> query, int topK,
        VectorQueryMode mode, CancellationToken cancellationToken)
        => PostgresNativeVectorIndex.SearchAsync(source, RequireProfile(), query, topK, mode, cancellationToken);

    /// <inheritdoc />
    public Task<string> ExplainAsync(ReadOnlyMemory<float> query, VectorQueryMode mode, CancellationToken cancellationToken)
        => PostgresNativeVectorIndex.ExplainAsync(source, RequireProfile(), query, mode, cancellationToken);

    /// <inheritdoc />
    public Task UpdateAsync(VectorUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        return PostgresNativeVectorStorage.UpdateAsync(source, update, cancellationToken);
    }

    /// <inheritdoc />
    public Task<VectorReadback?> ReadAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return PostgresNativeVectorStorage.ReadAsync(source, id, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (schemaCommitAttempted)
            {
                using var deadline = new CancellationTokenSource(execution.CleanupTimeout);
                await using var connection = await source.OpenConnectionAsync(deadline.Token).ConfigureAwait(false);
                await PostgresSchemaLifecycle.DropIfOwnedAsync(connection, identity, ownerGuid, deadline.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            await source.DisposeAsync().ConfigureAwait(false);
        }
    }

    private VectorComparisonProfile RequireProfile()
        => profile ?? throw new InvalidOperationException(PostgresNativeVectorTargetValues.TheNativeVectorIndexHasNot);

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (schemaCommitAttempted)
        {
            throw new InvalidOperationException(PostgresNativeVectorTargetValues.APostgreSQLVectorTargetCanIngest);
        }

        await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await PostgresTopology.ConfigureReplicationAsync(connection, topology, cancellationToken).ConfigureAwait(false);
        await PostgresSchemaLifecycle.CreateAsync(connection, identity, ownerGuid, dimensions: PostgresNativeVectorTargetValues.VectorDimensions,
            () => schemaCommitAttempted = true, cancellationToken).ConfigureAwait(false);
        await PostgresNativeVectorStorage.AddVectorReadbackColumnsAsync(connection, cancellationToken).ConfigureAwait(false);
        Profile = await VerifySettingsAsync(connection, Profile, cancellationToken).ConfigureAwait(false);
        Profile = await PostgresTopology.ObserveCopiesAsync(connection, topology, Profile, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<TargetProfile> VerifySettingsAsync(NpgsqlConnection connection, TargetProfile profile,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = PostgresSchemaCommands.VerifySettings;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (reader.GetString(PostgresNativeVectorTargetValues.SingleElementOffset) != FlushEnabled || reader.GetString(PostgresNativeVectorTargetValues.VectorBoundaryCount) != FlushEnabled)
        {
            throw new ComparisonFailureException(FlushRequired);
        }

        return profile with
        {
            Version = reader.GetString(PostgresNativeVectorTargetValues.FirstIndex) + VersionSeparator + reader.GetString(PostgresNativeVectorTargetValues.ReportSchemaVersion),
            Transport = reader.GetBoolean(PostgresNativeVectorTargetValues.FloatByteCount) ? PostgresNativeVectorTargetValues.NpgsqlPooledSQLTLS : PostgresNativeVectorTargetValues.NpgsqlPooledSQLTCP
        };
    }
}
