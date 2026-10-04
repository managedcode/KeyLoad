using Npgsql;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal enum TimescaleTimeSeriesIntensiveRoutine
{
    SeedBatch,
    AppendOne,
    Read,
    Latest,
    Aggregate,
    Windows
}

internal sealed class TimescaleTimeSeriesIntensiveCommand : IAsyncDisposable
{
    private readonly NpgsqlCommand command;

    internal TimescaleTimeSeriesIntensiveCommand(TimescaleTimeSeriesIntensiveRoutine routine, NpgsqlConnection connection,
        NpgsqlTransaction? transaction = null)
    {
        command = routine switch
        {
            TimescaleTimeSeriesIntensiveRoutine.SeedBatch => new NpgsqlCommand(TimescaleTimeSeriesIntensiveProtocol.SeedBatchSql, connection, transaction),
            TimescaleTimeSeriesIntensiveRoutine.AppendOne => new NpgsqlCommand(TimescaleTimeSeriesIntensiveProtocol.AppendOneSql, connection, transaction),
            TimescaleTimeSeriesIntensiveRoutine.Read => new NpgsqlCommand(TimescaleTimeSeriesIntensiveProtocol.ReadSql, connection, transaction),
            TimescaleTimeSeriesIntensiveRoutine.Latest => new NpgsqlCommand(TimescaleTimeSeriesIntensiveProtocol.LatestSql, connection, transaction),
            TimescaleTimeSeriesIntensiveRoutine.Aggregate => new NpgsqlCommand(TimescaleTimeSeriesIntensiveProtocol.AggregateSql, connection, transaction),
            TimescaleTimeSeriesIntensiveRoutine.Windows => new NpgsqlCommand(TimescaleTimeSeriesIntensiveProtocol.WindowsSql, connection, transaction),
            _ => throw new ArgumentOutOfRangeException(nameof(routine))
        };
    }

    internal NpgsqlParameterCollection Parameters => command.Parameters;

    internal Task<NpgsqlDataReader> ExecuteReaderAsync(CancellationToken token) => command.ExecuteReaderAsync(token);

    public ValueTask DisposeAsync() => command.DisposeAsync();
}
