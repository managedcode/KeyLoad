using Npgsql;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimescaleTimeSeriesIntensiveSession : IAsyncDisposable
{
    private readonly NpgsqlDataSource dataSource;

    internal TimescaleTimeSeriesIntensiveSession(string connectionString, string schemaName)
    {
        dataSource = NpgsqlDataSource.Create(CreateConnectionSettings(connectionString, schemaName));
    }

    internal static NpgsqlConnectionStringBuilder CreateConnectionSettings(string connectionString, string schemaName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        TimescaleSchemaLifecycle.ValidateSchemaName(schemaName);
        return new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = true,
            Timeout = TimescaleTimeSeriesIntensiveProtocol.ConnectionTimeoutSeconds,
            CommandTimeout = TimescaleTimeSeriesIntensiveProtocol.CommandTimeoutSeconds,
            CancellationTimeout = TimescaleTimeSeriesIntensiveProtocol.CancellationTimeoutMilliseconds,
            MinPoolSize = TimescaleTimeSeriesIntensiveProtocol.MinimumPoolSize,
            MaxPoolSize = TimescaleTimeSeriesIntensiveProtocol.MaximumPoolSize,
            Enlist = false,
            Multiplexing = false,
            NoResetOnClose = false,
            IncludeErrorDetail = false,
            LogParameters = false,
            SearchPath = schemaName + ",pg_catalog,pg_temp"
        };
    }

    internal NpgsqlConnection CreateConnection() => dataSource.CreateConnection();

    internal Task<bool> InitializeAsync(TimescaleTimeSeriesIntensiveContext context, CancellationToken cancellationToken) =>
        TimescaleSchemaLifecycle.InitializeOwnedAsync(dataSource, context.SchemaName, context.OwnerId,
            (connection, transaction, token) => TimescaleTimeSeriesIntensiveSchema.InstallAsync(
                connection, transaction, context.SchemaName, context.SeriesSet, token), cancellationToken);

    internal Task DropOwnedSchemaAsync(TimescaleTimeSeriesIntensiveContext context, CancellationToken cancellationToken) =>
        TimescaleSchemaLifecycle.DropOwnedSchemaAsync(dataSource, context.SchemaName, context.OwnerId,
            cancellationToken);

    public ValueTask DisposeAsync() => dataSource.DisposeAsync();
}
