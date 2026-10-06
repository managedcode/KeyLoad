using Microsoft.Extensions.Options;
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

    internal static async Task InitializeAsync(NpgsqlConnection connection, IComparisonCorpus dataset, PostgresSchemaIdentity identity, Guid ownerGuid, ComparisonTopology topology, TargetProfile initialProfile, Action<TargetProfile> updateProfile, Action markCommitAttempted, IOptions<ComparisonLifecycleOptions> lifecycleOptions, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var nativeTopology = new PostgresTopology(lifecycleOptions, provider: timeProvider);
        await nativeTopology.ConfigureReplicationAsync(connection, topology, cancellationToken);
        await PostgresSchemaLifecycle.CreateAsync(connection, identity, ownerGuid, dataset.Settings.Dimensions,
            markCommitAttempted, cancellationToken);
        var profile = await VerifySettingsAsync(connection, initialProfile, updateProfile, cancellationToken);
        await PostgresDocumentOperations.SeedAsync(connection, dataset, cancellationToken);
        if (dataset is BenchmarkDataset control)
        {
            await PostgresStreamOperations.SeedAsync(connection, control, cancellationToken);
            await CopyEdgesAsync(connection, dataset, cancellationToken);
        }
        await AnalyzeAsync(connection, cancellationToken);
        updateProfile(await nativeTopology.ObserveCopiesAsync(connection, topology, profile, cancellationToken));
    }

    private static async Task<TargetProfile> VerifySettingsAsync(NpgsqlConnection connection, TargetProfile profile,
        Action<TargetProfile> updateProfile, CancellationToken cancellationToken)
    {
        const int FifthColumnIndex = 4;

        const int SecondColumnIndex = 1;
        const int ThirdColumnIndex = 2;
        const int FirstColumnIndex = 0;
        const int FourthColumnIndex = 3;

        await using var verify = connection.CreateCommand();
        verify.CommandText = PostgresSchemaCommands.VerifySettings;
        await using var reader = await verify.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        if (reader.GetString(SecondColumnIndex) != FlushEnabled || reader.GetString(ThirdColumnIndex) != FlushEnabled)
        {
            throw new ComparisonFailureException(FlushFailure);
        }

        var observed = profile with
        {
            Version = reader.GetString(FirstColumnIndex) + VersionSeparator + reader.GetString(FourthColumnIndex),
            Transport = reader.GetBoolean(FifthColumnIndex) ? TlsTransport : TcpTransport
        };
        updateProfile(observed);
        return observed;
    }

    private static async Task CopyEdgesAsync(NpgsqlConnection connection, IComparisonCorpus dataset,
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
