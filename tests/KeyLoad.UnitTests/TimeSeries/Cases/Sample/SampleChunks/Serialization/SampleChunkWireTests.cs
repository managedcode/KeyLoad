using KeyLoad.UnitTests.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleChunkWireTests
{
    private const string ExpectedUnsupportedVersionMessage = "The sample chunk format is unsupported.";

    [Test]
    public async Task AcChunk004ChecksumMismatchAndUnsupportedChunkVersionHaveExactErrors()
    {
        var payload = SampleChunkTestData.Payload(SampleChunkWireCodecAssertions.Encoded(SampleChunkTestData.FixedRecords()));
        var badChecksum = payload.Checksum.ToArray();
        badChecksum[0] ^= 1;
        var checksumFailure = SampleChunkWireCodecAssertions.DecodeFailure(SampleChunkTestData.Serialize(payload with
        {
            Checksum = badChecksum
        }));
        var unknownPayload = payload with
        {
            FormatVersion = payload.FormatVersion + 1
        };
        var versionFailure = SampleChunkWireCodecAssertions.DecodeFailure(SampleChunkTestData.Rechecksum(unknownPayload));

        await Assert.That(checksumFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(versionFailure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(versionFailure.Message).IsEqualTo(ExpectedUnsupportedVersionMessage);
    }

    [Test]
    public async Task AcChunk004EnvelopeTruncationTrailingBytesAndUnknownVersionFailClosed()
    {
        var encoded = SampleChunkWireCodecAssertions.Encoded(SampleChunkTestData.FixedRecords());
        var truncated = encoded[..^1];
        var trailing = encoded.Append((byte)0).ToArray();
        using var serializer = new SerializerFixture();
        var payload = SampleChunkTestData.Payload(encoded);
        var unknownVersion = serializer.Encode(payload, PayloadVersion.Current + 1);
        var truncatedFailure = SampleChunkWireCodecAssertions.DecodeFailure(truncated);
        var trailingFailure = SampleChunkWireCodecAssertions.DecodeFailure(trailing);
        var versionFailure = SampleChunkWireCodecAssertions.DecodeFailure(unknownVersion);

        await Assert.That(truncatedFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(trailingFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(versionFailure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
    }

    [Test]
    public async Task AcChunk004InvalidTimestampOffsetSequenceAndValueRejectWithValidChecksum()
    {
        var single = SampleChunkTestData.Row("wire-one", DateTimeOffset.UnixEpoch, 1, 1d, "{}");
        var onePayload = SampleChunkTestData.Payload(SampleChunkWireCodecAssertions.Encoded([single]));
        var badOffset = onePayload with
        {
            Offsets = SampleChunkWireData.Memory([0x49, 0x03])
        };
        var badOffsetShape = onePayload with
        {
            Offsets = SampleChunkWireData.Memory([0])
        };
        var beyondUtcRange = SampleChunkWireData.Uleb((ulong)DateTimeOffset.MaxValue.UtcTicks + 1);
        var badTimestamp = onePayload with
        {
            UtcTicks = beyondUtcRange
        };
        var offsetFailure = SampleChunkWireCodecAssertions.DecodeFailure(SampleChunkTestData.Rechecksum(badOffset));
        var offsetShapeFailure = SampleChunkWireCodecAssertions.DecodeFailure(SampleChunkTestData.Rechecksum(badOffsetShape));
        var timestampFailure = SampleChunkWireCodecAssertions.DecodeFailure(SampleChunkTestData.Rechecksum(badTimestamp));

        var pair = SampleChunkTestData.SeededRecords(2);
        var pairPayload = SampleChunkTestData.Payload(SampleChunkWireCodecAssertions.Encoded(pair));
        var overflowSequences = new byte[10];
        Array.Fill(overflowSequences, byte.MaxValue, 0, 8);
        overflowSequences[8] = 0x7F;
        overflowSequences[9] = 0x02;
        var sequenceFailure = SampleChunkWireCodecAssertions.DecodeFailure(SampleChunkTestData.Rechecksum(pairPayload with
        {
            Sequences = overflowSequences
        }));
        var infinity = SampleChunkWireData.Uleb(0x7FF0_0000_0000_0000UL);
        var valueFailure = SampleChunkWireCodecAssertions.DecodeFailure(SampleChunkTestData.Rechecksum(onePayload with
        {
            Values = infinity
        }));

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
        var payload = SampleChunkTestData.Payload(SampleChunkWireCodecAssertions.Encoded([single]));
        var nonminimal = payload with
        {
            UtcTicks = SampleChunkWireData.Memory(SampleChunkWireData.MakeNonMinimal(payload.UtcTicks.Span))
        };
        var nonminimalFailure = SampleChunkWireCodecAssertions.DecodeFailure(SampleChunkTestData.Rechecksum(nonminimal));

        var decreasing = new[]
        {
            SampleChunkTestData.Row("wire-first", DateTimeOffset.UnixEpoch, 2, 1d, "{}"),
            SampleChunkTestData.Row("wire-second", DateTimeOffset.UnixEpoch.AddTicks(1), 1, 2d, "{}")
        };
        var orderedPayload = SampleChunkTestData.Payload(SampleChunkWireCodecAssertions.Encoded(decreasing));
        var equalTicks = SampleChunkWireData.Uleb((ulong)DateTimeOffset.UnixEpoch.UtcTicks).Append((byte)0).ToArray();
        var equalTimestamps = orderedPayload with
        {
            UtcTicks = SampleChunkWireData.Memory(equalTicks)
        };
        var orderFailure = SampleChunkWireCodecAssertions.DecodeFailure(SampleChunkTestData.Rechecksum(equalTimestamps));

        await Assert.That(nonminimalFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(orderFailure.Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcChunk004InvalidUtf8AndNoncanonicalUtf16RejectWithValidChecksum()
    {
        var payload = SampleChunkTestData.Payload(SampleChunkWireCodecAssertions.Encoded(
            [SampleChunkTestData.Row("wire-text", DateTimeOffset.UnixEpoch, 1, 1d, "{}")]));
        var invalidUtf8 = payload with
        {
            Series = SampleChunkWireData.Memory([0x02, 0xFF])
        };
        var unnecessaryUtf16 = payload with
        {
            Series = SampleChunkWireData.Memory([0x05, 0x61, 0x62])
        };
        var invalidFailure = SampleChunkWireCodecAssertions.DecodeFailure(SampleChunkTestData.Rechecksum(invalidUtf8));
        var noncanonicalFailure = SampleChunkWireCodecAssertions.DecodeFailure(SampleChunkTestData.Rechecksum(unnecessaryUtf16));

        await Assert.That(invalidFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(noncanonicalFailure.Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcChunk004EmptySeriesEventAndTagTextRejectWithValidChecksum()
    {
        var row = SampleChunkTestData.Row("wire-empty", DateTimeOffset.UnixEpoch, 1, 1d, "{}");
        var payload = SampleChunkTestData.Payload(SampleChunkWireCodecAssertions.Encoded([row]));
        var emptySeries = payload with
        {
            Series = SampleChunkWireData.Memory([0])
        };
        var emptyEvent = payload with
        {
            EventIds = SampleChunkWireData.Memory([0])
        };
        var emptyTag = payload with
        {
            Tags = SampleChunkWireData.Memory([1, 0, 0])
        };
        var seriesFailure = SampleChunkWireCodecAssertions.DecodeFailure(SampleChunkTestData.Rechecksum(emptySeries));
        var eventFailure = SampleChunkWireCodecAssertions.DecodeFailure(SampleChunkTestData.Rechecksum(emptyEvent));
        var tagFailure = SampleChunkWireCodecAssertions.DecodeFailure(SampleChunkTestData.Rechecksum(emptyTag));

        await Assert.That(seriesFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(eventFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(tagFailure.Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcChunk004UnreferencedTagDictionaryEntryAndColumnTrailingBytesReject()
    {
        var unreferencedTags = SampleChunkWireData.TagDictionaryWithUnreferencedEntry();
        var pair = SampleChunkTestData.SeededRecords(2);
        var pairPayload = SampleChunkTestData.Payload(SampleChunkWireCodecAssertions.Encoded(pair));
        var badDictionary = pairPayload with
        {
            Tags = unreferencedTags
        };
        var duplicateDictionary = pairPayload with
        {
            Tags = SampleChunkWireData.TagDictionaryWithDuplicates()
        };
        var outOfOrderDictionary = pairPayload with
        {
            Tags = SampleChunkWireData.TagDictionaryWithOutOfOrderIndexes()
        };
        var extraSeries = pairPayload.Series.ToArray().Append((byte)0).ToArray();
        var extraSeriesByte = pairPayload with
        {
            Series = SampleChunkWireData.Memory(extraSeries)
        };
        var truncatedSeries = pairPayload with
        {
            Series = pairPayload.Series[..^1]
        };
        var invalidIndexTags = SampleChunkWireData.TagDictionaryWithInvalidIndexes();
        var invalidIndex = pairPayload with
        {
            Tags = invalidIndexTags
        };
        var badCount = pairPayload with
        {
            RecordCount = pairPayload.RecordCount - 1
        };
        var dictionaryFailure = SampleChunkWireCodecAssertions.DecodeFailure(SampleChunkTestData.Rechecksum(badDictionary));
        var duplicateFailure = SampleChunkWireCodecAssertions.DecodeFailure(SampleChunkTestData.Rechecksum(duplicateDictionary));
        var dictionaryOrderFailure = SampleChunkWireCodecAssertions.DecodeFailure(SampleChunkTestData.Rechecksum(outOfOrderDictionary));
        var trailingFailure = SampleChunkWireCodecAssertions.DecodeFailure(SampleChunkTestData.Rechecksum(extraSeriesByte));
        var truncatedFailure = SampleChunkWireCodecAssertions.DecodeFailure(SampleChunkTestData.Rechecksum(truncatedSeries));
        var indexFailure = SampleChunkWireCodecAssertions.DecodeFailure(SampleChunkTestData.Rechecksum(invalidIndex));
        var countFailure = SampleChunkWireCodecAssertions.DecodeFailure(SampleChunkTestData.Rechecksum(badCount));

        await Assert.That(dictionaryFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(duplicateFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(dictionaryOrderFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(trailingFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(truncatedFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(indexFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(countFailure.Code).IsEqualTo(ErrorCode.Corruption);
    }

}
