using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Npgsql;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimescaleTimeSeriesIntensiveReader
{
    internal static async Task<ImmutableArray<TimescaleTimeSeriesIntensiveAppendRow>> ReadBatchAsync(
        NpgsqlDataReader reader, CancellationToken token)
    {
        const int FirstElementIndex = 0;
        const int FirstColumnIndex = 0;
        const int SecondColumnIndex = 1;
        const int ThirdColumnIndex = 2;

        ValidateColumns(reader, TimescaleTimeSeriesIntensiveProtocol.BatchColumns);
        var rows = new TimescaleTimeSeriesIntensiveAppendRow[TimeSeriesIntensiveProfile.SeedBatchSize];
        var count = FirstElementIndex;
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (count == rows.Length || await reader.IsDBNullAsync(FirstColumnIndex, token).ConfigureAwait(false)
                || await reader.IsDBNullAsync(SecondColumnIndex, token).ConfigureAwait(false)
                || await reader.IsDBNullAsync(ThirdColumnIndex, token).ConfigureAwait(false))
            {
                throw InvalidReply();
            }
            rows[count++] = new(reader.GetInt32(FirstColumnIndex), reader.GetGuid(SecondColumnIndex), reader.GetInt64(ThirdColumnIndex));
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
        const int FirstColumnIndex = 0;
        const int SecondColumnIndex = 1;

        ValidateColumns(reader, TimescaleTimeSeriesIntensiveProtocol.ScalarColumns);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            throw InvalidReply();
        }
        if (await reader.IsDBNullAsync(FirstColumnIndex, token).ConfigureAwait(false)
            || await reader.IsDBNullAsync(SecondColumnIndex, token).ConfigureAwait(false))
        {
            throw InvalidReply();
        }
        var receipt = new TimeSeriesIntensiveAppendReceipt(reader.GetGuid(FirstColumnIndex), reader.GetInt64(SecondColumnIndex));
        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            throw InvalidReply();
        }
        await ValidateEndAsync(reader, token).ConfigureAwait(false);
        return receipt;
    }

    internal static void ValidateColumns(NpgsqlDataReader reader, ReadOnlySpan<string> expected)
    {
        const int FirstElementIndex = 0;

        if (reader.FieldCount != expected.Length)
        {
            throw InvalidReply();
        }
        for (var index = FirstElementIndex; index < expected.Length; index++)
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
