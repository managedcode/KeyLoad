using KeyLoad.Core.Features.TimeSeries;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleChunkGoldenTests
{
    private const string NativeEnvelope = "IAADMQGthjxnRWtleWxvYWQuY29yZS52MS5TYW1wbGVDaHVua1BheWxvYWToAAUBBUETgIDWvd/+388IQQUAAEEDAUEVgICAgICAgICAAUEbGGNodW5rLXNlcmllc0EPDGdvbGRlbkELAQR7fQBBQffOlzG+MwYd4aAjddKx0PQUlCfz2y/Y6nNkxVahI3Yv4OA=";

    [Test]
    public async Task AcChunk004FrozenColumnsAndChecksumMatchIndependentOneRowFixture()
    {
        SampleRecord[] source = [new("chunk-series", new("golden", DateTimeOffset.UnixEpoch, -0d), 1, "{}")];
        var encoded = SampleChunkCodec.Encode(source, SampleChunkTestData.Budget(), UnitExecutionOptions.TimeSeriesExecution());
        var payload = NativeSerialization.Deserialize<SampleChunkPayload>(encoded);
        await Assert.That(payload.FormatVersion).IsEqualTo(1);
        await Assert.That(payload.RecordCount).IsEqualTo(1);
        await Hex(payload.UtcTicks, "8080d6bddffedfcf08");
        await Hex(payload.Offsets, "0000");
        await Hex(payload.Sequences, "01");
        await Hex(payload.Values, "80808080808080808001");
        await Hex(payload.Series, "186368756e6b2d736572696573");
        await Hex(payload.EventIds, "0c676f6c64656e");
        await Hex(payload.Tags, "01047b7d00");
        await Hex(payload.Checksum, "f7ce9731be33061de1a02375d2b1d0f4149427f3db2fd8ea7364c556a123762f");
        await Assert.That(Convert.ToBase64String(encoded)).IsEqualTo(NativeEnvelope);
        var frozen = Convert.FromBase64String(NativeEnvelope);
        await SampleChunkTestData.AssertRecordsExact(source,
            SampleChunkCodec.Decode(frozen, SampleChunkTestData.ChargedDecodeBudget(frozen), UnitExecutionOptions.TimeSeriesExecution()));
    }

    private static async Task Hex(ReadOnlyMemory<byte> actual, string expected)
        => await Assert.That(Convert.ToHexStringLower(actual.Span)).IsEqualTo(expected);
}
