using System.Text.Json;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;
using TestKeys = KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive.TimeSeriesIntensiveRunJsonTestKeys;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveRunJsonValidationTests
{
    private const string UnchangedValue = "unchanged";
    private const string WriterChangedBeforeValidation = "Invalid DTO changed the borrowed writer before validation completed.";
    private const string CallerStructureChanged = "Invalid DTO altered the caller's JSON structure.";

    [Test]
    public async Task AcTj009003InvalidScenarioFrequencyAndSlotCountLeaveOutputUnchanged()
    {
        var original = TimeSeriesIntensiveRunJsonTestData.Create();
        Reject(original with { Scenario = (TimeSeriesIntensiveScenario)int.MaxValue });
        Reject(original with { TimestampFrequency = 0 });
        Reject(original with { TimestampFrequency = -1 });
        Reject(original with { Attempts = original.Attempts[..49999] });
        Reject(original with { Attempts = new TimeSeriesIntensiveAttempt[50001] });
        Reject(original with { Repetitions = default });
        Reject(original with { Repetitions = [null!] });
        await Assert.That(original.Attempts.Length).IsEqualTo(50000);
    }

    [Test]
    public async Task AcTj009003InvalidFinalSlotAndObservedTicksLeaveOutputUnchanged()
    {
        var original = TimeSeriesIntensiveRunJsonTestData.Create();
        var storage = original.Attempts.ToArray();
        var planned = storage[49999];
        foreach (var invalid in new[] { planned with { Repetition = 0 }, planned with { Index = 0 }, planned with { Worker = 0 },
            planned with { Outcome = (TimeSeriesIntensiveOutcome)int.MaxValue },
            planned with { Outcome = TimeSeriesIntensiveOutcome.Cancelled, LatencyTicks = -1 },
            planned with { Outcome = TimeSeriesIntensiveOutcome.Cancelled, ValidationTicks = -1 } })
        {
            storage[49999] = invalid;
            Reject(original with { Attempts = storage });
        }

        storage[49999] = planned;
        await Assert.That(storage[49999]).IsEqualTo(planned);
    }

    [Test]
    public async Task AcTj009003UndefinedStageOutcomeAndFailureEnumsLeaveOutputUnchanged()
    {
        var original = TimeSeriesIntensiveRunJsonTestData.Create();
        var valid = new TimeSeriesIntensiveRunFailure(TimeSeriesIntensiveRunStage.Seed, 0,
            TimeSeriesIntensiveOutcome.TargetFailure, default);
        var badOrigin = new TimeSeriesIntensiveFailure((TimeSeriesIntensiveFailureOrigin)int.MaxValue, null, null, null);
        var badCode = new TimeSeriesIntensiveFailure(TimeSeriesIntensiveFailureOrigin.KeyLoad, (ErrorCode)int.MaxValue, null, null);
        foreach (var invalid in new[] { valid with { Stage = (TimeSeriesIntensiveRunStage)int.MaxValue },
            valid with { Outcome = (TimeSeriesIntensiveOutcome)int.MaxValue }, valid with { Failure = badOrigin }, valid with { Failure = badCode } })
        {
            Reject(original with { Failure = invalid });
            Reject(original with { Repetitions = [new(0, default, default, false, invalid)] });
        }

        foreach (var failure in new[] { badOrigin, badCode })
        {
            RejectObservedFailure(original, failure, false);
            RejectObservedFailure(original, failure, true);
        }

        await Assert.That(original.Failure).IsNull();
    }

    [Test]
    public async Task AcTj009003EachNonfiniteMeasurementFieldLeavesOutputUnchanged()
    {
        var original = TimeSeriesIntensiveRunJsonTestData.Create();
        var finite = new TimeSeriesIntensiveMeasurement(10000, 9999, 1, 2, 3, 4, 5, 6);
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            foreach (var invalid in new[] { finite with { WallSeconds = value }, finite with { Throughput = value },
                finite with { ValidationWorkerSeconds = value }, finite with { P50Milliseconds = value },
                finite with { P95Milliseconds = value }, finite with { P99Milliseconds = value } })
            {
                var phase = new TimeSeriesIntensivePhaseResult(false, false, 0, 0, 0, 0, invalid);
                Reject(original with { Repetitions = [new(0, phase, default, false, null)] });
                Reject(original with { Repetitions = [new(0, default, phase, false, null)] });
            }
        }

        await Assert.That(finite.WallSeconds).IsEqualTo(1);
    }

    [Test]
    public async Task AcTj009002UnobservedFactsAreAbsentAndFailureNullableFactsRemainNull()
    {
        var storage = TimeSeriesIntensiveAttemptLedger.CreateMeasuredStorage();
        storage[49999] = storage[49999] with { LatencyTicks = -1, ValidationTicks = -1, ResultCount = long.MaxValue };
        var ledger = new TimeSeriesIntensiveAttemptLedger(storage, 0, 10000, 0);
        ledger.Publish(new(0, 3, 3, 7, 0, TimeSeriesIntensiveOutcome.TransportFailure, null, default, 0,
            new(TimeSeriesIntensiveFailureOrigin.HttpTransport, null, 502, null)));
        var original = TimeSeriesIntensiveRunJsonTestData.Create() with { Attempts = storage };
        using var document = TimeSeriesIntensiveRunJsonTestData.Serialize(original);
        var attempt = document.RootElement.GetProperty(TestKeys.Attempts)[3];
        await Assert.That(attempt.GetProperty(TestKeys.ResultCount).ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(attempt.GetProperty(TestKeys.Acknowledgement).ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(attempt.GetProperty(TestKeys.Outcome).GetString()).IsEqualTo("TransportFailure");
        await TimeSeriesIntensiveRunJsonTestData.VerifyFailure(attempt.GetProperty(TestKeys.Failure), "HttpTransport", null, 502, null);
        await Assert.That(TimeSeriesIntensiveRunJsonTestData.HasKeys(document.RootElement.GetProperty(TestKeys.Attempts)[49999],
            [TestKeys.Slot, TestKeys.Repetition, TestKeys.Index, TestKeys.Worker, TestKeys.Observed])).IsTrue();
    }

    private static void RejectObservedFailure(TimeSeriesIntensiveRunResult result, TimeSeriesIntensiveFailure failure, bool cleanup)
    {
        var storage = result.Attempts.ToArray();
        storage[49999] = storage[49999] with
        {
            Outcome = TimeSeriesIntensiveOutcome.Cancelled,
            Failure = cleanup ? default : failure,
            CleanupFailure = cleanup ? failure : default
        };
        Reject(result with { Attempts = storage });
    }

    private static void Reject(TimeSeriesIntensiveRunResult result)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartArray();
        writer.WriteStringValue(UnchangedValue);
        writer.Flush();
        var original = stream.ToArray();
        var pending = writer.BytesPending;
        var committed = writer.BytesCommitted;
        Assert.ThrowsExactly<ArgumentException>(() => TimeSeriesIntensiveRunJson.Write(writer, result));
        if (!stream.ToArray().SequenceEqual(original) || writer.BytesPending != pending || writer.BytesCommitted != committed)
        {
            throw new InvalidOperationException(WriterChangedBeforeValidation);
        }

        writer.WriteEndArray();
        writer.Flush();
        using var document = JsonDocument.Parse(stream.ToArray());
        if (document.RootElement.GetArrayLength() != 1 || document.RootElement[0].GetString() != UnchangedValue)
        {
            throw new InvalidOperationException(CallerStructureChanged);
        }
    }
}
