using System.Collections.Immutable;
using Npgsql;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal sealed class TimescaleTimeSeriesTarget(string connectionString, string? image = null)
    : ITimeSeriesPersistentTarget, ITimeSeriesAggregationTarget
{
    private const string TargetName = "TimescaleDB TimeSeries";
    private const string StorageGuarantee =
        "Container-lifetime single-node TimescaleDB hypertable; no cross-run volume and no replica guarantee";
    private const string AcknowledgementGuarantee = "PostgreSQL transaction commit acknowledged by one server";
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(15);
    private readonly Guid ownerId = Guid.NewGuid();
    private string schemaName = string.Empty;
    private int ownsSchema;

    public TimeSeriesTargetMetadata Metadata { get; } = new(TargetName, "Persisted TimescaleDB hypertable",
        StorageGuarantee, AcknowledgementGuarantee, image, null);

    public async Task InitializeAsync(TimeSeriesComparisonWorkload workload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workload);
        if (Volatile.Read(ref ownsSchema) != 0)
        {
            throw TimeSeriesComparisonTargetErrors.Create("TimescaleAlreadyInitialized");
        }

        schemaName = TimescaleSchemaLifecycle.SchemaName(workload.RunId);
        try
        {
            var confirmed = await TimescaleSchemaLifecycle.InitializeAsync(connectionString, schemaName, ownerId,
                cancellationToken).ConfigureAwait(false);
            if (!confirmed)
            {
                throw TimeSeriesComparisonTargetErrors.Create("TimescaleOwnershipUnconfirmed");
            }

            Interlocked.Exchange(ref ownsSchema, 1);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException error) when (TimeSeriesComparisonTargetErrors.TryGetCode(error, out _))
        {
            throw;
        }
        catch (NpgsqlException error)
        {
            throw TimeSeriesComparisonTargetErrors.Create("TimescaleInitializeFailed", error);
        }
        catch (TimeoutException error)
        {
            throw TimeSeriesComparisonTargetErrors.Create("TimescaleInitializeFailed", error);
        }
        catch (ArgumentException error)
        {
            throw TimeSeriesComparisonTargetErrors.Create("TimescaleInitializeFailed", error);
        }
        catch (InvalidOperationException error)
        {
            throw TimeSeriesComparisonTargetErrors.Create("TimescaleInitializeFailed", error);
        }
    }

    public Task SeedAsync(TimeSeriesComparisonWorkload workload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workload);
        RequireOwnership();
        return TimescaleSampleStore.SeedAsync(connectionString, schemaName, workload, cancellationToken);
    }

    public Task<TimeSeriesReadResult> ReadAsync(TimeSeriesComparisonWorkload workload,
        TimeSeriesReadRange range, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workload);
        ArgumentNullException.ThrowIfNull(range);
        return TimescaleSampleStore.ReadAsync(connectionString, schemaName, workload, range, cancellationToken);
    }

    public Task<ImmutableArray<TimeSeriesBucketValue>> AggregateAsync(TimeSeriesComparisonWorkload workload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workload);
        RequireOwnership();
        return TimescaleSampleStore.AggregateAsync(connectionString, schemaName, workload, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref ownsSchema) == 0)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(CleanupTimeout);
        try
        {
            await TimescaleSchemaLifecycle.DropOwnedSchemaAsync(connectionString, schemaName, ownerId, timeout.Token)
                .ConfigureAwait(false);
            Interlocked.Exchange(ref ownsSchema, 0);
        }
        catch (InvalidOperationException error) when (TimeSeriesComparisonTargetErrors.TryGetCode(error, out _))
        {
            throw;
        }
        catch (NpgsqlException error)
        {
            throw TimeSeriesComparisonTargetErrors.Create("TimescaleCleanupFailed", error);
        }
        catch (TimeoutException error)
        {
            throw TimeSeriesComparisonTargetErrors.Create("TimescaleCleanupFailed", error);
        }
        catch (ArgumentException error)
        {
            throw TimeSeriesComparisonTargetErrors.Create("TimescaleCleanupFailed", error);
        }
        catch (InvalidOperationException error)
        {
            throw TimeSeriesComparisonTargetErrors.Create("TimescaleCleanupFailed", error);
        }
        catch (OperationCanceledException error)
        {
            throw TimeSeriesComparisonTargetErrors.Create("TimescaleCleanupFailed", error);
        }
    }

    private void RequireOwnership()
    {
        if (Volatile.Read(ref ownsSchema) == 0)
        {
            throw TimeSeriesComparisonTargetErrors.Create("TimescaleNamespaceNotOwned");
        }
    }
}
