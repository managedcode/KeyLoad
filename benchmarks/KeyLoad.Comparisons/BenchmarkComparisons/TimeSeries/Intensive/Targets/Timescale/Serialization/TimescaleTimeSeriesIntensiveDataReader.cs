using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Npgsql;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimescaleTimeSeriesIntensiveDataReader
{
    internal static async Task<ImmutableArray<SampleRecord>> ReadSamplesAsync(NpgsqlDataReader reader,
        int limit, int initialCapacity, CancellationToken token)
    {
        const int FirstElementIndex = 0;
        const int GeometricGrowthFactor = 2;
        const int FirstColumnIndex = 0;
        const int SecondColumnIndex = 1;
        const int ThirdColumnIndex = 2;
        const int FourthColumnIndex = 3;
        const int OrdinalIdentity = 4;
        const int ReadSamplesAsyncOrdinalIdentity = 5;

        TimescaleTimeSeriesIntensiveReader.ValidateColumns(reader, TimescaleTimeSeriesIntensiveProtocol.SampleColumns);
        var records = new SampleRecord[initialCapacity];
        var count = FirstElementIndex;
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (count == records.Length)
            {
                Array.Resize(ref records, checked(records.Length * GeometricGrowthFactor));
            }
            if (count >= limit || await reader.IsDBNullAsync(FirstColumnIndex, token).ConfigureAwait(false)
                || await reader.IsDBNullAsync(SecondColumnIndex, token).ConfigureAwait(false)
                || await reader.IsDBNullAsync(ThirdColumnIndex, token).ConfigureAwait(false)
                || await reader.IsDBNullAsync(FourthColumnIndex, token).ConfigureAwait(false)
                || await reader.IsDBNullAsync(OrdinalIdentity, token).ConfigureAwait(false)
                || await reader.IsDBNullAsync(ReadSamplesAsyncOrdinalIdentity, token).ConfigureAwait(false))
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
        const int FirstColumnIndex = 0;
        const int SecondColumnIndex = 1;
        const int ThirdColumnIndex = 2;
        const int FourthColumnIndex = 3;
        const int OrdinalIdentity = 4;
        const int ReadLatestAsyncOrdinalIdentity = 5;

        TimescaleTimeSeriesIntensiveReader.ValidateColumns(reader, TimescaleTimeSeriesIntensiveProtocol.SampleColumns);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            await TimescaleTimeSeriesIntensiveReader.ValidateEndAsync(reader, token).ConfigureAwait(false);
            return null;
        }
        if (await reader.IsDBNullAsync(FirstColumnIndex, token).ConfigureAwait(false)
            || await reader.IsDBNullAsync(SecondColumnIndex, token).ConfigureAwait(false)
            || await reader.IsDBNullAsync(ThirdColumnIndex, token).ConfigureAwait(false)
            || await reader.IsDBNullAsync(FourthColumnIndex, token).ConfigureAwait(false)
            || await reader.IsDBNullAsync(OrdinalIdentity, token).ConfigureAwait(false)
            || await reader.IsDBNullAsync(ReadLatestAsyncOrdinalIdentity, token).ConfigureAwait(false))
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
        const int FirstColumnIndex = 0;
        const int SecondColumnIndex = 1;
        const int NoObservedItems = 0;

        TimescaleTimeSeriesIntensiveReader.ValidateColumns(reader, TimescaleTimeSeriesIntensiveProtocol.AggregateColumns);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            throw TimescaleTimeSeriesIntensiveReader.InvalidReply();
        }
        if (await reader.IsDBNullAsync(FirstColumnIndex, token).ConfigureAwait(false)
            || await reader.IsDBNullAsync(SecondColumnIndex, token).ConfigureAwait(false))
        {
            throw TimescaleTimeSeriesIntensiveReader.InvalidReply();
        }
        var aggregate = await ReadAggregateAsync(reader, NoObservedItems, token).ConfigureAwait(false);
        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            throw TimescaleTimeSeriesIntensiveReader.InvalidReply();
        }
        await TimescaleTimeSeriesIntensiveReader.ValidateEndAsync(reader, token).ConfigureAwait(false);
        return aggregate;
    }

    internal static async Task<ImmutableArray<SampleAggregateWindow>> ReadWindowsAsync(NpgsqlDataReader reader,
        int maxWindows, int initialCapacity, CancellationToken token)
    {
        const int FirstElementIndex = 0;
        const int GeometricGrowthFactor = 2;
        const int FirstColumnIndex = 0;
        const int SecondColumnIndex = 1;
        const int FourthColumnIndex = 3;
        const int FifthColumnIndex = 4;
        const int OrdinalIdentity = 2;
        const int ThirdContractOrdinal = 3;

        TimescaleTimeSeriesIntensiveReader.ValidateColumns(reader, TimescaleTimeSeriesIntensiveProtocol.WindowColumns);
        var windows = new SampleAggregateWindow[initialCapacity];
        var count = FirstElementIndex;
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (count == windows.Length)
            {
                Array.Resize(ref windows, checked(windows.Length * GeometricGrowthFactor));
            }
            if (count >= maxWindows || await reader.IsDBNullAsync(FirstColumnIndex, token).ConfigureAwait(false)
                || await reader.IsDBNullAsync(SecondColumnIndex, token).ConfigureAwait(false)
                || await reader.IsDBNullAsync(FourthColumnIndex, token).ConfigureAwait(false)
                || await reader.IsDBNullAsync(FifthColumnIndex, token).ConfigureAwait(false)
                || reader.GetInt32(FirstColumnIndex) != count)
            {
                throw TimescaleTimeSeriesIntensiveReader.InvalidReply();
            }
            var from = Utc(reader.GetDateTime(SecondColumnIndex));
            DateTimeOffset? until = await reader.IsDBNullAsync(OrdinalIdentity, token).ConfigureAwait(false)
                ? null : Utc(reader.GetDateTime(OrdinalIdentity));
            windows[count++] = new(from, until, await ReadAggregateAsync(reader, ThirdContractOrdinal, token).ConfigureAwait(false));
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
        const int FirstColumnIndex = 0;
        const int SecondColumnIndex = 1;
        const int ThirdColumnIndex = 2;
        const int FourthColumnIndex = 3;
        const int OrdinalIdentity = 4;
        const int ReadSampleOrdinalIdentity = 5;

        return new(reader.GetString(FirstColumnIndex), new SampleData(reader.GetString(SecondColumnIndex), Utc(reader.GetDateTime(ThirdColumnIndex)),
            reader.GetDouble(FourthColumnIndex)), reader.GetInt64(OrdinalIdentity), reader.GetString(ReadSampleOrdinalIdentity));
    }

    private static async Task<SampleAggregate> ReadAggregateAsync(NpgsqlDataReader reader, int offset,
        CancellationToken token)
    {
        const int SingleItemCount = 1;
        const int PairMemberCount = 2;
        const int ThirdContractOrdinal = 3;
        const int OrdinalIdentity = 4;

        return new(reader.GetInt64(offset), reader.GetDouble(offset + SingleItemCount),
            await NullableDoubleAsync(reader, offset + PairMemberCount, token).ConfigureAwait(false),
            await NullableDoubleAsync(reader, offset + ThirdContractOrdinal, token).ConfigureAwait(false),
            await NullableDoubleAsync(reader, offset + OrdinalIdentity, token).ConfigureAwait(false));
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
