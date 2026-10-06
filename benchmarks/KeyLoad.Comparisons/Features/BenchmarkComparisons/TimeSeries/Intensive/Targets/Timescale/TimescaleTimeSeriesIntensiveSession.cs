using Microsoft.Extensions.Options;
using Npgsql;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimescaleTimeSeriesIntensiveSession : IAsyncDisposable
{
    private readonly NpgsqlDataSource dataSource;
    private readonly NativeComparisonExecutionOptions execution;

    internal int InitialReadCapacity => execution.TimeSeriesInitialReadCapacity;

    internal TimescaleTimeSeriesIntensiveSession(string connectionString, string schemaName, IOptions<NativeComparisonExecutionOptions> executionOptions)
    {
        execution = executionOptions.Value;
        execution.Validate();
        dataSource = NpgsqlDataSource.Create(CreateConnectionSettings(connectionString, schemaName, executionOptions));
    }

    internal static NpgsqlConnectionStringBuilder CreateConnectionSettings(string connectionString, string schemaName, IOptions<NativeComparisonExecutionOptions> executionOptions)
    {
        const string PgCatalogPgTempToken = ",pg_catalog,pg_temp";

        var execution = NativeComparisonExecutionOptions.Require(executionOptions).Value;
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        TimescaleSchemaLifecycle.ValidateSchemaName(schemaName);
        return new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = true,
            Timeout = checked((int)execution.TimescaleConnectionTimeout.TotalSeconds),
            CommandTimeout = checked((int)execution.TimescaleCommandTimeout.TotalSeconds),
            CancellationTimeout = execution.TimescaleCancellationTimeoutMilliseconds,
            MinPoolSize = execution.TimescaleMinimumPoolSize,
            MaxPoolSize = execution.TimescaleMaximumPoolSize,
            Enlist = false,
            Multiplexing = false,
            NoResetOnClose = false,
            IncludeErrorDetail = false,
            LogParameters = false,
            SearchPath = schemaName + PgCatalogPgTempToken
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
