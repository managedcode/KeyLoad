using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Npgsql;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimescaleTimeSeriesIntensiveReader
{
    internal static async Task<ImmutableArray<TimescaleTimeSeriesIntensiveAppendRow>> ReadBatchAsync(
        NpgsqlDataReader reader, CancellationToken token)
    {
        ValidateColumns(reader, TimescaleTimeSeriesIntensiveProtocol.BatchColumns);
        var rows = new TimescaleTimeSeriesIntensiveAppendRow[TimeSeriesIntensiveProfile.SeedBatchSize];
        var count = 0;
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (count == rows.Length || await reader.IsDBNullAsync(0, token).ConfigureAwait(false)
                || await reader.IsDBNullAsync(1, token).ConfigureAwait(false)
                || await reader.IsDBNullAsync(2, token).ConfigureAwait(false))
            {
                throw InvalidReply();
            }
            rows[count++] = new(reader.GetInt32(0), reader.GetGuid(1), reader.GetInt64(2));
        }
        await ValidateEndAsync(reader, token).ConfigureAwait(false);
        if (count != rows.Length)
        {
            Array.Resize(ref rows, count);
        }
        return ImmutableCollectionsMarshal.AsImmutableArray(rows);
    }

    internal static async Task<TimeSeriesIntensiveAppendReceipt> ReadScalarAsync(NpgsqlDataReader reader,
        CancellationToken token)
    {
        ValidateColumns(reader, TimescaleTimeSeriesIntensiveProtocol.ScalarColumns);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            throw InvalidReply();
        }
        if (await reader.IsDBNullAsync(0, token).ConfigureAwait(false)
            || await reader.IsDBNullAsync(1, token).ConfigureAwait(false))
        {
            throw InvalidReply();
        }
        var receipt = new TimeSeriesIntensiveAppendReceipt(reader.GetGuid(0), reader.GetInt64(1));
        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            throw InvalidReply();
        }
        await ValidateEndAsync(reader, token).ConfigureAwait(false);
        return receipt;
    }

    internal static void ValidateColumns(NpgsqlDataReader reader, ReadOnlySpan<string> expected)
    {
        if (reader.FieldCount != expected.Length)
        {
            throw InvalidReply();
        }
        for (var index = 0; index < expected.Length; index++)
        {
            if (!string.Equals(reader.GetName(index), expected[index], StringComparison.Ordinal))
            {
                throw InvalidReply();
            }
        }
    }

    internal static async Task ValidateEndAsync(NpgsqlDataReader reader, CancellationToken token)
    {
        if (await reader.NextResultAsync(token).ConfigureAwait(false))
        {
            throw InvalidReply();
        }
    }

    internal static ComparisonFailureException InvalidReply() =>
        new(TimescaleTimeSeriesIntensiveProtocol.InvalidReply);
}
