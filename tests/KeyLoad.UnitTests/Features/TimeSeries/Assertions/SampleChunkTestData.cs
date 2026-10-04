using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Core;
using KeyLoad.Core.Features.TimeSeries;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleChunkTestData
{
    internal const string Series = "chunk-series";
    internal const string OtherSeries = "other-series";
    internal const int MaximumRecords = 256;
    internal const int MaximumBytes = 8_388_608;
    private const string ChecksumDomain = "keyload.sample-chunk.v1";
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static SampleRecord[] FixedRecords()
    {
        var offsetTime = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.FromHours(2));
        var sameInstantUtc = offsetTime.ToUniversalTime();
        return
        [
            Row("minimum", DateTimeOffset.MinValue, 4, -0d, "{}"),
            Row("offset-first", offsetTime, 7, 1.25d, "{\"kind\":\"cpu\",\"units\":\"μs\"}"),
            Row("offset-second", sameInstantUtc, 8, double.Epsilon, "{\"kind\":\"cpu\",\"units\":\"μs\"}"),
            Row("late-arrival", sameInstantUtc.AddTicks(1), 1, -2.5d, "{\"note\":\"late\"}"),
            Row("maximum", DateTimeOffset.MaxValue, 2, double.MaxValue, "{\"edge\":true}")
        ];
    }

    internal static SampleRecord[] SeededRecords(int count)
    {
        var records = new SampleRecord[count];
        for (var index = 0; index < count; index++)
        {
            var ticks = (long)(DeterministicBits(count, index, 0) % (ulong)(count + 1));
            var timestamp = Epoch.AddTicks(ticks);
            var valueBits = DeterministicBits(count, index, 1) >> 11;
            var value = (valueBits * (1d / (1UL << 53))) * 2d - 1d;
            records[index] = Row($"seed-{index:D3}", timestamp, index + 1, value,
                index % 3 == 0 ? "{}" : "{\"kind\":\"seeded\"}");
        }

        Array.Sort(records, static (left, right) =>
        {
            var order = left.Sample.Timestamp.UtcTicks.CompareTo(right.Sample.Timestamp.UtcTicks);
            return order != 0 ? order : left.Sequence.CompareTo(right.Sequence);
        });
        return records;
    }

    private static ulong DeterministicBits(int count, int index, byte lane)
    {
        Span<byte> material = stackalloc byte[13];
        BinaryPrimitives.WriteInt32LittleEndian(material, 0x5EED);
        BinaryPrimitives.WriteInt32LittleEndian(material[4..], count);
        BinaryPrimitives.WriteInt32LittleEndian(material[8..], index);
        material[12] = lane;
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(material, digest);
        return BinaryPrimitives.ReadUInt64LittleEndian(digest);
    }

    internal static SampleRecord[] TextRecords()
    {
        var timestamp = Epoch.AddDays(1);
        const string textSeries = "chunk-series-\uD800";
        var boundaryText = string.Concat(new string('x', 0x3FFE), "🐈", "\uD800", new string('猫', 32));
        return
        [
            new(textSeries, new("plain-é-🐈", timestamp, 1d), 1, "{\"message\":\"café 🐈\"}"),
            new(textSeries, new("lone-high-\uD800", timestamp.AddTicks(1), -0d), 2, "{\"surrogate\":\"\\ud800\"}"),
            new(textSeries, new("lone-low-\uDC00", timestamp.AddTicks(2), double.Epsilon), 3, "{\"surrogate\":\"\\udc00\"}"),
            new(textSeries, new(boundaryText, timestamp.AddTicks(3), 2d), 4, "{\"boundary\":true}")
        ];
    }

    internal static SampleRecord Row(string id, DateTimeOffset timestamp, long sequence, double value, string tags)
        => new(Series, new(id, timestamp, value), sequence, tags);

    internal static ReadExecutionBudget Budget(DatabaseLimits? limits = null,
        CancellationToken cancellationToken = default)
        => new(limits ?? new DatabaseLimits(), cancellationToken: cancellationToken);

    internal static ReadExecutionBudget ChargedDecodeBudget(ReadOnlySpan<byte> bytes,
        DatabaseLimits? limits = null, CancellationToken cancellationToken = default)
    {
        var budget = Budget(limits, cancellationToken);
        budget.ChargeBytes(bytes.Length);
        return budget;
    }

    internal static async Task AssertRecordsExact(SampleRecord[] expected, SampleRecord[] actual)
    {
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            var left = expected[index];
            var right = actual[index];
            await Assert.That(right.SeriesId).IsEqualTo(left.SeriesId);
            await Assert.That(right.Sample.EventId).IsEqualTo(left.Sample.EventId);
            await Assert.That(right.Sample.Timestamp.Ticks).IsEqualTo(left.Sample.Timestamp.Ticks);
            await Assert.That(right.Sample.Timestamp.Offset).IsEqualTo(left.Sample.Timestamp.Offset);
            await Assert.That(right.Sample.Timestamp.UtcTicks).IsEqualTo(left.Sample.Timestamp.UtcTicks);
            await Assert.That(right.Sequence).IsEqualTo(left.Sequence);
            await Assert.That(right.TagsJson).IsEqualTo(left.TagsJson);
            await Assert.That(BitConverter.DoubleToInt64Bits(right.Sample.Value))
                .IsEqualTo(BitConverter.DoubleToInt64Bits(left.Sample.Value));
        }
    }

    internal static byte[] Rechecksum(SampleChunkPayload payload)
    {
        var checksum = ComputeChecksum(payload);
        return NativeSerialization.Serialize(payload with { Checksum = checksum });
    }

    internal static SampleChunkPayload Payload(ReadOnlySpan<byte> encoded)
        => NativeSerialization.Deserialize<SampleChunkPayload>(encoded);

    internal static byte[] Serialize(SampleChunkPayload payload) => NativeSerialization.Serialize(payload);

    private static byte[] ComputeChecksum(SampleChunkPayload payload)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.ASCII.GetBytes(ChecksumDomain));
        Span<byte> scalar = stackalloc byte[sizeof(int)];
        AppendInt(payload.FormatVersion, scalar, hash);
        AppendInt(payload.RecordCount, scalar, hash);
        AppendColumn(payload.UtcTicks, scalar, hash);
        AppendColumn(payload.Offsets, scalar, hash);
        AppendColumn(payload.Sequences, scalar, hash);
        AppendColumn(payload.Values, scalar, hash);
        AppendColumn(payload.Series, scalar, hash);
        AppendColumn(payload.EventIds, scalar, hash);
        AppendColumn(payload.Tags, scalar, hash);
        return hash.GetHashAndReset();
    }

    private static void AppendColumn(ReadOnlyMemory<byte> column, Span<byte> scalar, IncrementalHash hash)
    {
        AppendInt(column.Length, scalar, hash);
        hash.AppendData(column.Span);
    }

    private static void AppendInt(int value, Span<byte> scalar, IncrementalHash hash)
    {
        BinaryPrimitives.WriteInt32LittleEndian(scalar, value);
        hash.AppendData(scalar);
    }
}
