using KeyLoad.Core.TimeSeries;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal sealed class SampleChunkGoldenTests
{
    private const string RuntimeEnvelope = "IAADMQEmwX/aP2tleWxvYWQuY29yZS5TYW1wbGVDaHVua1BheWxvYWToAAUBBUETgIDWvd/+388IQQUAAEEDAUEVgICAgICAgICAAUEbGGNodW5rLXNlcmllc0EPDGdvbGRlbkELAQR7fQBBQb1ScZJbh+wrT8rOoPPFZwYGTt4efxLF+WkeJ2N1Ln0W4OA=";

    [Test]
    public async Task AcChunk004FrozenColumnsAndChecksumMatchIndependentOneRowFixture()
    {
        SampleRecord[] source = [new("chunk-series", new("golden", DateTimeOffset.UnixEpoch, -0d), 1, "{}")];
        var encoded = SampleChunkCodec.Encode(source, SampleChunkTestData.Budget(), UnitExecutionOptions.TimeSeriesExecution());
        var payload = OrleansSerialization.Deserialize<SampleChunkPayload>(encoded);
        await Assert.That(payload.FormatVersion).IsEqualTo(1);
        await Assert.That(payload.RecordCount).IsEqualTo(1);
        await Hex(payload.UtcTicks, "8080d6bddffedfcf08");
        await Hex(payload.Offsets, "0000");
        await Hex(payload.Sequences, "01");
        await Hex(payload.Values, "80808080808080808001");
        await Hex(payload.Series, "186368756e6b2d736572696573");
        await Hex(payload.EventIds, "0c676f6c64656e");
        await Hex(payload.Tags, "01047b7d00");
        await Hex(payload.Checksum, "bd5271925b87ec2b4fcacea0f3c56706064ede1e7f12c5f9691e2763752e7d16");
        var independent = OrleansSerialization.Serialize(ExpectedPayload());
        await Assert.That(Convert.ToBase64String(encoded)).IsEqualTo(Convert.ToBase64String(independent));
        await Assert.That(Convert.ToBase64String(independent)).IsEqualTo(RuntimeEnvelope);
        await Assert.That(Convert.ToBase64String(encoded)).IsEqualTo(RuntimeEnvelope);
        var frozen = Convert.FromBase64String(RuntimeEnvelope);
        await SampleChunkTestData.AssertRecordsExact(source,
            SampleChunkCodec.Decode(frozen, SampleChunkTestData.Budget(), UnitExecutionOptions.TimeSeriesExecution()));
    }

    private static async Task Hex(ReadOnlyMemory<byte> actual, string expected)
        => await Assert.That(Convert.ToHexStringLower(actual.Span)).IsEqualTo(expected);

    private static SampleChunkPayload ExpectedPayload() => new(1, 1,
        Convert.FromHexString("8080d6bddffedfcf08"),
        Convert.FromHexString("0000"),
        Convert.FromHexString("01"),
        Convert.FromHexString("80808080808080808001"),
        Convert.FromHexString("186368756e6b2d736572696573"),
        Convert.FromHexString("0c676f6c64656e"),
        Convert.FromHexString("01047b7d00"),
        Convert.FromHexString("bd5271925b87ec2b4fcacea0f3c56706064ede1e7f12c5f9691e2763752e7d16"));
}
