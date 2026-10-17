using KeyLoad.Core;
using KeyLoad.Core.Features.TimeSeries;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.TimeSeries;

/// <summary>AC-CQ-034/AC-CHUNK-001..004: native slice policy preserves the frozen chunk contract.</summary>
internal sealed class SampleChunkExecutionPolicyTests
{
    [Test]
    [Arguments(1, 1)]
    [Arguments(3, 2)]
    [Arguments(1024, 1024)]
    [Arguments(65_536, 16_383)]
    public async Task ConfiguredSlicesPreserveNativeEnvelopeChecksumAndEveryDecodedFieldAsync(
        int hashChunkBytes, int textCancellationCheckIntervalCodeUnits)
    {
        var options = UnitExecutionOptions.TimeSeriesExecution(new TimeSeriesExecutionOptions
        {
            HashChunkBytes = hashChunkBytes,
            TextCancellationCheckIntervalCodeUnits = textCancellationCheckIntervalCodeUnits
        });
        foreach (var records in new[]
        {
            LargeTextRecords(false), LargeTextRecords(true), SampleChunkTestData.TextRecords(), SampleChunkTestData.FixedRecords()
        })
        {
            var original = SampleChunkCodec.Encode(records, SampleChunkTestData.Budget(), UnitExecutionOptions.TimeSeriesExecution());
            var encoded = SampleChunkCodec.Encode(records, SampleChunkTestData.Budget(), options);
            var decoded = SampleChunkCodec.Decode(encoded, SampleChunkTestData.ChargedDecodeBudget(encoded), options);
            var payload = SampleChunkTestData.Payload(encoded);
            await Assert.That(encoded.SequenceEqual(original)).IsTrue();
            await Assert.That(SampleChunkTestData.Rechecksum(payload).SequenceEqual(encoded)).IsTrue();
            await SampleChunkTestData.AssertRecordsExact(records, decoded);
        }
    }

    [Test]
    [Arguments(0, 16_383)]
    [Arguments(-1, 16_383)]
    [Arguments(65_537, 16_383)]
    [Arguments(65_536, 0)]
    [Arguments(65_536, -1)]
    [Arguments(65_536, 16_384)]
    public async Task InvalidSliceRejectsBeforeMalformedCodecInputAndPreservesBudgetAsync(
        int hashChunkBytes, int textCancellationCheckIntervalCodeUnits)
    {
        var options = Options.Create(new TimeSeriesExecutionOptions
        {
            HashChunkBytes = hashChunkBytes,
            TextCancellationCheckIntervalCodeUnits = textCancellationCheckIntervalCodeUnits
        });
        var encodeBudget = SampleChunkTestData.Budget();
        var decodeBudget = SampleChunkTestData.Budget();
        var encodeFailure = Assert.ThrowsExactly<InvalidOperationException>(
            () => SampleChunkCodec.Encode([], encodeBudget, options));
        var decodeFailure = Assert.ThrowsExactly<InvalidOperationException>(
            () => SampleChunkCodec.Decode([], decodeBudget, options));
        await Assert.That(encodeFailure.Message).IsEqualTo(TimeSeriesExecutionOptions.ValidationMessage);
        await Assert.That(decodeFailure.Message).IsEqualTo(TimeSeriesExecutionOptions.ValidationMessage);
        await Assert.That(encodeBudget.ReadBytes).IsEqualTo(0L);
        await Assert.That(decodeBudget.ReadBytes).IsEqualTo(0L);
        var records = SampleChunkTestData.FixedRecords();
        var encoded = SampleChunkCodec.Encode(records, encodeBudget, UnitExecutionOptions.TimeSeriesExecution());
        decodeBudget.ChargeBytes(encoded.Length);
        await SampleChunkTestData.AssertRecordsExact(records,
            SampleChunkCodec.Decode(encoded, decodeBudget, UnitExecutionOptions.TimeSeriesExecution()));
    }

    [Test]
    public async Task ConfiguredSingleByteSlicesRetainCallerCancellationAndFollowingWholeRoundtripAsync()
    {
        var records = LargeTextRecords(true);
        var options = UnitExecutionOptions.TimeSeriesExecution(new TimeSeriesExecutionOptions
        {
            HashChunkBytes = 1,
            TextCancellationCheckIntervalCodeUnits = 1
        });
        var encoded = SampleChunkCodec.Encode(records, SampleChunkTestData.Budget(), options);
        using var cancellation = new CancellationTokenSource();
        var cancelledEncodeBudget = SampleChunkTestData.Budget(cancellationToken: cancellation.Token);
        var cancelledBudget = SampleChunkTestData.ChargedDecodeBudget(encoded, cancellationToken: cancellation.Token);
        await cancellation.CancelAsync();
        var encodeFailure = Assert.ThrowsExactly<OperationCanceledException>(() => SampleChunkCodec.Encode(records,
            cancelledEncodeBudget, options));
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() => SampleChunkCodec.Decode(encoded,
            cancelledBudget, options));
        await Assert.That(encodeFailure.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
        var decoded = SampleChunkCodec.Decode(encoded, SampleChunkTestData.ChargedDecodeBudget(encoded), options);
        await SampleChunkTestData.AssertRecordsExact(records, decoded);
    }

    private static SampleRecord[] LargeTextRecords(bool unpaired)
    {
        var suffix = unpaired ? "\uD800" : "é🐈";
        var eventId = new string('x', 70_000) + suffix;
        var tags = "{\"text\":\"" + new string('猫', 30_000) + suffix + "\"}";
        return [SampleChunkTestData.Row(eventId, DateTimeOffset.UnixEpoch, 1, -0d, tags)];
    }
}
