using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;
using TestKeys = KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive.TimeSeriesIntensiveRunJsonTestKeys;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveRunJsonTests
{
    [Test]
    public async Task AcTj009001002003DefaultLedgerEmitsEveryPlannedSlotAndBorrowsWriter()
    {
        var result = TimeSeriesIntensiveRunJsonTestData.Create();
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartArray();
        TimeSeriesIntensiveRunJson.Write(writer, result);
        await Assert.That(writer.BytesPending).IsGreaterThan(0);
        writer.WriteNumberValue(17);
        writer.WriteEndArray();
        await writer.FlushAsync();
        using var document = JsonDocument.Parse(stream.ToArray());
        var root = document.RootElement[0];
        await Assert.That(document.RootElement[1].GetInt32()).IsEqualTo(17);
        await Assert.That(TimeSeriesIntensiveRunJsonTestData.HasKeys(root,
            [TestKeys.SchemaVersion, TestKeys.Scenario, TestKeys.TimestampFrequency, TestKeys.SeedVerified, TestKeys.WorkloadSucceeded, TestKeys.PlannedSlotCount,
                TestKeys.ObservedAttemptCount, TestKeys.UnstartedSlotCount, TestKeys.Failure, TestKeys.Repetitions, TestKeys.Attempts])).IsTrue();
        await Assert.That(root.GetProperty(TestKeys.SchemaVersion).GetInt32()).IsEqualTo(1);
        await Assert.That(root.GetProperty(TestKeys.Scenario).GetString()).IsEqualTo("RawRangeRead");
        await Assert.That(root.GetProperty(TestKeys.TimestampFrequency).GetInt64()).IsEqualTo(long.MaxValue);
        await Assert.That(root.GetProperty(TestKeys.SeedVerified).GetBoolean()).IsFalse();
        await Assert.That(root.GetProperty(TestKeys.WorkloadSucceeded).GetBoolean()).IsFalse();
        await Assert.That(root.GetProperty(TestKeys.PlannedSlotCount).GetInt32()).IsEqualTo(50000);
        await Assert.That(root.GetProperty(TestKeys.ObservedAttemptCount).GetInt32()).IsEqualTo(0);
        await Assert.That(root.GetProperty(TestKeys.UnstartedSlotCount).GetInt32()).IsEqualTo(50000);
        await Assert.That(root.GetProperty(TestKeys.Failure).ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(root.GetProperty(TestKeys.Repetitions).GetArrayLength()).IsEqualTo(0);
        await Assert.That(root.GetProperty(TestKeys.Attempts).GetArrayLength()).IsEqualTo(50000);
        await Assert.That(CountIncorrectPlannedSlots(root.GetProperty(TestKeys.Attempts))).IsEqualTo(0);
    }

    [Test]
    public async Task AcTj009001002004CancelledAcknowledgementRetainsIndependentOriginalFacts()
    {
        var storage = TimeSeriesIntensiveAttemptLedger.CreateMeasuredStorage();
        var ledger = new TimeSeriesIntensiveAttemptLedger(storage, 40000, 10000, 4);
        var primary = new TimeSeriesIntensiveFailure(TimeSeriesIntensiveFailureOrigin.KeyLoad, ErrorCode.OwnershipLost, 503, null);
        var cleanup = new TimeSeriesIntensiveFailure(TimeSeriesIntensiveFailureOrigin.PostgreSQL, null, null, ulong.MaxValue);
        var command = new Guid("00112233-4455-6677-8899-aabbccddeeff");
        ledger.Publish(new(4, 9999, 15, long.MaxValue, long.MaxValue, TimeSeriesIntensiveOutcome.Cancelled,
            long.MaxValue, new(1, 2, 3, 4), long.MaxValue, primary)
        {
            Acknowledgement = new(command, long.MaxValue),
            CleanupFailure = cleanup
        });
        var failure = new TimeSeriesIntensiveRunFailure(TimeSeriesIntensiveRunStage.FinalVerification, 4,
            TimeSeriesIntensiveOutcome.Cancelled, primary);
        var measurement = new TimeSeriesIntensiveMeasurement(10000, 9999, 2.5, 3999.6, 0.125, 0.25, 1.5, double.MaxValue);
        var warmup = new TimeSeriesIntensivePhaseResult(true, true, long.MaxValue, 16, 11, 7, null);
        var measured = new TimeSeriesIntensivePhaseResult(true, false, long.MaxValue, 16, 16, 16, measurement);
        var result = new TimeSeriesIntensiveRunResult(TimeSeriesIntensiveScenario.Append, long.MaxValue, storage,
            [new(4, warmup, measured, false, failure)], true, failure);
        using var document = TimeSeriesIntensiveRunJsonTestData.Serialize(result);
        var root = document.RootElement;
        await Assert.That(root.GetProperty(TestKeys.WorkloadSucceeded).GetBoolean()).IsFalse();
        await Assert.That(root.GetProperty(TestKeys.SeedVerified).GetBoolean()).IsTrue();
        await Assert.That(root.GetProperty(TestKeys.ObservedAttemptCount).GetInt32()).IsEqualTo(1);
        await Assert.That(root.GetProperty(TestKeys.UnstartedSlotCount).GetInt32()).IsEqualTo(49999);
        await VerifyRunFailure(root.GetProperty(TestKeys.Failure));
        var repetition = root.GetProperty(TestKeys.Repetitions)[0];
        await Assert.That(TimeSeriesIntensiveRunJsonTestData.HasKeys(repetition,
            [TestKeys.Repetition, TestKeys.Warmup, TestKeys.Measured, TestKeys.FinalVerified, TestKeys.Failure])).IsTrue();
        await Assert.That(repetition.GetProperty(TestKeys.Repetition).GetInt32()).IsEqualTo(4);
        await Assert.That(repetition.GetProperty(TestKeys.FinalVerified).GetBoolean()).IsFalse();
        await VerifyRunFailure(repetition.GetProperty(TestKeys.Failure));
        await VerifyPhase(repetition.GetProperty(TestKeys.Warmup), warmup);
        await VerifyPhase(repetition.GetProperty(TestKeys.Measured), measured);
        await VerifyMeasurement(repetition.GetProperty(TestKeys.Measured).GetProperty(TestKeys.Measurement));
        await VerifyCancelledAttempt(root.GetProperty(TestKeys.Attempts)[49999]);
    }

    [Test]
    public async Task AcTj009002004SuccessAndNullFailuresPreserveOriginalRunPredicate()
    {
        var storage = TimeSeriesIntensiveAttemptLedger.CreateMeasuredStorage();
        var ledger = new TimeSeriesIntensiveAttemptLedger(storage, 0, 10000, 0);
        ledger.Publish(new(0, 0, 0, 0, 0, TimeSeriesIntensiveOutcome.Succeeded, 0, default, 0, default));
        var phase = new TimeSeriesIntensivePhaseResult(true, true, 0, 16, 0, 0, null);
        var repetitions = Enumerable.Range(0, 5).Select(index =>
            new TimeSeriesIntensiveRepetitionResult(index, phase, phase, true, null)).ToImmutableArray();
        var result = new TimeSeriesIntensiveRunResult(TimeSeriesIntensiveScenario.Latest, 1, storage, repetitions, true, null);
        using var document = TimeSeriesIntensiveRunJsonTestData.Serialize(result);
        var root = document.RootElement;
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(root.GetProperty(TestKeys.WorkloadSucceeded).GetBoolean()).IsTrue();
        await Assert.That(root.GetProperty(TestKeys.Repetitions).GetArrayLength()).IsEqualTo(5);
        await Assert.That(root.GetProperty(TestKeys.UnstartedSlotCount).GetInt32()).IsEqualTo(49999);
        foreach (var index in Enumerable.Range(0, 5))
        {
            var repetition = root.GetProperty(TestKeys.Repetitions)[index];
            await Assert.That(repetition.GetProperty(TestKeys.Repetition).GetInt32()).IsEqualTo(index);
            await Assert.That(repetition.GetProperty(TestKeys.FinalVerified).GetBoolean()).IsTrue();
            await Assert.That(repetition.GetProperty(TestKeys.Failure).ValueKind).IsEqualTo(JsonValueKind.Null);
            await VerifyPhase(repetition.GetProperty(TestKeys.Warmup), phase);
            await VerifyPhase(repetition.GetProperty(TestKeys.Measured), phase);
        }

        var attempt = root.GetProperty(TestKeys.Attempts)[0];
        await Assert.That(attempt.GetProperty(TestKeys.Outcome).GetString()).IsEqualTo("Succeeded");
        await Assert.That(attempt.GetProperty(TestKeys.Observed).GetBoolean()).IsTrue();
        await Assert.That(attempt.GetProperty(TestKeys.ResultCount).GetInt64()).IsEqualTo(0);
        await Assert.That(attempt.GetProperty(TestKeys.ReceiptSequence).GetInt64()).IsEqualTo(0);
        await Assert.That(attempt.GetProperty(TestKeys.Digest).GetString()).IsEqualTo(new string('0', 64));
        await Assert.That(attempt.GetProperty(TestKeys.Acknowledgement).ValueKind).IsEqualTo(JsonValueKind.Null);
        await TimeSeriesIntensiveRunJsonTestData.VerifyFailure(attempt.GetProperty(TestKeys.Failure), "None", null, null, null);
        await TimeSeriesIntensiveRunJsonTestData.VerifyFailure(attempt.GetProperty(TestKeys.CleanupFailure), "None", null, null, null);
    }

    private static int CountIncorrectPlannedSlots(JsonElement attempts)
    {
        string[] keys = [TestKeys.Slot, TestKeys.Repetition, TestKeys.Index, TestKeys.Worker, TestKeys.Observed];
        var incorrect = 0;
        var slot = 0;
        foreach (var attempt in attempts.EnumerateArray())
        {
            if (!TimeSeriesIntensiveRunJsonTestData.HasKeys(attempt, keys) || attempt.GetProperty(TestKeys.Slot).GetInt32() != slot
                || attempt.GetProperty(TestKeys.Repetition).GetInt32() != slot / 10000 || attempt.GetProperty(TestKeys.Index).GetInt32() != slot % 10000
                || attempt.GetProperty(TestKeys.Worker).GetInt32() != slot % 10000 % 16 || attempt.GetProperty(TestKeys.Observed).GetBoolean())
            {
                incorrect++;
            }

            slot++;
        }

        return incorrect;
    }

    private static async Task VerifyRunFailure(JsonElement failure)
    {
        await Assert.That(TimeSeriesIntensiveRunJsonTestData.HasKeys(failure, [TestKeys.Stage, TestKeys.Repetition, TestKeys.Outcome, TestKeys.Failure])).IsTrue();
        await Assert.That(failure.GetProperty(TestKeys.Stage).GetString()).IsEqualTo("FinalVerification");
        await Assert.That(failure.GetProperty(TestKeys.Repetition).GetInt32()).IsEqualTo(4);
        await Assert.That(failure.GetProperty(TestKeys.Outcome).GetString()).IsEqualTo("Cancelled");
        await TimeSeriesIntensiveRunJsonTestData.VerifyFailure(failure.GetProperty(TestKeys.Failure), "KeyLoad", "OwnershipLost", 503, null);
    }

    private static async Task VerifyPhase(JsonElement phase, TimeSeriesIntensivePhaseResult original)
    {
        await Assert.That(TimeSeriesIntensiveRunJsonTestData.HasKeys(phase,
            [TestKeys.Complete, TestKeys.Succeeded, TestKeys.WallTicks, TestKeys.WorkersStarted, TestKeys.PeakClientCalls, TestKeys.PeakDecodedResponses, TestKeys.Measurement])).IsTrue();
        await Assert.That(phase.GetProperty(TestKeys.Complete).GetBoolean()).IsEqualTo(original.Complete);
        await Assert.That(phase.GetProperty(TestKeys.Succeeded).GetBoolean()).IsEqualTo(original.Succeeded);
        await Assert.That(phase.GetProperty(TestKeys.WallTicks).GetInt64()).IsEqualTo(original.WallTicks);
        await Assert.That(phase.GetProperty(TestKeys.WorkersStarted).GetInt32()).IsEqualTo(original.WorkersStarted);
        await Assert.That(phase.GetProperty(TestKeys.PeakClientCalls).GetInt32()).IsEqualTo(original.PeakClientCalls);
        await Assert.That(phase.GetProperty(TestKeys.PeakDecodedResponses).GetInt32()).IsEqualTo(original.PeakDecodedResponses);
        await Assert.That(phase.GetProperty(TestKeys.Measurement).ValueKind).IsEqualTo(original.Measurement is null ? JsonValueKind.Null : JsonValueKind.Object);
    }

    private static async Task VerifyMeasurement(JsonElement measurement)
    {
        await Assert.That(TimeSeriesIntensiveRunJsonTestData.HasKeys(measurement,
            [TestKeys.Attempted, TestKeys.Succeeded, TestKeys.WallSeconds, TestKeys.Throughput, TestKeys.ValidationWorkerSeconds, TestKeys.P50Milliseconds, TestKeys.P95Milliseconds, TestKeys.P99Milliseconds])).IsTrue();
        await Assert.That(measurement.GetProperty(TestKeys.Attempted).GetInt32()).IsEqualTo(10000);
        await Assert.That(measurement.GetProperty(TestKeys.Succeeded).GetInt32()).IsEqualTo(9999);
        await Assert.That(measurement.GetProperty(TestKeys.WallSeconds).GetDouble()).IsEqualTo(2.5);
        await Assert.That(measurement.GetProperty(TestKeys.Throughput).GetDouble()).IsEqualTo(3999.6);
        await Assert.That(measurement.GetProperty(TestKeys.ValidationWorkerSeconds).GetDouble()).IsEqualTo(0.125);
        await Assert.That(measurement.GetProperty(TestKeys.P50Milliseconds).GetDouble()).IsEqualTo(0.25);
        await Assert.That(measurement.GetProperty(TestKeys.P95Milliseconds).GetDouble()).IsEqualTo(1.5);
        await Assert.That(measurement.GetProperty(TestKeys.P99Milliseconds).GetDouble()).IsEqualTo(double.MaxValue);
    }

    private static async Task VerifyCancelledAttempt(JsonElement attempt)
    {
        await Assert.That(TimeSeriesIntensiveRunJsonTestData.HasKeys(attempt, [TestKeys.Slot, TestKeys.Repetition, TestKeys.Index, TestKeys.Worker, TestKeys.Observed, TestKeys.Outcome,
            TestKeys.LatencyTicks, TestKeys.ValidationTicks, TestKeys.ResultCount, TestKeys.Digest, TestKeys.ReceiptSequence, TestKeys.Acknowledgement, TestKeys.Failure, TestKeys.CleanupFailure])).IsTrue();
        await Assert.That(attempt.GetProperty(TestKeys.Outcome).GetString()).IsEqualTo("Cancelled");
        await Assert.That(attempt.GetProperty(TestKeys.Slot).GetInt32()).IsEqualTo(49999);
        await Assert.That(attempt.GetProperty(TestKeys.Repetition).GetInt32()).IsEqualTo(4);
        await Assert.That(attempt.GetProperty(TestKeys.Index).GetInt32()).IsEqualTo(9999);
        await Assert.That(attempt.GetProperty(TestKeys.Worker).GetInt32()).IsEqualTo(15);
        await Assert.That(attempt.GetProperty(TestKeys.Observed).GetBoolean()).IsTrue();
        await Assert.That(attempt.GetProperty(TestKeys.LatencyTicks).GetInt64()).IsEqualTo(long.MaxValue);
        await Assert.That(attempt.GetProperty(TestKeys.ValidationTicks).GetInt64()).IsEqualTo(long.MaxValue);
        await Assert.That(attempt.GetProperty(TestKeys.ResultCount).GetInt64()).IsEqualTo(long.MaxValue);
        await Assert.That(attempt.GetProperty(TestKeys.ReceiptSequence).GetInt64()).IsEqualTo(long.MaxValue);
        await Assert.That(attempt.GetProperty(TestKeys.Digest).GetString()).IsEqualTo("0000000000000001000000000000000200000000000000030000000000000004");
        var acknowledgement = attempt.GetProperty(TestKeys.Acknowledgement);
        await Assert.That(TimeSeriesIntensiveRunJsonTestData.HasKeys(acknowledgement, [TestKeys.CommandId, TestKeys.Sequence])).IsTrue();
        await Assert.That(acknowledgement.GetProperty(TestKeys.CommandId).GetGuid()).IsEqualTo(new Guid("00112233-4455-6677-8899-aabbccddeeff"));
        await Assert.That(acknowledgement.GetProperty(TestKeys.Sequence).GetInt64()).IsEqualTo(long.MaxValue);
        await TimeSeriesIntensiveRunJsonTestData.VerifyFailure(attempt.GetProperty(TestKeys.Failure), "KeyLoad", "OwnershipLost", 503, null);
        await TimeSeriesIntensiveRunJsonTestData.VerifyFailure(attempt.GetProperty(TestKeys.CleanupFailure), "PostgreSQL", null, null, ulong.MaxValue);
    }
}

internal static class TimeSeriesIntensiveRunJsonTestData
{
    internal static TimeSeriesIntensiveRunResult Create() => new(TimeSeriesIntensiveScenario.RawRangeRead, long.MaxValue,
        TimeSeriesIntensiveAttemptLedger.CreateMeasuredStorage(), [], false, null);

    internal static bool HasKeys(JsonElement value, string[] keys) => value.EnumerateObject().Select(property => property.Name).SequenceEqual(keys);

    internal static JsonDocument Serialize(TimeSeriesIntensiveRunResult result)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        TimeSeriesIntensiveRunJson.Write(writer, result);
        writer.Flush();
        return JsonDocument.Parse(stream.ToArray());
    }

    internal static async Task VerifyFailure(JsonElement failure, string origin, string? code, int? status, ulong? sqlState)
    {
        await Assert.That(HasKeys(failure, [TestKeys.Origin, TestKeys.KeyLoadCode, TestKeys.HttpStatus, TestKeys.SqlState])).IsTrue();
        await Assert.That(failure.GetProperty(TestKeys.Origin).GetString()).IsEqualTo(origin);
        await Assert.That(failure.GetProperty(TestKeys.KeyLoadCode).GetString()).IsEqualTo(code);
        var actualStatus = failure.GetProperty(TestKeys.HttpStatus);
        await Assert.That(actualStatus.ValueKind == JsonValueKind.Null ? (int?)null : actualStatus.GetInt32()).IsEqualTo(status);
        var actualSqlState = failure.GetProperty(TestKeys.SqlState);
        await Assert.That(actualSqlState.ValueKind == JsonValueKind.Null ? (ulong?)null : actualSqlState.GetUInt64()).IsEqualTo(sqlState);
    }
}

