using System.Collections.Immutable;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed partial class TimescaleTimeSeriesIntensiveTarget
{
    public Task<ImmutableArray<SampleRecord>> ReadAsync(string seriesId, DateTimeOffset from, DateTimeOffset until,
        int limit, CancellationToken cancellationToken) =>
        TimescaleTimeSeriesIntensiveReads.ReadAsync(this, seriesId, from, until, limit, cancellationToken);

    public Task<SampleRecord?> LatestAsync(string seriesId, DateTimeOffset? atOrBefore,
        CancellationToken cancellationToken) =>
        TimescaleTimeSeriesIntensiveReads.LatestAsync(this, seriesId, atOrBefore, cancellationToken);

    public Task<SampleAggregate> AggregateAsync(string seriesId, DateTimeOffset from, DateTimeOffset? untilExclusive,
        int maxSamples, CancellationToken cancellationToken) =>
        TimescaleTimeSeriesIntensiveReads.AggregateAsync(this, seriesId, from, untilExclusive, maxSamples, cancellationToken);

    public Task<ImmutableArray<SampleAggregateWindow>> WindowsAsync(string seriesId, DateTimeOffset from,
        DateTimeOffset? untilExclusive, TimeSpan width, int maxSamples, int maxWindows,
        CancellationToken cancellationToken) =>
        TimescaleTimeSeriesIntensiveReads.WindowsAsync(this, seriesId, from, untilExclusive, width, maxSamples,
            maxWindows, cancellationToken);
}

internal static class TimescaleTimeSeriesIntensiveReads
{

    internal static async Task<ImmutableArray<SampleRecord>> ReadAsync(TimescaleTimeSeriesIntensiveTarget target,
        string seriesId, DateTimeOffset from,
        DateTimeOffset until, int limit, CancellationToken token)
    {
        var operation = await target.StartOperationAsync(token).ConfigureAwait(false);
        try
        {
            var command = operation.CreateCommand(TimescaleTimeSeriesIntensiveRoutine.Read);
            AddReadParameters(command, seriesId, from, until);
            TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.IntegerType, limit);
            var reader = operation.Own(await command.ExecuteReaderAsync(token).ConfigureAwait(false));
            var result = await TimescaleTimeSeriesIntensiveDataReader.ReadSamplesAsync(reader, limit, token).ConfigureAwait(false);
            await operation.DisposeResourceAsync(reader).ConfigureAwait(false);
            await operation.DisposeAsync().ConfigureAwait(false);
            return result;
        }
        catch (Exception error)
        {
            operation.RecordPrimary(error);
            await operation.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static async Task<SampleRecord?> LatestAsync(TimescaleTimeSeriesIntensiveTarget target,
        string seriesId, DateTimeOffset? atOrBefore, CancellationToken token)
    {
        var operation = await target.StartOperationAsync(token).ConfigureAwait(false);
        try
        {
            var command = operation.CreateCommand(TimescaleTimeSeriesIntensiveRoutine.Latest);
            TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.SetType, TimeSeriesIntensiveProfile.Name);
            TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.SeriesType, seriesId);
            TimescaleTimeSeriesIntensiveParameters.AddNullable(command, TimescaleTimeSeriesIntensiveProtocol.TimestampType,
                atOrBefore?.UtcDateTime);
            var reader = operation.Own(await command.ExecuteReaderAsync(token).ConfigureAwait(false));
            var result = await TimescaleTimeSeriesIntensiveDataReader.ReadLatestAsync(reader, token).ConfigureAwait(false);
            await operation.DisposeResourceAsync(reader).ConfigureAwait(false);
            await operation.DisposeAsync().ConfigureAwait(false);
            return result;
        }
        catch (Exception error)
        {
            operation.RecordPrimary(error);
            await operation.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static async Task<SampleAggregate> AggregateAsync(TimescaleTimeSeriesIntensiveTarget target,
        string seriesId, DateTimeOffset from,
        DateTimeOffset? untilExclusive, int maxSamples, CancellationToken token)
    {
        var operation = await target.StartOperationAsync(token).ConfigureAwait(false);
        try
        {
            var command = operation.CreateCommand(TimescaleTimeSeriesIntensiveRoutine.Aggregate);
            TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.SetType, TimeSeriesIntensiveProfile.Name);
            TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.SeriesType, seriesId);
            TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.TimestampType, from.UtcDateTime);
            TimescaleTimeSeriesIntensiveParameters.AddNullable(command, TimescaleTimeSeriesIntensiveProtocol.TimestampType,
                untilExclusive?.UtcDateTime);
            TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.IntegerType, maxSamples);
            var reader = operation.Own(await command.ExecuteReaderAsync(token).ConfigureAwait(false));
            var result = await TimescaleTimeSeriesIntensiveDataReader.ReadAggregateAsync(reader, token).ConfigureAwait(false);
            await operation.DisposeResourceAsync(reader).ConfigureAwait(false);
            await operation.DisposeAsync().ConfigureAwait(false);
            return result;
        }
        catch (Exception error)
        {
            operation.RecordPrimary(error);
            await operation.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static async Task<ImmutableArray<SampleAggregateWindow>> WindowsAsync(TimescaleTimeSeriesIntensiveTarget target,
        string seriesId, DateTimeOffset from,
        DateTimeOffset? untilExclusive, TimeSpan width, int maxSamples, int maxWindows, CancellationToken token)
    {
        var operation = await target.StartOperationAsync(token).ConfigureAwait(false);
        try
        {
            var command = operation.CreateCommand(TimescaleTimeSeriesIntensiveRoutine.Windows);
            TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.SetType, TimeSeriesIntensiveProfile.Name);
            TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.SeriesType, seriesId);
            TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.TimestampType, from.UtcDateTime);
            TimescaleTimeSeriesIntensiveParameters.AddNullable(command, TimescaleTimeSeriesIntensiveProtocol.TimestampType,
                untilExclusive?.UtcDateTime);
            TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.IntervalType, width);
            TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.IntegerType, maxSamples);
            TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.IntegerType, maxWindows);
            var reader = operation.Own(await command.ExecuteReaderAsync(token).ConfigureAwait(false));
            var result = await TimescaleTimeSeriesIntensiveDataReader.ReadWindowsAsync(reader, maxWindows, token).ConfigureAwait(false);
            await operation.DisposeResourceAsync(reader).ConfigureAwait(false);
            await operation.DisposeAsync().ConfigureAwait(false);
            return result;
        }
        catch (Exception error)
        {
            operation.RecordPrimary(error);
            await operation.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static void AddReadParameters(TimescaleTimeSeriesIntensiveCommand command, string seriesId, DateTimeOffset from,
        DateTimeOffset until)
    {
        TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.SetType, TimeSeriesIntensiveProfile.Name);
        TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.SeriesType, seriesId);
        TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.TimestampType, from.UtcDateTime);
        TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.TimestampType, until.UtcDateTime);
    }
}
