using System.Text.Json;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedNativeOpenLoopAssertions
{
    internal static async Task VerifyAsync(string output, ComparisonWorkerSelection selection,
        int rate, IOptions<BenchmarkProvenanceOptions> provenanceOptions, CancellationToken cancellationToken)
    {
        var file = new FileInfo(Path.Combine(output, OpenLoopEvidenceContract.OpenLoopEvidenceFileName));
        await Assert.That(file.Exists && file.Length is >= OpenLoopNativeTestOracle.MinimumArtifactBytes
            and <= OpenLoopNativeTestOracle.MaximumArtifactBytes).IsTrue();
        await Assert.That(File.Exists(Path.Combine(output,
            OpenLoopCancellationProofContract.ProofFileName))).IsFalse();
        await using var stream = file.OpenRead();
        var report = await JsonSerializer.DeserializeAsync<OpenLoopComparisonReport>(stream,
            ReportWriter.JsonOptions, cancellationToken)
            ?? throw new InvalidDataException(OpenLoopNativeTestOracle.EvidenceMissing);
        await VerifyIdentityAsync(report, selection, rate, provenanceOptions);
        await VerifyPolicyAsync(report.ExecutionPolicy);
        await VerifyAccountingAsync(report);
        await VerifySamplesAsync(report, rate);
    }

    private static async Task VerifyIdentityAsync(OpenLoopComparisonReport report,
        ComparisonWorkerSelection selection, int rate, IOptions<BenchmarkProvenanceOptions> provenanceOptions)
    {
        var worker = report.Worker ?? throw new InvalidDataException(OpenLoopNativeTestOracle.WorkerIdentityMissing);
        ArgumentNullException.ThrowIfNull(provenanceOptions);
        await Assert.That(report.Version).IsEqualTo(OpenLoopNativeTestOracle.SchemaVersion);
        await Assert.That(report.ProfileId).IsEqualTo(selection.Profile);
        var profile = ScaledComparisonProfileParser.Parse(selection.Profile);
        var corpus = new ScaledComparisonCorpus(profile);
        await Assert.That(report.DatasetRecords).IsEqualTo(profile.Documents);
        await Assert.That(report.DatasetSha256).IsEqualTo(corpus.Sha256);
        await Assert.That(report.OfferedRatePerSecond).IsEqualTo(rate);
        await Assert.That(report.Scenario).IsEqualTo(selection.Scenario);
        await Assert.That(report.Target.Name).IsEqualTo(selection.Target);
        await Assert.That(report.Target.Cluster!.Nodes).IsEqualTo(selection.NodeCount);
        var original = provenanceOptions.Value;
        await Assert.That(report.SourceRevision).IsEqualTo(Required(original.SourceRevision));
        await Assert.That(report.RunId).IsEqualTo(PositiveInt64(original.WorkflowRunId));
        await Assert.That(report.Attempt).IsEqualTo(PositiveInt32(original.RunAttempt));
        await Assert.That(report.JobId).IsEqualTo(PositiveInt64(original.JobId));
        await Assert.That(worker.Target).IsEqualTo(selection.Target);
        await Assert.That(worker.NodeCount).IsEqualTo(selection.NodeCount);
        await Assert.That(worker.Scenario).IsEqualTo(selection.Scenario);
        await Assert.That(worker.Profile).IsEqualTo(selection.Profile);
        await Assert.That(worker.SourceRevision).IsEqualTo(report.SourceRevision);
        await Assert.That(worker.RunId).IsEqualTo(report.RunId);
        await Assert.That(worker.Attempt).IsEqualTo(report.Attempt);
        await Assert.That(worker.JobId).IsEqualTo(report.JobId);
        await Assert.That(worker.Repository).IsEqualTo(Required(original.Repository));
        await Assert.That(worker.Ref).IsEqualTo(Required(original.Reference));
        await Assert.That(worker.Workflow).IsEqualTo(Required(original.Workflow));
        await Assert.That(report.ElapsedSeconds > OpenLoopNativeTestOracle.NoElapsedSeconds
            && report.ClientResources is not null).IsTrue();
        await Assert.That(double.IsFinite(report.Timing.SuccessfulOperationsPerSecond)
            && report.Timing.SuccessfulOperationsPerSecond >= OpenLoopNativeTestOracle.NoSuccessfulOperationsPerSecond).IsTrue();
    }

    private static async Task VerifyPolicyAsync(OpenLoopExecutionPolicy policy)
    {
        if (policy is null)
        {
            throw new InvalidDataException(OpenLoopNativeTestOracle.ExecutionPolicyMissing);
        }
        await Assert.That(policy.QueueCapacity).IsEqualTo(OpenLoopNativeTestOracle.QueueCapacity);
        await Assert.That(policy.ConcurrentSessions).IsEqualTo(OpenLoopNativeTestOracle.ConcurrentSessions);
        await Assert.That(policy.MaximumNodes).IsEqualTo(OpenLoopNativeTestOracle.MaximumNodes);
        await Assert.That(policy.OperationDeadlineMilliseconds).IsEqualTo(OpenLoopNativeTestOracle.OperationDeadlineMilliseconds);
        await Assert.That(policy.DrainMilliseconds).IsEqualTo(OpenLoopNativeTestOracle.DrainMilliseconds);
        await Assert.That(policy.ControlPollMilliseconds).IsEqualTo(OpenLoopNativeTestOracle.ControlPollMilliseconds);
        await Assert.That(policy.SpinWindowMicroseconds).IsEqualTo(OpenLoopNativeTestOracle.SpinWindowMicroseconds);
    }

    private static async Task VerifyAccountingAsync(OpenLoopComparisonReport report)
    {
        var counts = report.Accounting;
        var planned = (long)counts.NotOffered + counts.HarnessRejected + counts.TimedOutBeforeStart
            + counts.Succeeded + counts.Failed + counts.TargetRejected + counts.TimedOutAfterStart
            + counts.UnfinishedQueued + counts.UnfinishedStarted;
        var started = (long)counts.Succeeded + counts.Failed + counts.TargetRejected
            + counts.TimedOutAfterStart + counts.UnfinishedStarted;
        var completed = (long)counts.Succeeded + counts.Failed + counts.TargetRejected + counts.TimedOutAfterStart;
        await Assert.That(counts.Planned).IsEqualTo(OpenLoopNativeTestOracle.PlannedOperations);
        await Assert.That(planned).IsEqualTo(OpenLoopNativeTestOracle.PlannedOperations);
        await Assert.That(counts.Started).IsEqualTo(started);
        await Assert.That(counts.Completed).IsEqualTo(completed);
        await Assert.That(report.ScheduleComplete).IsTrue();
        await Assert.That(report.ScheduleComplete).IsEqualTo(counts.NotOffered == OpenLoopNativeTestOracle.NoOfferedCalls);
        await Assert.That(counts.Succeeded + counts.TargetRejected).IsEqualTo(OpenLoopNativeTestOracle.PlannedOperations);
        await Assert.That(report.CallerCancelled).IsFalse();
        await Assert.That(report.DrainExpired).IsFalse();
        await Assert.That(counts.Failed).IsEqualTo(OpenLoopNativeTestOracle.NoFailures);
        await Assert.That(counts.HarnessRejected).IsEqualTo(OpenLoopNativeTestOracle.NoRejectedHarnessCalls);
        await Assert.That(counts.TimedOutBeforeStart + counts.TimedOutAfterStart).IsEqualTo(OpenLoopNativeTestOracle.NoTimeouts);
        await Assert.That(counts.UnfinishedQueued + counts.UnfinishedStarted).IsEqualTo(OpenLoopNativeTestOracle.NoUnfinishedCalls);
        await Assert.That(report.SessionsClosed).IsTrue();
        var readback = report.Scenario != Scenario.PointRead && counts.Succeeded == OpenLoopNativeTestOracle.PlannedOperations;
        await Assert.That(report.MutationReadbackVerified).IsEqualTo(readback);
    }

    private static async Task VerifySamplesAsync(OpenLoopComparisonReport report, int rate)
    {
        await Assert.That(report.Samples.Length).IsEqualTo(OpenLoopNativeTestOracle.SampleCapacity);
        await Assert.That(report.Timing.SampleCapacity).IsEqualTo(OpenLoopNativeTestOracle.SampleCapacity);
        await Assert.That(report.Timing.CollectedSamples).IsEqualTo(OpenLoopNativeTestOracle.SampleCapacity);
        await Assert.That(report.Timing.MissingSamples).IsEqualTo(OpenLoopNativeTestOracle.NoMissingSamples);
        await AssertDimensionAsync(report.Timing.ScheduledToTerminal, OpenLoopNativeTestOracle.SampleCapacity);
        await AssertDimensionAsync(report.Timing.SchedulerLag, OpenLoopNativeTestOracle.SampleCapacity);
        await AssertDimensionAsync(report.Timing.QueueDelay, OpenLoopNativeTestOracle.SampleCapacity);
        await AssertDimensionAsync(report.Timing.ServiceTime, OpenLoopNativeTestOracle.SampleCapacity);
        for (var slot = 0; slot < OpenLoopNativeTestOracle.SampleCapacity; slot++)
        {
            var sample = report.Samples[slot];
            var expectedIndex = (int)((long)slot * (OpenLoopNativeTestOracle.PlannedOperations - OpenLoopNativeTestOracle.FinalSampleOffset) / (OpenLoopNativeTestOracle.SampleCapacity - OpenLoopNativeTestOracle.FinalSampleOffset));
            var expectedDue = (long)expectedIndex * OpenLoopNativeTestOracle.NanosecondsPerSecond / rate;
            await Assert.That(sample.Index).IsEqualTo(expectedIndex);
            await Assert.That(sample.DueOffsetNanoseconds).IsEqualTo(expectedDue);
            await Assert.That(sample.PayloadBytes).IsEqualTo(OpenLoopNativeTestOracle.PayloadBytes);
            await Assert.That(sample.Session is >= OpenLoopNativeTestOracle.FirstIndex and < OpenLoopNativeTestOracle.ConcurrentSessions).IsTrue();
            await Assert.That(sample.Outcome is OpenLoopOutcome.Succeeded or OpenLoopOutcome.TargetRejected).IsTrue();
        }
    }

    private static async Task AssertDimensionAsync(OpenLoopLatencyQuantiles dimension, int capacity)
    {
        await Assert.That(dimension.Denominator).IsEqualTo(capacity);
        await Assert.That(dimension.SampleCount is >= OpenLoopNativeTestOracle.NoSuccessfulLatencySamples && dimension.SampleCount <= capacity).IsTrue();
        await Assert.That(dimension.MissingSamples).IsEqualTo(capacity - dimension.SampleCount);
        await AssertQuantilesAsync(dimension);
    }

    private static async Task AssertQuantilesAsync(OpenLoopLatencyQuantiles dimension)
    {
        var values = new[] { dimension.P50Milliseconds, dimension.P95Milliseconds, dimension.P99Milliseconds };
        if (dimension.SampleCount == OpenLoopNativeTestOracle.NoSuccessfulLatencySamples)
        {
            await Assert.That(values.All(value => value is null)).IsTrue();
            return;
        }

        await Assert.That(values.All(value => value is { } sample && double.IsFinite(sample) && sample >= OpenLoopNativeTestOracle.NoLatencyMilliseconds)).IsTrue();
        var ordered = values.Select(value => value!.Value).ToArray();
        await Assert.That(ordered[OpenLoopNativeTestOracle.FirstIndex] <= ordered[OpenLoopNativeTestOracle.SecondIndex]
            && ordered[OpenLoopNativeTestOracle.SecondIndex] <= ordered[OpenLoopNativeTestOracle.ThirdIndex]).IsTrue();
    }
    private static string Required(string? value)
        => OpenLoopNativeProvenanceValues.IsPresent(value) ? value!
            : throw new InvalidOperationException(OpenLoopNativeTestOracle.RunIdentityMissing);

    private static long PositiveInt64(string? value)
        => OpenLoopNativeProvenanceValues.TryPositiveInt64(value, out var parsed) ? parsed
            : throw new InvalidOperationException(OpenLoopNativeTestOracle.RunIdentityInvalid);

    private static int PositiveInt32(string? value)
        => OpenLoopNativeProvenanceValues.TryPositiveInt32(value, out var parsed) ? parsed
            : throw new InvalidOperationException(OpenLoopNativeTestOracle.RunIdentityInvalid);

}
