using System.Collections.Immutable;
using System.Globalization;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed partial class TimescaleTimeSeriesIntensiveTarget
{
    public Task InitializeAsync(CancellationToken cancellationToken) =>
        TimescaleTimeSeriesIntensiveSetup.InitializeAsync(this, cancellationToken);

    public Task SeedAsync(string seriesId, ImmutableArray<SampleData> samples, string tagsJson,
        CancellationToken cancellationToken) =>
        TimescaleTimeSeriesIntensiveSetup.SeedAsync(this, seriesId, samples, tagsJson, cancellationToken);

    internal static Guid SeedCommandId(string runId, int batch) =>
        TimescaleTimeSeriesIntensiveSetup.SeedCommandId(runId, batch);
}

internal static class TimescaleTimeSeriesIntensiveSetup
{
    internal static async Task InitializeAsync(TimescaleTimeSeriesIntensiveTarget target, CancellationToken token)
    {
        target.EnsureOpen();
        if (!target.TryBeginInitialization())
        {
            throw InvalidInitialization();
        }
        var lease = target.Context.EnterOperation();
        try
        {
            target.OwnsSchema = await target.Session.InitializeAsync(target.Context, token).ConfigureAwait(false);
            if (!target.OwnsSchema)
            {
                throw InvalidInitialization();
            }
            target.MarkInitializationSucceeded();
        }
        catch (Exception)
        {
            target.MarkInitializationFailed();
            throw;
        }
        finally
        {
            await lease.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal static async Task SeedAsync(TimescaleTimeSeriesIntensiveTarget target, string seriesId,
        ImmutableArray<SampleData> samples, string tagsJson, CancellationToken token)
    {
        var batch = target.ReadSeedOrdinal();
        ValidateAndReserve(target, batch, seriesId, samples);
        try
        {
            await ExecuteBatchAsync(target, batch, seriesId, samples, tagsJson, token).ConfigureAwait(false);
            if (!target.CompleteSeedOrdinal(batch))
            {
                throw InvalidSeed();
            }
        }
        catch (Exception)
        {
            target.ReleaseSeedOrdinal(batch);
            throw;
        }
    }

    internal static Guid SeedCommandId(string runId, int batch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentOutOfRangeException.ThrowIfNegative(batch);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(batch, TimeSeriesIntensiveProfile.SeedBatchCount);
        return TimeSeriesIntensivePlans.CommandId(runId,
            "seed:b" + batch.ToString(CultureInfo.InvariantCulture));
    }

    private static void ValidateAndReserve(TimescaleTimeSeriesIntensiveTarget target, int batch, string seriesId,
        ImmutableArray<SampleData> samples)
    {
        if (batch < 0 || batch >= TimeSeriesIntensiveProfile.SeedBatchCount
            || seriesId != TimeSeriesIntensiveProfile.SeedSeries || samples.IsDefault
            || samples.Length != TimeSeriesIntensiveProfile.SeedBatchSize
            || !target.TryReserveSeedOrdinal(batch))
        {
            throw InvalidSeed();
        }
    }

    private static async Task ExecuteBatchAsync(TimescaleTimeSeriesIntensiveTarget target, int batch,
        string seriesId, ImmutableArray<SampleData> samples, string tagsJson, CancellationToken token)
    {
        var commandId = SeedCommandId(target.Context.RunId, batch);
        var operation = await target.StartOperationAsync(token).ConfigureAwait(false);
        try
        {
            await ReadValidateCommitAsync(operation, seriesId, commandId, batch, samples, tagsJson, token)
                .ConfigureAwait(false);
            await operation.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            operation.RecordPrimary(error);
            await operation.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task ReadValidateCommitAsync(TimescaleTimeSeriesIntensiveOperation operation,
        string seriesId, Guid commandId, int batch, ImmutableArray<SampleData> samples, string tagsJson,
        CancellationToken token)
    {
        var transaction = operation.Own(await operation.Connection.BeginTransactionAsync(token).ConfigureAwait(false));
        var command = operation.CreateCommand(TimescaleTimeSeriesIntensiveRoutine.SeedBatch, transaction);
        AddSeedParameters(command, seriesId, commandId, samples, tagsJson);
        var reader = operation.Own(await command.ExecuteReaderAsync(token).ConfigureAwait(false));
        var rows = await TimescaleTimeSeriesIntensiveReader.ReadBatchAsync(reader, token).ConfigureAwait(false);
        _ = TimescaleTimeSeriesIntensiveReceipt.ValidateBatch(rows, commandId, batch);
        await operation.DisposeResourceAsync(reader).ConfigureAwait(false);
        if (operation.CleanupException is { } readerFailure)
        {
            throw readerFailure;
        }
        await transaction.CommitAsync(token).ConfigureAwait(false);
    }

    private static void AddSeedParameters(TimescaleTimeSeriesIntensiveCommand command, string seriesId,
        Guid commandId, ImmutableArray<SampleData> samples, string tagsJson)
    {
        var events = new string[samples.Length];
        var timestamps = new DateTime[samples.Length];
        var values = new double[samples.Length];
        for (var index = 0; index < samples.Length; index++)
        {
            events[index] = samples[index].EventId;
            timestamps[index] = samples[index].Timestamp.UtcDateTime;
            values[index] = samples[index].Value;
        }
        TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.SetType, TimeSeriesIntensiveProfile.Name);
        TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.SeriesType, seriesId);
        TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.CommandIdType, commandId);
        TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.EventArrayType, events);
        TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.TimestampArrayType, timestamps);
        TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.ValueArrayType, values);
        TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.TagsType, tagsJson);
    }

    private static ComparisonFailureException InvalidInitialization() =>
        new(TimescaleTimeSeriesIntensiveProtocol.InvalidInitialization);

    private static ComparisonFailureException InvalidSeed() => new(TimescaleTimeSeriesIntensiveProtocol.InvalidSeed);
}
