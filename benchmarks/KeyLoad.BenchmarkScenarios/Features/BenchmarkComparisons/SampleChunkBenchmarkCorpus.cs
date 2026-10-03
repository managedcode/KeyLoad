using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

internal static class SampleChunkBenchmarkCorpus
{
    internal const string Regular = "regular";
    internal const string Late = "late-equal";
    internal const string Random = "deterministic-random";
    internal const int Seed = 78_031;
    private const string Series = "metrics-Київ🌍";
    private static readonly DateTimeOffset Start = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static SampleRecord[] Create(int count, string corpus)
    {
        if (count is not (1 or 32 or 256) || corpus is not (Regular or Late or Random))
        {
            throw new ArgumentException("The sample chunk control corpus is unsupported.");
        }
        var records = new SampleRecord[count];
        for (var index = 0; index < count; index++)
        {
            var ticks = TimestampTicks(count, index, corpus);
            var timestamp = Start.AddTicks(ticks).ToOffset(Offset(index));
            var value = corpus == Random
                ? ((RandomPart(index, 1) >> 11) * (1d / (1UL << 53)) - 0.5) * 10_000 : 100d + index / 10d;
            var tags = "{\"region\":\"Київ🌍\",\"sensor\":\"sensor-"
                + (corpus == Regular ? 0 : index % 8).ToString(CultureInfo.InvariantCulture) + "\"}";
            var id = "sample-" + index.ToString(CultureInfo.InvariantCulture);
            records[index] = new(Series, new(id, timestamp, value), index + 1, tags);
        }
        Array.Sort(records, static (left, right) =>
        {
            var time = left.Sample.Timestamp.UtcTicks.CompareTo(right.Sample.Timestamp.UtcTicks);
            return time != 0 ? time : left.Sequence.CompareTo(right.Sequence);
        });
        return records;
    }

    private static long TimestampTicks(int count, int index, string corpus)
        => corpus switch
        {
            Regular => index * TimeSpan.TicksPerMinute,
            Late => ((index * 17 % count) / 2) * TimeSpan.TicksPerMinute,
            Random => (long)(RandomPart(index, 0) % TimeSpan.TicksPerDay),
            _ => throw new ArgumentException("The sample chunk control corpus is unsupported.")
        };

    private static TimeSpan Offset(int index)
        => TimeSpan.FromMinutes((index % 3) switch { 0 => -330, 1 => 0, _ => 120 });

    private static ulong RandomPart(int index, int lane)
    {
        Span<byte> input = stackalloc byte[sizeof(int) * 3];
        BinaryPrimitives.WriteInt32LittleEndian(input, Seed);
        BinaryPrimitives.WriteInt32LittleEndian(input[sizeof(int)..], index);
        BinaryPrimitives.WriteInt32LittleEndian(input[(sizeof(int) * 2)..], lane);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        _ = SHA256.HashData(input, digest);
        return BinaryPrimitives.ReadUInt64LittleEndian(digest);
    }
}
