using KeyLoad.Core.TimeSeries;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal sealed class SampleChunkGoldenTests
{
    private const string RuntimeEnvelope = "IDEBJsF/2j9rZXlsb2FkLmNvcmUuU2FtcGxlQ2h1bmtQYXlsb2Fk6AEFQROAgNa93/7fzwhBBQAAQQMBQRWAgICAgICAgIABQRsYY2h1bmstc2VyaWVzQQ8MZ29sZGVuQQsBBHt9AEFB0ItAtXVCDa8mmZFojIL3ykXhzEWVaf7Me9AlYZLueevg4A==";

    [Test]
    public async Task AcChunk004FrozenColumnsAndChecksumMatchIndependentOneRowFixture()
    {
        SampleRecord[] source = [new("chunk-series", new("golden", DateTimeOffset.UnixEpoch, -0d), 1, "{}")];
        var encoded = SampleChunkCodec.Encode(source, SampleChunkTestData.Budget(), UnitExecutionOptions.TimeSeriesExecution());
        var payload = OrleansSerialization.Deserialize<SampleChunkPayload>(encoded);
        await Assert.That(payload.RecordCount).IsEqualTo(1);
        await Hex(payload.UtcTicks, "8080d6bddffedfcf08");
        await Hex(payload.Offsets, "0000");
        await Hex(payload.Sequences, "01");
        await Hex(payload.Values, "80808080808080808001");
        await Hex(payload.Series, "186368756e6b2d736572696573");
        await Hex(payload.EventIds, "0c676f6c64656e");
        await Hex(payload.Tags, "01047b7d00");
        await Hex(payload.Checksum, "d08b40b575420daf269991688c82f7ca45e1cc459569fecc7bd0256192ee79eb");
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

    private static SampleChunkPayload ExpectedPayload() => new(1,
        Convert.FromHexString("8080d6bddffedfcf08"),
        Convert.FromHexString("0000"),
        Convert.FromHexString("01"),
        Convert.FromHexString("80808080808080808001"),
        Convert.FromHexString("186368756e6b2d736572696573"),
        Convert.FromHexString("0c676f6c64656e"),
        Convert.FromHexString("01047b7d00"),
        Convert.FromHexString("d08b40b575420daf269991688c82f7ca45e1cc459569fecc7bd0256192ee79eb"));
}
