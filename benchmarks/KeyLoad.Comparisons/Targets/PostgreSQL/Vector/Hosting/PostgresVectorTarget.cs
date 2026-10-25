using Microsoft.Extensions.Options;
using Npgsql;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Owns the PostgreSQL schema and lifecycle for one scaled execution vector cell.</summary>
public sealed class PostgresVectorTarget : IVectorComparisonTarget
{
    private readonly TimeProvider timeProvider;
    private const string VersionSeparator = "; pgvector ";
    private const string FlushEnabled = "on";
    private const string FlushRequired = "PostgresFlushRequired";
    private readonly NpgsqlDataSource source;
    private readonly IOptions<ComparisonExecutionOptions> execution;
    private readonly ComparisonSerializationOptions serialization;
    private ComparisonExecutionOptions Policy => execution.Value;
    private readonly PostgresSchemaIdentity identity;
    private readonly Guid ownerGuid = Guid.NewGuid();
    private readonly ComparisonTopology topology;
    private readonly PostgresTopology runtimeTopology;
    private bool schemaCommitAttempted;
    private VectorComparisonProfile? profile;

    /// <summary>Creates an isolated execution vector target for one Aspire-owned PostgreSQL service.</summary>
    /// <param name="connectionString">The execution Aspire-discovered connection.</param>
    /// <param name="runId">The unique isolated schema identity.</param>
    /// <param name="image">The immutable execution image reference.</param>
    /// <param name="topology">The actual execution PostgreSQL topology.</param>
    /// <param name="executionOptions">The validated execution execution limits.</param>
    /// <param name="serializationOptions">The centrally validated execution SQL builder reservations.</param>
    /// <param name="lifecycleOptions">The centrally validated execution replication lifecycle limits.</param>
    /// <param name="provider">Borrowed clock; defaults to the system provider.</param>
    public PostgresVectorTarget(string connectionString, string runId, string image,
        ComparisonTopology topology, IOptions<ComparisonExecutionOptions> executionOptions,
        IOptions<ComparisonSerializationOptions> serializationOptions, IOptions<ComparisonLifecycleOptions> lifecycleOptions, TimeProvider? provider = null)
    {
        timeProvider = provider ?? TimeProvider.System;
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(executionOptions);
        execution = ComparisonExecutionOptions.Require(executionOptions);
        serialization = ComparisonSerializationOptions.Require(serializationOptions).Value;
        ArgumentNullException.ThrowIfNull(lifecycleOptions);
        lifecycleOptions.Value.Validate();
        identity = PostgresSchemaIdentity.FromRunId(runId);
        this.topology = topology;
        runtimeTopology = new(lifecycleOptions, timeProvider);
        source = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(connectionString)
        {
            MaxAutoPrepare = Policy.PostgresMaxAutoPrepare,
            AutoPrepareMinUsages = Policy.PostgresAutoPrepareMinUsages,
            MaxPoolSize = Policy.PostgresMaxPoolSize,
            CommandTimeout = checked((int)Math.Ceiling(Policy.OperationTimeout.TotalSeconds)),
            SearchPath = identity.Name + PostgresVectorTargetValues.Public
        }.ConnectionString);
        Profile = new(PostgresVectorTargetValues.PostgreSQLPgvector, PostgresVectorTargetValues.Unverified, PostgresVectorTargetValues.UnverifiedPostgreSQLTopology,
            PostgresVectorTargetValues.SynchronousCommitOnLocalPostgreSQLWAL, PostgresVectorTargetValues.READCOMMITTEDPrimaryVectorReadback,
            PostgresVectorTargetValues.NpgsqlPooledSQLTCP, PostgresVectorTargetValues.IsolatedMarkedBenchmarkSchema, image);
    }

    /// <inheritdoc />
    public string Name => PostgresVectorTargetValues.PostgreSQLPgvector;
    /// <inheritdoc />
    public TargetProfile Profile
    {
        get; private set;
    }

    /// <inheritdoc />
    public bool Supports(VectorIndexKind indexKind, VectorQueryMode queryMode)
        => indexKind is VectorIndexKind.Exact or VectorIndexKind.Hnsw or VectorIndexKind.IvfFlat;

    /// <inheritdoc />
    public async Task<int> IngestAsync(IAsyncEnumerable<VectorDocument> documents, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documents);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        var inserted = await PostgresVectorStorage.IngestAsync(source, documents, Policy, serialization.PostgresVectorComponentBuilderCapacity, cancellationToken).ConfigureAwait(false);
        await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        Profile = await runtimeTopology.ObserveCopiesAsync(connection, topology, Profile, cancellationToken).ConfigureAwait(false);
        return inserted;
    }

    /// <inheritdoc />
    public async Task<VectorIndexReceipt> BuildIndexAsync(VectorComparisonProfile selected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selected);
        profile = selected;
        var receipt = await PostgresVectorIndex.BuildAsync(source, selected, Policy, cancellationToken: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
        var parameters = new Dictionary<string, string>(receipt.Parameters, StringComparer.Ordinal);
        serialization.RecordEvidence(parameters);
        return receipt with
        {
            Parameters = parameters
        };
    }

    /// <inheritdoc />
    public IAsyncEnumerable<VectorReadback> ReadbackAsync(CancellationToken cancellationToken)
        => PostgresVectorStorage.ReadbackAsync(source, Policy, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<VectorNeighbor>> SearchAsync(ReadOnlyMemory<float> query, int topK,
        VectorQueryMode mode, CancellationToken cancellationToken)
        => PostgresVectorIndex.SearchAsync(source, RequireProfile(), query, topK, mode, serialization.PostgresVectorComponentBuilderCapacity, cancellationToken);

    /// <inheritdoc />
    public Task<string> ExplainAsync(ReadOnlyMemory<float> query, VectorQueryMode mode, CancellationToken cancellationToken)
        => PostgresVectorIndex.ExplainAsync(source, RequireProfile(), query, mode, serialization.PostgresVectorComponentBuilderCapacity, cancellationToken);

    /// <inheritdoc />
    public Task UpdateAsync(VectorUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        return PostgresVectorStorage.UpdateAsync(source, update, serialization.PostgresVectorComponentBuilderCapacity, cancellationToken);
    }

    /// <inheritdoc />
    public Task<global::ManagedCode.Communication.Result<VectorReadback?>> ReadAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return PostgresVectorStorage.ReadAsync(source, id, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (schemaCommitAttempted)
            {
                using var deadline = new CancellationTokenSource(Policy.CleanupTimeout, timeProvider);
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
        => profile ?? throw new InvalidOperationException(PostgresVectorTargetValues.TheVectorIndexHasNot);

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (schemaCommitAttempted)
        {
            throw new InvalidOperationException(PostgresVectorTargetValues.APostgreSQLVectorTargetCanIngest);
        }

        await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await runtimeTopology.ConfigureReplicationAsync(connection, topology, cancellationToken).ConfigureAwait(false);
        await PostgresSchemaLifecycle.CreateAsync(connection, identity, ownerGuid, dimensions: PostgresVectorTargetValues.VectorDimensions,
            () => schemaCommitAttempted = true, cancellationToken).ConfigureAwait(false);
        await PostgresVectorStorage.AddVectorReadbackColumnsAsync(connection, cancellationToken).ConfigureAwait(false);
        Profile = await VerifySettingsAsync(connection, Profile, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<TargetProfile> VerifySettingsAsync(NpgsqlConnection connection, TargetProfile profile,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = PostgresSchemaCommands.VerifySettings;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (reader.GetString(PostgresVectorTargetValues.FsyncColumnOrdinal) != FlushEnabled || reader.GetString(PostgresVectorTargetValues.SynchronousCommitColumnOrdinal) != FlushEnabled)
        {
            throw new ComparisonFailureException(FlushRequired);
        }

        return profile with
        {
            Version = reader.GetString(PostgresVectorTargetValues.ServerVersionColumnOrdinal) + VersionSeparator + reader.GetString(PostgresVectorTargetValues.PgvectorVersionColumnOrdinal),
            Transport = reader.GetBoolean(PostgresVectorTargetValues.TlsColumnOrdinal) ? PostgresVectorTargetValues.NpgsqlPooledSQLTLS : PostgresVectorTargetValues.NpgsqlPooledSQLTCP
        };
    }
}
