using Npgsql;

namespace KeyLoad.Comparisons.Targets;

internal static class PostgresSchemaInitialization
{
    private const string EdgeCopySql = "COPY edges(source,target) FROM STDIN (FORMAT BINARY)";
    private const string AnalyzeSql = "ANALYZE documents; ANALYZE queue; ANALYZE edges";
    private const string FlushEnabled = "on";
    private const string FlushFailure = "PostgresFlushRequired";
    private const string VersionSeparator = "; pgvector ";
    private const string TlsTransport = "pooled prepared SQL/TLS";
    private const string TcpTransport = "pooled prepared SQL/TCP";

    internal static async Task InitializeAsync(NpgsqlConnection connection, BenchmarkDataset dataset,
        PostgresSchemaIdentity identity, Guid ownerGuid, ComparisonTopology topology, TargetProfile initialProfile,
        Action<TargetProfile> updateProfile, Action markCommitAttempted, CancellationToken cancellationToken)
    {
        await PostgresTopology.ConfigureReplicationAsync(connection, topology, cancellationToken);
        await PostgresSchemaLifecycle.CreateAsync(connection, identity, ownerGuid, dataset.Options.Dimensions,
            markCommitAttempted, cancellationToken);
        var profile = await VerifySettingsAsync(connection, initialProfile, updateProfile, cancellationToken);
        await PostgresDocumentOperations.SeedAsync(connection, dataset, cancellationToken);
        await PostgresStreamOperations.SeedAsync(connection, dataset, cancellationToken);
        await CopyEdgesAsync(connection, dataset, cancellationToken);
        await AnalyzeAsync(connection, cancellationToken);
        updateProfile(await PostgresTopology.ObserveCopiesAsync(connection, topology, profile, cancellationToken));
    }

    private static async Task<TargetProfile> VerifySettingsAsync(NpgsqlConnection connection, TargetProfile profile,
        Action<TargetProfile> updateProfile, CancellationToken cancellationToken)
    {
        await using var verify = connection.CreateCommand();
        verify.CommandText = PostgresSchemaCommands.VerifySettings;
        await using var reader = await verify.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        if (reader.GetString(1) != FlushEnabled || reader.GetString(2) != FlushEnabled)
        {
            throw new ComparisonFailureException(FlushFailure);
        }

        var observed = profile with
        {
            Version = reader.GetString(0) + VersionSeparator + reader.GetString(3),
            Transport = reader.GetBoolean(4) ? TlsTransport : TcpTransport
        };
        updateProfile(observed);
        return observed;
    }

    private static async Task CopyEdgesAsync(NpgsqlConnection connection, BenchmarkDataset dataset,
        CancellationToken cancellationToken)
    {
        await using var copy = await connection.BeginBinaryImportAsync(EdgeCopySql, cancellationToken);
        foreach (var edge in dataset.Edges)
        {
            await copy.StartRowAsync(cancellationToken);
            await copy.WriteAsync(edge.From, NpgsqlTypes.NpgsqlDbType.Text, cancellationToken);
            await copy.WriteAsync(edge.To, NpgsqlTypes.NpgsqlDbType.Text, cancellationToken);
        }

        await copy.CompleteAsync(cancellationToken);
    }

    private static async Task AnalyzeAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var analyze = connection.CreateCommand();
        analyze.CommandText = AnalyzeSql;
        await analyze.ExecuteNonQueryAsync(cancellationToken);
    }
}
