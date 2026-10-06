using System.Text;

namespace KeyLoad.Comparisons;

internal static class OpenLoopEvidenceValidation
{
    private const int NoObservedItems = 0;
    private const int NoMeasuredRate = 0;
    private const int NoItems = 0;

    internal static void Validate(OpenLoopComparisonReport report)
    {
        if (report.Worker is not { } worker)
        {
            throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopEvidenceIdentityInvalid);
        }
        ValidateAccounting(report.Accounting);
        if (!ValidReportIdentityAndMeasurements(report, worker))
        {
            throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopEvidenceIdentityInvalid);
        }
        ValidateSampleSelection(report);
    }

    private static void ValidateAccounting(OpenLoopOperationAccounting accounting)
        => _ = OpenLoopAccountingValidator.ValidateAndCreate(accounting.NotOffered, accounting.HarnessRejected,
            accounting.TimedOutBeforeStart, accounting.Succeeded, accounting.Failed, accounting.TargetRejected,
            accounting.TimedOutAfterStart, accounting.UnfinishedQueued, accounting.UnfinishedStarted,
            accounting.Started, accounting.Completed);

    private static bool ValidReportIdentityAndMeasurements(OpenLoopComparisonReport report,
        IsolatedComparisonWorker worker)
        => ValidFormatAndIdentity(report, worker) && ValidTiming(report) && ValidTarget(report, worker);

    private static bool ValidFormatAndIdentity(OpenLoopComparisonReport report,
        IsolatedComparisonWorker worker)
        => report.ExecutionPolicy is not null && report.ExecutionPolicy.IsQualifiedV1()
            && report.Version == OpenLoopEvidenceContract.SchemaVersion
            && report.Samples.Length == OpenLoopRateContract.SampleCapacity
            && worker.Profile == report.ProfileId && worker.Scenario == report.Scenario
            && worker.Target == report.Target.Name && worker.SourceRevision == report.SourceRevision
            && worker.JobId == report.JobId && worker.RunId == report.RunId
            && worker.Attempt == report.Attempt && report.JobId > NoObservedItems && report.RunId > NoObservedItems
            && report.Attempt > NoObservedItems && report.DatasetRecords > NoObservedItems
            && report.DatasetSha256.Length == OpenLoopEvidenceContract.Sha256HexCharacters
            && report.SourceRevision?.Length == OpenLoopEvidenceContract.GitRevisionHexCharacters
            && OpenLoopRateContract.AcceptedRates.Contains(report.OfferedRatePerSecond);

    private static bool ValidTiming(OpenLoopComparisonReport report)
        => report.Timing.CollectedSamples == report.Samples.Length
            && report.Timing.MissingSamples == report.Timing.SampleCapacity - report.Samples.Length
            && report.Timing.SampleCapacity == OpenLoopRateContract.SampleCapacity
            && ValidDimensions(report.Timing)
            && double.IsFinite(report.ElapsedSeconds) && report.ElapsedSeconds > NoMeasuredRate
            && (!report.ScheduleComplete || report.Accounting.NotOffered == NoObservedItems)
            && (report.ScheduleComplete || report.Accounting.NotOffered > NoObservedItems);

    private static bool ValidTarget(OpenLoopComparisonReport report, IsolatedComparisonWorker worker)
        => Within(report.Storage, OpenLoopEvidenceContract.MaximumStorageDescriptionCharacters)
            && report.Runtime is { } runtime
            && Within(runtime, OpenLoopEvidenceContract.MaximumRuntimeDescriptionCharacters)
            && ValidIdentityText(worker) && ValidTargetMetadata(report.Target)
            && report.Target.Cluster is { } cluster && cluster.Nodes == worker.NodeCount;

    private static void ValidateSampleSelection(OpenLoopComparisonReport report)
    {
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
        => dimension.Denominator == capacity && dimension.SampleCount is >= NoObservedItems
            && dimension.SampleCount <= capacity && dimension.MissingSamples is >= NoItems
            && dimension.MissingSamples <= capacity
            && dimension.SampleCount == capacity - dimension.MissingSamples;

    private static bool ValidIdentityText(IsolatedComparisonWorker worker)
        => Within(worker.Target, OpenLoopEvidenceContract.MaximumWorkerTargetCharacters)
            && Within(worker.Profile, OpenLoopEvidenceContract.MaximumProfileIdCharacters)
            && Within(worker.SourceRevision, OpenLoopEvidenceContract.MaximumWorkerSourceRevisionCharacters)
            && Within(worker.Repository, OpenLoopEvidenceContract.MaximumRepositoryCharacters)
            && Within(worker.Ref, OpenLoopEvidenceContract.MaximumRefCharacters)
            && Within(worker.Workflow, OpenLoopEvidenceContract.MaximumWorkflowCharacters);

    private static bool ValidTargetMetadata(TargetProfile target)
    {
        const int NoItems = 0;
        const int ZeroAccumulator = 0;

        var values = new[] { target.Name, target.Version, target.Topology, target.WriteAcknowledgement,
            target.ReadContract, target.Transport, target.Authorization,
            target.Cluster?.State ?? string.Empty };
        return values.All(value => Within(value, OpenLoopEvidenceContract.MaximumTargetFieldCharacters))
            && values.Sum(Encoding.UTF8.GetByteCount) <= OpenLoopEvidenceContract.MaximumTargetMetadataBytes
            && (target.Image is null || Within(target.Image, OpenLoopEvidenceContract.MaximumTargetFieldCharacters))
            && (target.Cluster?.Observations.Length ?? NoItems) <= OpenLoopEvidenceContract.MaximumClusterObservations
            && (target.Cluster?.Observations.All(value => Within(value, OpenLoopEvidenceContract.MaximumObservationCharacters)) ?? false)
            && (target.Cluster?.Observations.Sum(value => (long)Encoding.UTF8.GetByteCount(value)) ?? ZeroAccumulator) <= OpenLoopEvidenceContract.MaximumObservationBytes;
    }

    private static bool Within(string value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;
}
