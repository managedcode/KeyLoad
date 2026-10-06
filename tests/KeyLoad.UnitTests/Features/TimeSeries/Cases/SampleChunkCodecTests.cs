using KeyLoad.Core.Features.TimeSeries;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleChunkCodecTests
{
    [Test]
    public async Task AcChunk001FixedAndSeededRowsRoundTripEveryPersistedFieldExactly()
    {
        var fixedRows = SampleChunkTestData.FixedRecords();
        await RoundTripExact(fixedRows);
        for (var count = 1; count <= SampleChunkTestData.MaximumRecords; count++)
        {
            await RoundTripExact(SampleChunkTestData.SeededRecords(count));
        }
    }

    [Test]
    public async Task AcChunk002TextOffsetsNegativeZeroAndRawFoldRemainExact()
    {
        var expected = SampleChunkTestData.TextRecords();
        var encoded = SampleChunkCodec.Encode(expected, SampleChunkTestData.Budget(), UnitExecutionOptions.TimeSeriesExecution());
        var actual = SampleChunkCodec.Decode(encoded, SampleChunkTestData.ChargedDecodeBudget(encoded), UnitExecutionOptions.TimeSeriesExecution());

        await SampleChunkTestData.AssertRecordsExact(expected, actual);
        await AssertRawFoldSame(expected, actual);
    }

    [Test]
    public async Task AcChunk002UnpairedSurrogateTagsUseExactUtf16Fallback()
    {
        var expected = new[]
        {
            SampleChunkTestData.Row("utf16-tags", DateTimeOffset.UnixEpoch, 1, -0d,
                "{\"raw\":\"\uD800\"}")
        };
        var encoded = SampleChunkCodec.Encode(expected, SampleChunkTestData.Budget(), UnitExecutionOptions.TimeSeriesExecution());
        var payload = SampleChunkTestData.Payload(encoded);
        var decoded = SampleChunkCodec.Decode(encoded, SampleChunkTestData.ChargedDecodeBudget(encoded), UnitExecutionOptions.TimeSeriesExecution());

        await Assert.That((byte)(payload.Tags.Span[1] & 1)).IsEqualTo((byte)1);
        await SampleChunkTestData.AssertRecordsExact(expected, decoded);
    }

    [Test]
    public async Task AcChunk003NativeEnvelopeHonorsExactAndOneByteShortAdmission()
    {
        var expected = SampleChunkTestData.FixedRecords();
        var encoded = SampleChunkCodec.Encode(expected, SampleChunkTestData.Budget(), UnitExecutionOptions.TimeSeriesExecution());
        var exact = SampleChunkCodec.Encode(expected, SampleChunkTestData.Budget(), UnitExecutionOptions.TimeSeriesExecution(), encoded.Length);
        var decoded = SampleChunkCodec.Decode(encoded, SampleChunkTestData.ChargedDecodeBudget(encoded), UnitExecutionOptions.TimeSeriesExecution(), encoded.Length);
        var encodeShort = EncodeFailure(expected, encoded.Length - 1);
        var decodeShort = DecodeFailure(encoded, encoded.Length - 1);

        await Assert.That(exact.SequenceEqual(encoded)).IsTrue();
        await SampleChunkTestData.AssertRecordsExact(expected, decoded);
        await Assert.That(encodeShort.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(decodeShort.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task AcChunk003EmptyAndExcessRecordCountsUseFrozenErrors()
    {
        var empty = EncodeFailure([], SampleChunkTestData.MaximumBytes);
        var excess = EncodeFailure(SampleChunkTestData.SeededRecords(SampleChunkTestData.MaximumRecords + 1),
            SampleChunkTestData.MaximumBytes);

        await Assert.That(empty.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(excess.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task AcChunk003InvalidInputRowsRejectAsValidation()
    {
        var valid = SampleChunkTestData.FixedRecords();
        var mixedSeries = valid.ToArray();
        mixedSeries[1] = mixedSeries[1] with { SeriesId = SampleChunkTestData.OtherSeries };
        var duplicateId = valid.ToArray();
        duplicateId[1] = duplicateId[1] with
        { Sample = duplicateId[1].Sample with { EventId = duplicateId[0].Sample.EventId } };
        var invalidSequence = valid.ToArray();
        invalidSequence[0] = invalidSequence[0] with { Sequence = 0 };
        var nonfiniteValue = valid.ToArray();
        nonfiniteValue[0] = nonfiniteValue[0] with
        { Sample = nonfiniteValue[0].Sample with { Value = double.PositiveInfinity } };
        var outOfOrder = valid.Reverse().ToArray();

        await Assert.That(EncodeFailure(mixedSeries, SampleChunkTestData.MaximumBytes).Code)
            .IsEqualTo(ErrorCode.Validation);
        await Assert.That(EncodeFailure(duplicateId, SampleChunkTestData.MaximumBytes).Code)
            .IsEqualTo(ErrorCode.Validation);
        await Assert.That(EncodeFailure(invalidSequence, SampleChunkTestData.MaximumBytes).Code)
            .IsEqualTo(ErrorCode.Validation);
        await Assert.That(EncodeFailure(nonfiniteValue, SampleChunkTestData.MaximumBytes).Code)
            .IsEqualTo(ErrorCode.Validation);
        await Assert.That(EncodeFailure(outOfOrder, SampleChunkTestData.MaximumBytes).Code)
            .IsEqualTo(ErrorCode.Validation);
    }

    [Test]
    public async Task AcChunk003EmptyAndNullTextFieldsRejectAsValidation()
    {
        var row = SampleChunkTestData.FixedRecords()[0];
        SampleRecord[][] invalidRows =
        [
            [row with { SeriesId = string.Empty }],
            [row with { SeriesId = null! }],
            [row with { Sample = row.Sample with { EventId = string.Empty } }],
            [row with { Sample = row.Sample with { EventId = null! } }],
            [row with { TagsJson = string.Empty }],
            [row with { TagsJson = null! }]
        ];

        foreach (var invalid in invalidRows)
        {
            var failure = EncodeFailure(invalid, SampleChunkTestData.MaximumBytes);
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
        }
    }

    [Test]
    public async Task AcChunk003MaximumByteArgumentIsValidatedBeforeCodecWork()
    {
        var records = SampleChunkTestData.FixedRecords();
        var low = Assert.ThrowsExactly<KeyLoadException>(() => SampleChunkCodec.Encode(records,
            SampleChunkTestData.Budget(), UnitExecutionOptions.TimeSeriesExecution(), 0));
        var high = Assert.ThrowsExactly<KeyLoadException>(() => SampleChunkCodec.Encode(records,
            SampleChunkTestData.Budget(), UnitExecutionOptions.TimeSeriesExecution(), SampleChunkTestData.MaximumBytes + 1));
        var encoded = SampleChunkCodec.Encode(records, SampleChunkTestData.Budget(), UnitExecutionOptions.TimeSeriesExecution());
        var decodeLow = Assert.ThrowsExactly<KeyLoadException>(() => SampleChunkCodec.Decode(encoded,
            SampleChunkTestData.ChargedDecodeBudget(encoded), UnitExecutionOptions.TimeSeriesExecution(), 0));
        var decodeHigh = Assert.ThrowsExactly<KeyLoadException>(() => SampleChunkCodec.Decode(encoded,
            SampleChunkTestData.ChargedDecodeBudget(encoded), UnitExecutionOptions.TimeSeriesExecution(), SampleChunkTestData.MaximumBytes + 1));

        await Assert.That(low.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(high.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(decodeLow.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(decodeHigh.Code).IsEqualTo(ErrorCode.Validation);
    }

    [Test]
    public async Task AcChunk003CallerReadBudgetRejectsBeforeDecodeAndFollowingBudgetCanSucceed()
    {
        var records = SampleChunkTestData.FixedRecords();
        var encoded = SampleChunkCodec.Encode(records, SampleChunkTestData.Budget(), UnitExecutionOptions.TimeSeriesExecution());
        var limits = new DatabaseLimits { MaxQueryReadBytes = encoded.Length - 1 };
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            SampleChunkTestData.ChargedDecodeBudget(encoded, limits));
        var actual = SampleChunkCodec.Decode(encoded, SampleChunkTestData.ChargedDecodeBudget(encoded), UnitExecutionOptions.TimeSeriesExecution());

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await SampleChunkTestData.AssertRecordsExact(records, actual);
    }

    [Test]
    public async Task AcChunk003CancellationAndElapsedDeadlineFailWholeOperation()
    {
        var records = SampleChunkTestData.FixedRecords();
        using var cancellation = new CancellationTokenSource();
        var encoded = SampleChunkCodec.Encode(records, SampleChunkTestData.Budget(), UnitExecutionOptions.TimeSeriesExecution());
        var decodeBudget = SampleChunkTestData.ChargedDecodeBudget(encoded, cancellationToken: cancellation.Token);
        await cancellation.CancelAsync();
        var encodeCancelled = Assert.ThrowsExactly<OperationCanceledException>(() =>
            SampleChunkCodec.Encode(records, SampleChunkTestData.Budget(cancellationToken: cancellation.Token), UnitExecutionOptions.TimeSeriesExecution()));
        var decodeCancelled = Assert.ThrowsExactly<OperationCanceledException>(() =>
            SampleChunkCodec.Decode(encoded, decodeBudget, UnitExecutionOptions.TimeSeriesExecution()));
        var deadlineLimits = new DatabaseLimits { QueryDeadlineSeconds = 1 };
        var deadlineBudget = SampleChunkTestData.Budget(deadlineLimits);
        var decodeDeadlineBudget = SampleChunkTestData.ChargedDecodeBudget(encoded, deadlineLimits);
        await Task.Delay(TimeSpan.FromMilliseconds(1_100), TimeProvider.System);
        var encodeDeadline = Assert.ThrowsExactly<KeyLoadException>(() => SampleChunkCodec.Encode(records, deadlineBudget, UnitExecutionOptions.TimeSeriesExecution()));
        var decodeDeadline = Assert.ThrowsExactly<KeyLoadException>(() => SampleChunkCodec.Decode(encoded, decodeDeadlineBudget, UnitExecutionOptions.TimeSeriesExecution()));

        await Assert.That(encodeCancelled.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(decodeCancelled.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(encodeDeadline.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(decodeDeadline.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    private static async Task RoundTripExact(SampleRecord[] expected)
    {
        var encoded = SampleChunkCodec.Encode(expected, SampleChunkTestData.Budget(), UnitExecutionOptions.TimeSeriesExecution());
        var actual = SampleChunkCodec.Decode(encoded, SampleChunkTestData.ChargedDecodeBudget(encoded), UnitExecutionOptions.TimeSeriesExecution());
        await SampleChunkTestData.AssertRecordsExact(expected, actual);
    }

    private static async Task AssertRawFoldSame(SampleRecord[] expected, SampleRecord[] actual)
    {
        var expectedFold = RawFold(expected);
        var actualFold = RawFold(actual);
        await Assert.That(actualFold.Count).IsEqualTo(expectedFold.Count);
        await Assert.That(BitConverter.DoubleToInt64Bits(actualFold.Sum))
            .IsEqualTo(BitConverter.DoubleToInt64Bits(expectedFold.Sum));
        await Assert.That(actualFold.Minimum).IsEqualTo(expectedFold.Minimum);
        await Assert.That(actualFold.Maximum).IsEqualTo(expectedFold.Maximum);
    }

    private static (long Count, double Sum, double? Minimum, double? Maximum) RawFold(SampleRecord[] records)
    {
        long count = 0;
        var sum = 0d;
        double? minimum = null;
        double? maximum = null;
        foreach (var record in records)
        {
            count++;
            sum += record.Sample.Value;
            minimum = minimum is null ? record.Sample.Value : Math.Min(minimum.Value, record.Sample.Value);
            maximum = maximum is null ? record.Sample.Value : Math.Max(maximum.Value, record.Sample.Value);
        }
        return (count, sum, minimum, maximum);
    }

    private static KeyLoadException EncodeFailure(SampleRecord[] records, int maximumBytes)
        => Assert.ThrowsExactly<KeyLoadException>(() => SampleChunkCodec.Encode(records,
            SampleChunkTestData.Budget(), UnitExecutionOptions.TimeSeriesExecution(), maximumBytes));

    private static KeyLoadException DecodeFailure(byte[] encoded, int maximumBytes)
        => Assert.ThrowsExactly<KeyLoadException>(() => SampleChunkCodec.Decode(encoded,
            SampleChunkTestData.ChargedDecodeBudget(encoded), UnitExecutionOptions.TimeSeriesExecution(), maximumBytes));
}
