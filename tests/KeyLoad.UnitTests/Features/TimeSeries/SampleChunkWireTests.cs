using KeyLoad.Core.Features.TimeSeries;
using KeyLoad.Features.InternalSerialization;
using KeyLoad.UnitTests.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleChunkWireTests
{
    private const string ExpectedUnsupportedVersionMessage = "The sample chunk format is unsupported.";

    [Test]
    public async Task AcChunk004ChecksumMismatchAndUnsupportedChunkVersionHaveExactErrors()
    {
        var payload = SampleChunkTestData.Payload(Encoded(SampleChunkTestData.FixedRecords()));
        var badChecksum = payload.Checksum.ToArray();
        badChecksum[0] ^= 1;
        var checksumFailure = DecodeFailure(SampleChunkTestData.Serialize(payload with { Checksum = badChecksum }));
        var unknownPayload = payload with { FormatVersion = payload.FormatVersion + 1 };
        var versionFailure = DecodeFailure(SampleChunkTestData.Rechecksum(unknownPayload));

        await Assert.That(checksumFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(versionFailure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(versionFailure.Message).IsEqualTo(ExpectedUnsupportedVersionMessage);
    }

    [Test]
    public async Task AcChunk004NativeEnvelopeTruncationTrailingBytesAndUnknownVersionFailClosed()
    {
        var encoded = Encoded(SampleChunkTestData.FixedRecords());
        var truncated = encoded[..^1];
        var trailing = encoded.Append((byte)0).ToArray();
        using var serializer = new NativeSerializerFixture();
        var payload = SampleChunkTestData.Payload(encoded);
        var unknownVersion = serializer.Encode(payload, NativePayloadVersion.Current + 1);
        var truncatedFailure = DecodeFailure(truncated);
        var trailingFailure = DecodeFailure(trailing);
        var versionFailure = DecodeFailure(unknownVersion);

        await Assert.That(truncatedFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(trailingFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(versionFailure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
    }

    [Test]
    public async Task AcChunk004InvalidTimestampOffsetSequenceAndValueRejectWithValidChecksum()
    {
        var single = SampleChunkTestData.Row("wire-one", DateTimeOffset.UnixEpoch, 1, 1d, "{}");
        var onePayload = SampleChunkTestData.Payload(Encoded([single]));
        var badOffset = onePayload with { Offsets = SampleChunkWireData.Memory([0x49, 0x03]) };
        var badOffsetShape = onePayload with { Offsets = SampleChunkWireData.Memory([0]) };
        var beyondUtcRange = SampleChunkWireData.Uleb((ulong)DateTimeOffset.MaxValue.UtcTicks + 1);
        var badTimestamp = onePayload with { UtcTicks = beyondUtcRange };
        var offsetFailure = DecodeFailure(SampleChunkTestData.Rechecksum(badOffset));
        var offsetShapeFailure = DecodeFailure(SampleChunkTestData.Rechecksum(badOffsetShape));
        var timestampFailure = DecodeFailure(SampleChunkTestData.Rechecksum(badTimestamp));

        var pair = SampleChunkTestData.SeededRecords(2);
        var pairPayload = SampleChunkTestData.Payload(Encoded(pair));
        var overflowSequences = new byte[10];
        Array.Fill(overflowSequences, byte.MaxValue, 0, 8);
        overflowSequences[8] = 0x7F;
        overflowSequences[9] = 0x02;
        var sequenceFailure = DecodeFailure(SampleChunkTestData.Rechecksum(pairPayload with
        { Sequences = overflowSequences }));
        var infinity = SampleChunkWireData.Uleb(0x7FF0_0000_0000_0000UL);
        var valueFailure = DecodeFailure(SampleChunkTestData.Rechecksum(onePayload with { Values = infinity }));

        await Assert.That(offsetFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(offsetShapeFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(timestampFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(sequenceFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(valueFailure.Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcChunk004NonMinimalVarintAndTupleOrderRejectWithValidChecksum()
    {
        var single = SampleChunkTestData.Row("wire-varint", DateTimeOffset.UnixEpoch, 1, 1d, "{}");
        var payload = SampleChunkTestData.Payload(Encoded([single]));
        var nonminimal = payload with
        {
            UtcTicks = SampleChunkWireData.Memory(SampleChunkWireData.MakeNonMinimal(payload.UtcTicks.Span))
        };
        var nonminimalFailure = DecodeFailure(SampleChunkTestData.Rechecksum(nonminimal));

        var decreasing = new[]
        {
            SampleChunkTestData.Row("wire-first", DateTimeOffset.UnixEpoch, 2, 1d, "{}"),
            SampleChunkTestData.Row("wire-second", DateTimeOffset.UnixEpoch.AddTicks(1), 1, 2d, "{}")
        };
        var orderedPayload = SampleChunkTestData.Payload(Encoded(decreasing));
        var equalTicks = SampleChunkWireData.Uleb((ulong)DateTimeOffset.UnixEpoch.UtcTicks).Append((byte)0).ToArray();
        var equalTimestamps = orderedPayload with { UtcTicks = SampleChunkWireData.Memory(equalTicks) };
        var orderFailure = DecodeFailure(SampleChunkTestData.Rechecksum(equalTimestamps));

        await Assert.That(nonminimalFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(orderFailure.Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcChunk004InvalidUtf8AndUnneededUtf16FallbackRejectWithValidChecksum()
    {
        var payload = SampleChunkTestData.Payload(Encoded(
            [SampleChunkTestData.Row("wire-text", DateTimeOffset.UnixEpoch, 1, 1d, "{}")]));
        var invalidUtf8 = payload with { Series = SampleChunkWireData.Memory([0x02, 0xFF]) };
        var unnecessaryUtf16 = payload with { Series = SampleChunkWireData.Memory([0x05, 0x61, 0x62]) };
        var invalidFailure = DecodeFailure(SampleChunkTestData.Rechecksum(invalidUtf8));
        var fallbackFailure = DecodeFailure(SampleChunkTestData.Rechecksum(unnecessaryUtf16));

        await Assert.That(invalidFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(fallbackFailure.Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcChunk004EmptySeriesEventAndTagTextRejectWithValidChecksum()
    {
        var row = SampleChunkTestData.Row("wire-empty", DateTimeOffset.UnixEpoch, 1, 1d, "{}");
        var payload = SampleChunkTestData.Payload(Encoded([row]));
        var emptySeries = payload with { Series = SampleChunkWireData.Memory([0]) };
        var emptyEvent = payload with { EventIds = SampleChunkWireData.Memory([0]) };
        var emptyTag = payload with { Tags = SampleChunkWireData.Memory([1, 0, 0]) };
        var seriesFailure = DecodeFailure(SampleChunkTestData.Rechecksum(emptySeries));
        var eventFailure = DecodeFailure(SampleChunkTestData.Rechecksum(emptyEvent));
        var tagFailure = DecodeFailure(SampleChunkTestData.Rechecksum(emptyTag));

        await Assert.That(seriesFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(eventFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(tagFailure.Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcChunk004UnreferencedTagDictionaryEntryAndColumnTrailingBytesReject()
    {
        var unreferencedTags = SampleChunkWireData.TagDictionaryWithUnreferencedEntry();
        var pair = SampleChunkTestData.SeededRecords(2);
        var pairPayload = SampleChunkTestData.Payload(Encoded(pair));
        var badDictionary = pairPayload with { Tags = unreferencedTags };
        var duplicateDictionary = pairPayload with { Tags = SampleChunkWireData.TagDictionaryWithDuplicates() };
        var outOfOrderDictionary = pairPayload with { Tags = SampleChunkWireData.TagDictionaryWithOutOfOrderIndexes() };
        var extraSeries = pairPayload.Series.ToArray().Append((byte)0).ToArray();
        var extraSeriesByte = pairPayload with { Series = SampleChunkWireData.Memory(extraSeries) };
        var truncatedSeries = pairPayload with { Series = pairPayload.Series[..^1] };
        var invalidIndexTags = SampleChunkWireData.TagDictionaryWithInvalidIndexes();
        var invalidIndex = pairPayload with { Tags = invalidIndexTags };
        var badCount = pairPayload with { RecordCount = pairPayload.RecordCount - 1 };
        var dictionaryFailure = DecodeFailure(SampleChunkTestData.Rechecksum(badDictionary));
        var duplicateFailure = DecodeFailure(SampleChunkTestData.Rechecksum(duplicateDictionary));
        var dictionaryOrderFailure = DecodeFailure(SampleChunkTestData.Rechecksum(outOfOrderDictionary));
        var trailingFailure = DecodeFailure(SampleChunkTestData.Rechecksum(extraSeriesByte));
        var truncatedFailure = DecodeFailure(SampleChunkTestData.Rechecksum(truncatedSeries));
        var indexFailure = DecodeFailure(SampleChunkTestData.Rechecksum(invalidIndex));
        var countFailure = DecodeFailure(SampleChunkTestData.Rechecksum(badCount));

        await Assert.That(dictionaryFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(duplicateFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(dictionaryOrderFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(trailingFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(truncatedFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(indexFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(countFailure.Code).IsEqualTo(ErrorCode.Corruption);
    }

    private static byte[] Encoded(SampleRecord[] records)
        => SampleChunkCodec.Encode(records, SampleChunkTestData.Budget());

    private static KeyLoadException DecodeFailure(byte[] bytes)
        => Assert.ThrowsExactly<KeyLoadException>(() => SampleChunkCodec.Decode(bytes,
            SampleChunkTestData.ChargedDecodeBudget(bytes)));

}
