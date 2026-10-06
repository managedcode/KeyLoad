using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

internal static class SampleChunkBenchmarkCorpus
{
    private const int CorpusStartYear = 2024;
    private const int CorpusStartMonth = 1;
    private const int CorpusStartDay = 1;
    private const int CorpusStartHour = 0;
    private const int CorpusStartMinute = 0;
    private const int CorpusStartSecond = 0;
    private const int RandomTickRangeFactor = 17;
    private const int RepeatedTimestampGroupSize = 2;
    private const int TimestampRandomLane = 0;
    private const string TimestampTicksFailureMessage = "The sample chunk control corpus is unsupported.";
    private const int OffsetCycleLength = 3;
    private const int OffsetEmptyCount = 0;
    private const int NegativeOffsetMinutes = -330;
    private const int OffsetSingleItemCount = 1;
    private const int PositiveOffsetMinutes = 120;

    internal const string Regular = "regular";
    internal const string Late = "late-equal";
    internal const string Random = "deterministic-random";
    internal const int Seed = 78_031;
    private const string Series = "metrics-Київ🌍";
    private static readonly DateTimeOffset Start = new(CorpusStartYear, CorpusStartMonth, CorpusStartDay, CorpusStartHour, CorpusStartMinute, CorpusStartSecond, TimeSpan.Zero);

    internal static SampleRecord[] Create(int count, string corpus)
    {
        const int CountFirstCount = 1;
        const int CountValidationBound = 32;
        const int CreateCountValidationBound = 256;
        const string CreateFailureMessage = "The sample chunk control corpus is unsupported.";
        const int IndexInitialValue = 0;
        const int LaneSingleItemCount = 1;
        const int RandomPartBitOffset = 11;
        const double UnitIntervalNumerator = 1d;
        const ulong UnitIntervalBase = 1UL;
        const int DoublePrecisionMantissaBits = 53;
        const double RandomValueMidpoint = 0.5;
        const int RandomValueRange = 10_000;
        const double RegularInitialValue = 100d;
        const double RegularValueDivisor = 10d;
        const string MetadataPrefix = "{\"region\":\"Київ🌍\",\"sensor\":\"sensor-";
        const int CreateAbsentCount = 0;
        const int IndexValidationBoundary = 8;
        const string MetadataSuffix = "\"}";
        const int IndexStep = 1;
        const int EmptyTime = 0;

        if (count is not (CountFirstCount or CountValidationBound or CreateCountValidationBound) || corpus is not (Regular or Late or Random))
        {
            throw new ArgumentException(CreateFailureMessage);
        }
        var records = new SampleRecord[count];
        for (var index = IndexInitialValue; index < count; index++)
        {
            var ticks = TimestampTicks(count, index, corpus);
            var timestamp = Start.AddTicks(ticks).ToOffset(Offset(index));
            var value = corpus == Random
                ? ((RandomPart(index, LaneSingleItemCount) >> RandomPartBitOffset) * (UnitIntervalNumerator / (UnitIntervalBase << DoublePrecisionMantissaBits)) - RandomValueMidpoint) * RandomValueRange : RegularInitialValue + index / RegularValueDivisor;
            var tags = MetadataPrefix
                + (corpus == Regular ? CreateAbsentCount : index % IndexValidationBoundary).ToString(CultureInfo.InvariantCulture) + MetadataSuffix;
            var id = "sample-" + index.ToString(CultureInfo.InvariantCulture);
            records[index] = new(Series, new(id, timestamp, value), index + IndexStep, tags);
        }
        Array.Sort(records, static (left, right) =>
        {
            var time = left.Sample.Timestamp.UtcTicks.CompareTo(right.Sample.Timestamp.UtcTicks);
            return time != EmptyTime ? time : left.Sequence.CompareTo(right.Sequence);
        });
        return records;
    }

    private static long TimestampTicks(int count, int index, string corpus)
        => corpus switch
        {
            Regular => index * TimeSpan.TicksPerMinute,
            Late => ((index * RandomTickRangeFactor % count) / RepeatedTimestampGroupSize) * TimeSpan.TicksPerMinute,
            Random => (long)(RandomPart(index, TimestampRandomLane) % TimeSpan.TicksPerDay),
            _ => throw new ArgumentException(TimestampTicksFailureMessage)
        };

    private static TimeSpan Offset(int index)
        => TimeSpan.FromMinutes((index % OffsetCycleLength) switch { OffsetEmptyCount => NegativeOffsetMinutes, OffsetSingleItemCount => OffsetEmptyCount, _ => PositiveOffsetMinutes });

    private static ulong RandomPart(int index, int lane)
    {
        const int SeedComponentCount = 3;
        const int InputComponentIndex = 2;

        Span<byte> input = stackalloc byte[sizeof(int) * SeedComponentCount];
        BinaryPrimitives.WriteInt32LittleEndian(input, Seed);
        BinaryPrimitives.WriteInt32LittleEndian(input[sizeof(int)..], index);
        BinaryPrimitives.WriteInt32LittleEndian(input[(sizeof(int) * InputComponentIndex)..], lane);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        _ = SHA256.HashData(input, digest);
        return BinaryPrimitives.ReadUInt64LittleEndian(digest);
    }
}
