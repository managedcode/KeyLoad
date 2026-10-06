using System.Collections.Immutable;
using Microsoft.Extensions.Options;
using Npgsql;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal sealed class TimescaleTimeSeriesTarget(string connectionString, IOptions<ComparisonLifecycleOptions> lifecycleOptions,
    string? image = null, TimeProvider? provider = null)
    : ITimeSeriesPersistentTarget, ITimeSeriesAggregationTarget
{
    private readonly TimeProvider timeProvider = provider ?? TimeProvider.System;
    private const string PersistedTimescaleDBHypertableToken = "Persisted TimescaleDB hypertable";

    private const string TargetName = "TimescaleDB TimeSeries";
    private const string StorageGuarantee =
        "Container-lifetime single-node TimescaleDB hypertable; no cross-run volume and no replica guarantee";
    private const string AcknowledgementGuarantee = "PostgreSQL transaction commit acknowledged by one server";
    private readonly ComparisonLifecycleOptions settings = lifecycleOptions.Value;
    private readonly Guid ownerId = Guid.NewGuid();
    private string schemaName = string.Empty;
    private int ownsSchema;

    public TimeSeriesTargetMetadata Metadata { get; } = new(TargetName, PersistedTimescaleDBHypertableToken,
        StorageGuarantee, AcknowledgementGuarantee, image, null);

    public async Task InitializeAsync(TimeSeriesComparisonWorkload workload, CancellationToken cancellationToken)
    {
        const string TimescaleInitializeFailedDetail = "TimescaleInitializeFailed";

        const int NoObservedItems = 0;
        const string TimescaleAlreadyInitializedDetail = "TimescaleAlreadyInitialized";
        const string TimescaleOwnershipUnconfirmedDetail = "TimescaleOwnershipUnconfirmed";
        const int SingleItemCount = 1;

        ArgumentNullException.ThrowIfNull(workload);
        if (Volatile.Read(ref ownsSchema) != NoObservedItems)
        {
            throw TimeSeriesComparisonTargetErrors.Create(TimescaleAlreadyInitializedDetail);
        }

        schemaName = TimescaleSchemaLifecycle.SchemaName(workload.RunId);
        try
        {
            var confirmed = await TimescaleSchemaLifecycle.InitializeAsync(connectionString, schemaName, ownerId,
                cancellationToken).ConfigureAwait(false);
            if (!confirmed)
            {
                throw TimeSeriesComparisonTargetErrors.Create(TimescaleOwnershipUnconfirmedDetail);
            }

            Interlocked.Exchange(ref ownsSchema, SingleItemCount);
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
            throw TimeSeriesComparisonTargetErrors.Create(TimescaleInitializeFailedDetail, error);
        }
        catch (TimeoutException error)
        {
            throw TimeSeriesComparisonTargetErrors.Create(TimescaleInitializeFailedDetail, error);
        }
        catch (ArgumentException error)
        {
            throw TimeSeriesComparisonTargetErrors.Create(TimescaleInitializeFailedDetail, error);
        }
        catch (InvalidOperationException error)
        {
            throw TimeSeriesComparisonTargetErrors.Create(TimescaleInitializeFailedDetail, error);
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
        const int NoObservedItems = 0;
        const string TimescaleCleanupFailedDetail = "TimescaleCleanupFailed";

        if (Volatile.Read(ref ownsSchema) == NoObservedItems)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(settings.TimescaleCleanupTimeout, timeProvider);
        try
        {
            await TimescaleSchemaLifecycle.DropOwnedSchemaAsync(connectionString, schemaName, ownerId, timeout.Token)
                .ConfigureAwait(false);
            Interlocked.Exchange(ref ownsSchema, NoObservedItems);
        }
        catch (InvalidOperationException error) when (TimeSeriesComparisonTargetErrors.TryGetCode(error, out _))
        {
            throw;
        }
        catch (NpgsqlException error)
        {
            throw TimeSeriesComparisonTargetErrors.Create(TimescaleCleanupFailedDetail, error);
        }
        catch (TimeoutException error)
        {
            throw TimeSeriesComparisonTargetErrors.Create(TimescaleCleanupFailedDetail, error);
        }
        catch (ArgumentException error)
        {
            throw TimeSeriesComparisonTargetErrors.Create(TimescaleCleanupFailedDetail, error);
        }
        catch (InvalidOperationException error)
        {
            throw TimeSeriesComparisonTargetErrors.Create(TimescaleCleanupFailedDetail, error);
        }
        catch (OperationCanceledException error)
        {
            throw TimeSeriesComparisonTargetErrors.Create(TimescaleCleanupFailedDetail, error);
        }
    }

    private void RequireOwnership()
    {
        const int NoObservedItems = 0;
        const string TimescaleNamespaceNotOwnedDetail = "TimescaleNamespaceNotOwned";

        if (Volatile.Read(ref ownsSchema) == NoObservedItems)
        {
            throw TimeSeriesComparisonTargetErrors.Create(TimescaleNamespaceNotOwnedDetail);
        }
    }
}