internal static class TimeSeriesIntensiveRunJsonTestKeys
{
    internal const string SchemaVersion = "schemaVersion";
    internal const string Scenario = "scenario";
    internal const string TimestampFrequency = "timestampFrequency";
    internal const string SeedVerified = "seedVerified";
    internal const string WorkloadSucceeded = "workloadSucceeded";
    internal const string PlannedSlotCount = "plannedSlotCount";
    internal const string ObservedAttemptCount = "observedAttemptCount";
    internal const string UnstartedSlotCount = "unstartedSlotCount";
    internal const string Failure = "failure";
    internal const string Repetitions = "repetitions";
    internal const string Attempts = "attempts";
    internal const string Stage = "stage";
    internal const string Repetition = "repetition";
    internal const string Outcome = "outcome";
    internal const string Warmup = "warmup";
    internal const string Measured = "measured";
    internal const string FinalVerified = "finalVerified";
    internal const string Complete = "complete";
    internal const string Succeeded = "succeeded";
    internal const string WallTicks = "wallTicks";
    internal const string WorkersStarted = "workersStarted";
    internal const string PeakClientCalls = "peakClientCalls";
    internal const string PeakDecodedResponses = "peakDecodedResponses";
    internal const string Measurement = "measurement";
    internal const string Attempted = "attempted";
    internal const string WallSeconds = "wallSeconds";
    internal const string Throughput = "throughput";
    internal const string ValidationWorkerSeconds = "validationWorkerSeconds";
    internal const string P50Milliseconds = "p50Milliseconds";
    internal const string P95Milliseconds = "p95Milliseconds";
    internal const string P99Milliseconds = "p99Milliseconds";
    internal const string Slot = "slot";
    internal const string Index = "index";
    internal const string Worker = "worker";
    internal const string Observed = "observed";
    internal const string LatencyTicks = "latencyTicks";
    internal const string ValidationTicks = "validationTicks";
    internal const string ResultCount = "resultCount";
    internal const string Digest = "digest";
    internal const string ReceiptSequence = "receiptSequence";
    internal const string Acknowledgement = "acknowledgement";
    internal const string CommandId = "commandId";
    internal const string Sequence = "sequence";
    internal const string CleanupFailure = "cleanupFailure";
    internal const string Origin = "origin";
    internal const string KeyLoadCode = "keyLoadCode";
    internal const string HttpStatus = "httpStatus";
    internal const string SqlState = "sqlState";
}
