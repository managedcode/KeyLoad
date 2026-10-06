using Microsoft.Extensions.Options;
using Npgsql;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Owns the PostgreSQL schema and lifecycle for one scaled native vector cell.</summary>
public sealed class PostgresNativeVectorTarget : IVectorComparisonTarget
{
    private readonly TimeProvider timeProvider;
    private const string VersionSeparator = "; pgvector ";
    private const string FlushEnabled = "on";
    private const string FlushRequired = "PostgresFlushRequired";
    private readonly NpgsqlDataSource source;
    private readonly IOptions<NativeComparisonExecutionOptions> execution;
    private readonly NativeComparisonSerializationOptions serialization;
    private NativeComparisonExecutionOptions Policy => execution.Value;
    private readonly PostgresSchemaIdentity identity;
    private readonly Guid ownerGuid = Guid.NewGuid();
    private readonly ComparisonTopology topology;
    private readonly PostgresTopology nativeTopology;
    private bool schemaCommitAttempted;
    private VectorComparisonProfile? profile;

    /// <summary>Creates an isolated native vector target for one Aspire-owned PostgreSQL service.</summary>
    /// <param name="connectionString">The native Aspire-discovered connection.</param>
    /// <param name="runId">The unique isolated schema identity.</param>
    /// <param name="image">The immutable native image reference.</param>
    /// <param name="topology">The actual native PostgreSQL topology.</param>
    /// <param name="executionOptions">The validated native execution limits.</param>
    /// <param name="serializationOptions">The centrally validated native SQL builder reservations.</param>
    /// <param name="lifecycleOptions">The centrally validated native replication lifecycle limits.</param>
    /// <param name="provider">Borrowed clock; defaults to the system provider.</param>
    public PostgresNativeVectorTarget(string connectionString, string runId, string image,
        ComparisonTopology topology, IOptions<NativeComparisonExecutionOptions> executionOptions,
        IOptions<NativeComparisonSerializationOptions> serializationOptions, IOptions<ComparisonLifecycleOptions> lifecycleOptions, TimeProvider? provider = null)
    {
        timeProvider = provider ?? TimeProvider.System;
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(executionOptions);
        execution = NativeComparisonExecutionOptions.Require(executionOptions);
        serialization = NativeComparisonSerializationOptions.Require(serializationOptions).Value;
        ArgumentNullException.ThrowIfNull(lifecycleOptions);
        lifecycleOptions.Value.Validate();
        identity = PostgresSchemaIdentity.FromRunId(runId);
        this.topology = topology;
        nativeTopology = new(lifecycleOptions, timeProvider);
        source = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(connectionString)
        {
            MaxAutoPrepare = Policy.PostgresMaxAutoPrepare,
            AutoPrepareMinUsages = Policy.PostgresAutoPrepareMinUsages,
            MaxPoolSize = Policy.PostgresMaxPoolSize,
            CommandTimeout = checked((int)Math.Ceiling(Policy.OperationTimeout.TotalSeconds)),
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
        var inserted = await PostgresNativeVectorStorage.IngestAsync(source, documents, Policy, serialization.PostgresVectorComponentBuilderCapacity, cancellationToken).ConfigureAwait(false);
        await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        Profile = await nativeTopology.ObserveCopiesAsync(connection, topology, Profile, cancellationToken).ConfigureAwait(false);
        return inserted;
    }

    /// <inheritdoc />
    public async Task<VectorIndexReceipt> BuildIndexAsync(VectorComparisonProfile selected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selected);
        profile = selected;
        var receipt = await PostgresNativeVectorIndex.BuildAsync(source, selected, Policy, cancellationToken: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
        var parameters = new Dictionary<string, string>(receipt.Parameters, StringComparer.Ordinal);
        serialization.RecordEvidence(parameters);
        return receipt with { Parameters = parameters };
    }

    /// <inheritdoc />
    public IAsyncEnumerable<VectorReadback> ReadbackAsync(CancellationToken cancellationToken)
        => PostgresNativeVectorStorage.ReadbackAsync(source, Policy, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<VectorNeighbor>> SearchAsync(ReadOnlyMemory<float> query, int topK,
        VectorQueryMode mode, CancellationToken cancellationToken)
        => PostgresNativeVectorIndex.SearchAsync(source, RequireProfile(), query, topK, mode, serialization.PostgresVectorComponentBuilderCapacity, cancellationToken);

    /// <inheritdoc />
    public Task<string> ExplainAsync(ReadOnlyMemory<float> query, VectorQueryMode mode, CancellationToken cancellationToken)
        => PostgresNativeVectorIndex.ExplainAsync(source, RequireProfile(), query, mode, serialization.PostgresVectorComponentBuilderCapacity, cancellationToken);

    /// <inheritdoc />
    public Task UpdateAsync(VectorUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        return PostgresNativeVectorStorage.UpdateAsync(source, update, serialization.PostgresVectorComponentBuilderCapacity, cancellationToken);
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
        => profile ?? throw new InvalidOperationException(PostgresNativeVectorTargetValues.TheNativeVectorIndexHasNot);

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (schemaCommitAttempted)
        {
            throw new InvalidOperationException(PostgresNativeVectorTargetValues.APostgreSQLVectorTargetCanIngest);
        }

        await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await nativeTopology.ConfigureReplicationAsync(connection, topology, cancellationToken).ConfigureAwait(false);
        await PostgresSchemaLifecycle.CreateAsync(connection, identity, ownerGuid, dimensions: PostgresNativeVectorTargetValues.VectorDimensions,
            () => schemaCommitAttempted = true, cancellationToken).ConfigureAwait(false);
        await PostgresNativeVectorStorage.AddVectorReadbackColumnsAsync(connection, cancellationToken).ConfigureAwait(false);
        Profile = await VerifySettingsAsync(connection, Profile, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<TargetProfile> VerifySettingsAsync(NpgsqlConnection connection, TargetProfile profile,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = PostgresSchemaCommands.VerifySettings;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (reader.GetString(PostgresNativeVectorTargetValues.FsyncColumnOrdinal) != FlushEnabled || reader.GetString(PostgresNativeVectorTargetValues.SynchronousCommitColumnOrdinal) != FlushEnabled)
        {
            throw new ComparisonFailureException(FlushRequired);
        }

        return profile with
        {
            Version = reader.GetString(PostgresNativeVectorTargetValues.ServerVersionColumnOrdinal) + VersionSeparator + reader.GetString(PostgresNativeVectorTargetValues.PgvectorVersionColumnOrdinal),
            Transport = reader.GetBoolean(PostgresNativeVectorTargetValues.TlsColumnOrdinal) ? PostgresNativeVectorTargetValues.NpgsqlPooledSQLTLS : PostgresNativeVectorTargetValues.NpgsqlPooledSQLTCP
        };
    }
}
