
namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed partial class TimescaleTimeSeriesIntensiveTarget
{
    public Task<TimeSeriesIntensiveAppendReceipt> AppendAsync(string seriesId, Guid commandId,
        SampleData sample, string tagsJson, CancellationToken cancellationToken)
        => TimescaleTimeSeriesIntensiveWrites.AppendAsync(this, seriesId, commandId, sample, tagsJson, cancellationToken);
}

internal static class TimescaleTimeSeriesIntensiveWrites
{
    internal static async Task<TimeSeriesIntensiveAppendReceipt> AppendAsync(TimescaleTimeSeriesIntensiveTarget target,
        string seriesId, Guid commandId,
        SampleData sample, string tagsJson, CancellationToken cancellationToken)
    {
        var operation = await target.StartOperationAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var transaction = operation.Own(await operation.Connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false));
            var command = operation.CreateCommand(TimescaleTimeSeriesIntensiveRoutine.AppendOne, transaction);
            AddAppendParameters(command, seriesId, commandId, sample, tagsJson);
            var reader = operation.Own(await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false));
            var actual = await TimescaleTimeSeriesIntensiveReader.ReadScalarAsync(reader, cancellationToken).ConfigureAwait(false);
            var receipt = TimescaleTimeSeriesIntensiveReceipt.ValidateScalar(actual, commandId);
            await operation.DisposeResourceAsync(reader).ConfigureAwait(false);
            if (operation.CleanupException is { } readerFailure)
            {
                operation.RecordPrimary(readerFailure);
                throw readerFailure;
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            operation.Acknowledgement = TimeSeriesIntensiveAcknowledgement.FromReceipt(receipt);
            await operation.DisposeAsync().ConfigureAwait(false);
            return receipt;
        }
        catch (Exception error)
        {
            operation.RecordPrimary(error);
            await operation.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static void AddAppendParameters(TimescaleTimeSeriesIntensiveCommand command, string seriesId, Guid commandId,
        SampleData sample, string tagsJson)
    {
        TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.SetType, TimeSeriesIntensiveProfile.Name);
        TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.SeriesType, seriesId);
        TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.CommandIdType, commandId);
        TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.SeriesType, sample.EventId);
        TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.TimestampType,
            sample.Timestamp.UtcDateTime);
        TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.ValueType, sample.Value);
        TimescaleTimeSeriesIntensiveParameters.Add(command, TimescaleTimeSeriesIntensiveProtocol.TagsType, tagsJson);
    }
}
