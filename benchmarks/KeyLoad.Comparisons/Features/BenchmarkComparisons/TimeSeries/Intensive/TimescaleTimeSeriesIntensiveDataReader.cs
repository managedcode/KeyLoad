using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Npgsql;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimescaleTimeSeriesIntensiveDataReader
{
    internal static async Task<ImmutableArray<SampleRecord>> ReadSamplesAsync(NpgsqlDataReader reader,
        int limit, CancellationToken token)
    {
        TimescaleTimeSeriesIntensiveReader.ValidateColumns(reader, TimescaleTimeSeriesIntensiveProtocol.SampleColumns);
        var records = new SampleRecord[16];
        var count = 0;
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (count == records.Length)
            {
                Array.Resize(ref records, checked(records.Length * 2));
            }
            if (count >= limit || await reader.IsDBNullAsync(0, token).ConfigureAwait(false)
                || await reader.IsDBNullAsync(1, token).ConfigureAwait(false)
                || await reader.IsDBNullAsync(2, token).ConfigureAwait(false)
                || await reader.IsDBNullAsync(3, token).ConfigureAwait(false)
                || await reader.IsDBNullAsync(4, token).ConfigureAwait(false)
                || await reader.IsDBNullAsync(5, token).ConfigureAwait(false))
            {
                throw TimescaleTimeSeriesIntensiveReader.InvalidReply();
            }
            records[count++] = ReadSample(reader);
        }
        await TimescaleTimeSeriesIntensiveReader.ValidateEndAsync(reader, token).ConfigureAwait(false);
        if (count != records.Length)
        {
            Array.Resize(ref records, count);
        }
        return ImmutableCollectionsMarshal.AsImmutableArray(records);
    }

    internal static async Task<SampleRecord?> ReadLatestAsync(NpgsqlDataReader reader, CancellationToken token)
    {
        TimescaleTimeSeriesIntensiveReader.ValidateColumns(reader, TimescaleTimeSeriesIntensiveProtocol.SampleColumns);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            await TimescaleTimeSeriesIntensiveReader.ValidateEndAsync(reader, token).ConfigureAwait(false);
            return null;
        }
        if (await reader.IsDBNullAsync(0, token).ConfigureAwait(false)
            || await reader.IsDBNullAsync(1, token).ConfigureAwait(false)
            || await reader.IsDBNullAsync(2, token).ConfigureAwait(false)
            || await reader.IsDBNullAsync(3, token).ConfigureAwait(false)
            || await reader.IsDBNullAsync(4, token).ConfigureAwait(false)
            || await reader.IsDBNullAsync(5, token).ConfigureAwait(false))
        {
            throw TimescaleTimeSeriesIntensiveReader.InvalidReply();
        }
        var sample = ReadSample(reader);
        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            throw TimescaleTimeSeriesIntensiveReader.InvalidReply();
        }
        await TimescaleTimeSeriesIntensiveReader.ValidateEndAsync(reader, token).ConfigureAwait(false);
        return sample;
    }

    internal static async Task<SampleAggregate> ReadAggregateAsync(NpgsqlDataReader reader, CancellationToken token)
    {
        TimescaleTimeSeriesIntensiveReader.ValidateColumns(reader, TimescaleTimeSeriesIntensiveProtocol.AggregateColumns);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            throw TimescaleTimeSeriesIntensiveReader.InvalidReply();
        }
        if (await reader.IsDBNullAsync(0, token).ConfigureAwait(false)
            || await reader.IsDBNullAsync(1, token).ConfigureAwait(false))
        {
            throw TimescaleTimeSeriesIntensiveReader.InvalidReply();
        }
        var aggregate = await ReadAggregateAsync(reader, 0, token).ConfigureAwait(false);
        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            throw TimescaleTimeSeriesIntensiveReader.InvalidReply();
        }
        await TimescaleTimeSeriesIntensiveReader.ValidateEndAsync(reader, token).ConfigureAwait(false);
        return aggregate;
    }

    internal static async Task<ImmutableArray<SampleAggregateWindow>> ReadWindowsAsync(NpgsqlDataReader reader,
        int maxWindows, CancellationToken token)
    {
        TimescaleTimeSeriesIntensiveReader.ValidateColumns(reader, TimescaleTimeSeriesIntensiveProtocol.WindowColumns);
        var windows = new SampleAggregateWindow[16];
        var count = 0;
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (count == windows.Length)
            {
                Array.Resize(ref windows, checked(windows.Length * 2));
            }
            if (count >= maxWindows || await reader.IsDBNullAsync(0, token).ConfigureAwait(false)
                || await reader.IsDBNullAsync(1, token).ConfigureAwait(false)
                || await reader.IsDBNullAsync(3, token).ConfigureAwait(false)
                || await reader.IsDBNullAsync(4, token).ConfigureAwait(false)
                || reader.GetInt32(0) != count)
            {
                throw TimescaleTimeSeriesIntensiveReader.InvalidReply();
            }
            var from = Utc(reader.GetDateTime(1));
            DateTimeOffset? until = await reader.IsDBNullAsync(2, token).ConfigureAwait(false)
                ? null : Utc(reader.GetDateTime(2));
            windows[count++] = new(from, until, await ReadAggregateAsync(reader, 3, token).ConfigureAwait(false));
        }
        await TimescaleTimeSeriesIntensiveReader.ValidateEndAsync(reader, token).ConfigureAwait(false);
        if (count != windows.Length)
        {
            Array.Resize(ref windows, count);
        }
        return ImmutableCollectionsMarshal.AsImmutableArray(windows);
    }

    private static SampleRecord ReadSample(NpgsqlDataReader reader)
    {
        return new(reader.GetString(0), new SampleData(reader.GetString(1), Utc(reader.GetDateTime(2)),
            reader.GetDouble(3)), reader.GetInt64(4), reader.GetString(5));
    }

    private static async Task<SampleAggregate> ReadAggregateAsync(NpgsqlDataReader reader, int offset,
        CancellationToken token)
    {
        return new(reader.GetInt64(offset), reader.GetDouble(offset + 1),
            await NullableDoubleAsync(reader, offset + 2, token).ConfigureAwait(false),
            await NullableDoubleAsync(reader, offset + 3, token).ConfigureAwait(false),
            await NullableDoubleAsync(reader, offset + 4, token).ConfigureAwait(false));
    }

    private static async Task<double?> NullableDoubleAsync(NpgsqlDataReader reader, int ordinal, CancellationToken token) =>
        await reader.IsDBNullAsync(ordinal, token).ConfigureAwait(false) ? null : reader.GetDouble(ordinal);

    private static DateTimeOffset Utc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw TimescaleTimeSeriesIntensiveReader.InvalidReply();
        }
        return new DateTimeOffset(value);
    }

}
