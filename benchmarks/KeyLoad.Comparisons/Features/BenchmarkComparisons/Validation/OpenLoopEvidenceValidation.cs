using System.Text;

namespace KeyLoad.Comparisons;

internal static class OpenLoopEvidenceValidation
{
    internal static void Validate(OpenLoopComparisonReport report)
    {
        if (report.Worker is not { } worker)
        {
            throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopEvidenceIdentityInvalid);
        }
        _ = OpenLoopAccountingValidator.ValidateAndCreate(report.Accounting.NotOffered,
            report.Accounting.HarnessRejected, report.Accounting.TimedOutBeforeStart,
            report.Accounting.Succeeded, report.Accounting.Failed, report.Accounting.TargetRejected,
            report.Accounting.TimedOutAfterStart, report.Accounting.UnfinishedQueued,
            report.Accounting.UnfinishedStarted, report.Accounting.Started, report.Accounting.Completed);
        if (report.ExecutionPolicy is null || !report.ExecutionPolicy.IsQualifiedV1()
            || report.Version != 1 || report.Samples.Length != OpenLoopRateContract.SampleCapacity
            || worker.Profile != report.ProfileId || worker.Scenario != report.Scenario
            || worker.Target != report.Target.Name || worker.SourceRevision != report.SourceRevision
            || worker.JobId != report.JobId || worker.RunId != report.RunId
            || worker.Attempt != report.Attempt || report.JobId < 1 || report.RunId < 1
            || report.Attempt < 1 || report.DatasetRecords < 1 || report.DatasetSha256.Length != 64
            || report.SourceRevision?.Length != 40 || !OpenLoopRateContract.AcceptedRates.Contains(report.OfferedRatePerSecond)
            || report.Timing.CollectedSamples != report.Samples.Length
            || report.Timing.MissingSamples != report.Timing.SampleCapacity - report.Samples.Length
            || report.Timing.SampleCapacity != OpenLoopRateContract.SampleCapacity
            || !ValidDimensions(report.Timing)
            || !Within(report.Storage, 512) || report.Runtime is not { } runtime || !Within(runtime, 256)
            || !double.IsFinite(report.ElapsedSeconds) || report.ElapsedSeconds <= 0
            || !report.ScheduleComplete && report.Accounting.NotOffered == 0
            || report.ScheduleComplete && report.Accounting.NotOffered != 0
            || !ValidIdentityText(worker) || !ValidTargetMetadata(report.Target)
            || report.Target.Cluster is not { } cluster || cluster.Nodes != worker.NodeCount)
        {
            throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopEvidenceIdentityInvalid);
        }
        var expectedIndices = ScaledLatencySample.Indices(report.Accounting.Planned,
            OpenLoopRateContract.SampleCapacity);
        if (report.Samples.Where((sample, index) => index >= expectedIndices.Length
            || sample.Index != expectedIndices[index]).Any())
        {
            throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopEvidenceSampleSelectionInvalid);
        }
    }


    private static bool ValidDimensions(OpenLoopTimingSummary timing)
        => ValidDimension(timing.ScheduledToTerminal, timing.SampleCapacity)
            && ValidDimension(timing.SchedulerLag, timing.SampleCapacity)
            && ValidDimension(timing.QueueDelay, timing.SampleCapacity)
            && ValidDimension(timing.ServiceTime, timing.SampleCapacity);

    private static bool ValidDimension(OpenLoopLatencyQuantiles dimension, int capacity)
        => dimension.Denominator == capacity && dimension.SampleCount is >= 0
            && dimension.SampleCount <= capacity && dimension.MissingSamples is >= 0
            && dimension.MissingSamples <= capacity
            && dimension.SampleCount == capacity - dimension.MissingSamples;

    private static bool ValidIdentityText(IsolatedComparisonWorker worker)
        => Within(worker.Target, 256) && Within(worker.Profile, 64) && Within(worker.SourceRevision, 64)
            && Within(worker.Repository, 512) && Within(worker.Ref, 512) && Within(worker.Workflow, 256);

    private static bool ValidTargetMetadata(TargetProfile target)
    {
        var values = new[] { target.Name, target.Version, target.Topology, target.WriteAcknowledgement,
            target.ReadContract, target.Transport, target.Authorization,
            target.Cluster?.State ?? "" };
        return values.All(value => Within(value, 2_048))
            && values.Sum(value => Encoding.UTF8.GetByteCount(value)) <= 8_192
            && (target.Image is null || Within(target.Image, 2_048))
            && (target.Cluster?.Observations.Length ?? 0) <= 64
            && (target.Cluster?.Observations.All(value => Within(value, 1_024)) ?? false)
            && (target.Cluster?.Observations.Sum(value => (long)Encoding.UTF8.GetByteCount(value)) ?? 0) <= 8_192;
    }

    private static bool Within(string value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;
}

