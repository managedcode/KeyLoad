using Npgsql;
using NpgsqlTypes;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimescaleTimeSeriesIntensiveParameters
{
    internal static void Add(TimescaleTimeSeriesIntensiveCommand command, NpgsqlDbType type, object value)
    {
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = type, Value = value });
    }

    internal static void AddNullable(TimescaleTimeSeriesIntensiveCommand command, NpgsqlDbType type, object? value)
    {
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = type, Value = value ?? DBNull.Value });
    }
}
