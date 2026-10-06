using System.Security.Cryptography;
using System.Text;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class OpenLoopCancellationProofValidationForTests
{
    internal static void Validate(OpenLoopCancellationProofV1 proof, ComparisonWorkerSelection selection,
        int expectedRate, OpenLoopNativeCompletionV1 observedMarker,
        IOptions<BenchmarkProvenanceOptions> provenanceOptions)
    {
        if (proof is null || proof.Worker is null || proof.Milestone is null || proof.Accounting is null
            || proof.DatasetSha256 is null || proof.HealthyReadSha256 is null)
        {
            throw new InvalidDataException(OpenLoopNativeTestOracle.ProofFieldsMissing);
        }
        ArgumentNullException.ThrowIfNull(observedMarker);
        ArgumentNullException.ThrowIfNull(provenanceOptions);
        var profile = ScaledComparisonProfileParser.Parse(selection.Profile);
        var corpus = new ScaledComparisonCorpus(profile);
        ValidateIdentity(proof, selection, profile, expectedRate, observedMarker, provenanceOptions);
        ValidateAccounting(proof);
        ValidatePolicy(proof.ExecutionPolicy);
        var expected = corpus.CreateDocument(OpenLoopNativeTestOracle.FirstIndex);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(expected.Json)));
        if (proof.DatasetRecords != profile.Documents || proof.DatasetSha256 != corpus.Sha256
            || proof.HealthyReadSha256 != hash || proof.HealthyReadRevision <= OpenLoopNativeTestOracle.NoHealthyReadRevision)
        {
            throw new InvalidDataException(OpenLoopNativeTestOracle.ProofReadbackInvalid);
        }
    }

    private static void ValidateIdentity(OpenLoopCancellationProofV1 proof,
        ComparisonWorkerSelection selection, ScaledComparisonProfile profile, int rate,
        OpenLoopNativeCompletionV1 observedMarker, IOptions<BenchmarkProvenanceOptions> provenanceOptions)
    {
        var worker = proof.Worker;
        var milestone = proof.Milestone;
        var original = provenanceOptions.Value;
        if (proof.Version != OpenLoopNativeTestOracle.SchemaVersion
            || worker.Target != OpenLoopNativeTestOracle.KeyLoadTarget
            || worker.NodeCount != OpenLoopNativeTestOracle.KeyLoadNodeCount
            || worker.Scenario != Scenario.PointRead || worker.Profile != profile.Id
            || worker.SourceRevision != Required(original.SourceRevision)
            || worker.RunId != PositiveInt64(original.WorkflowRunId)
            || worker.Attempt != PositiveInt32(original.RunAttempt)
            || worker.JobId != PositiveInt64(original.JobId)
            || worker.Repository != Required(original.Repository)
            || worker.Ref != Required(original.Reference)
            || worker.Workflow != Required(original.Workflow)
            || selection.Target != OpenLoopNativeTestOracle.KeyLoadTarget
            || selection.NodeCount != OpenLoopNativeTestOracle.KeyLoadNodeCount
            || selection.Scenario != Scenario.PointRead || selection.Profile != profile.Id
            || proof.ProfileId != profile.Id || proof.Rate != rate || rate != selection.OpenLoopRate
            || proof.Scenario != Scenario.PointRead || milestone.Scenario != Scenario.PointRead
            || milestone.OfferedRatePerSecond != rate || milestone.Completed != OpenLoopNativeTestOracle.MinimumMilestoneCompleted
            || milestone.Completed != observedMarker.Completed || milestone.Started != observedMarker.Started
            || milestone.Planned != observedMarker.Planned || observedMarker.ProfileId != profile.Id
            || observedMarker.Scenario != milestone.Scenario || observedMarker.Rate != rate
            || milestone.Completed > milestone.Started
            || milestone.Started > milestone.Planned || milestone.Planned != OpenLoopNativeTestOracle.PlannedOperations)
        {
            throw new InvalidDataException(OpenLoopNativeTestOracle.ProofIdentityInvalid);
        }
    }

    private static void ValidatePolicy(OpenLoopExecutionPolicy policy)
    {
        if (policy is null || policy.QueueCapacity != OpenLoopNativeTestOracle.QueueCapacity
            || policy.ConcurrentSessions != OpenLoopNativeTestOracle.ConcurrentSessions
            || policy.MaximumNodes != OpenLoopNativeTestOracle.MaximumNodes
            || policy.OperationDeadlineMilliseconds != OpenLoopNativeTestOracle.OperationDeadlineMilliseconds
            || policy.DrainMilliseconds != OpenLoopNativeTestOracle.DrainMilliseconds
            || policy.ControlPollMilliseconds != OpenLoopNativeTestOracle.ControlPollMilliseconds
            || policy.SpinWindowMicroseconds != OpenLoopNativeTestOracle.SpinWindowMicroseconds)
        {
            throw new InvalidDataException(OpenLoopNativeTestOracle.ProofPolicyInvalid);
        }
    }

    private static void ValidateAccounting(OpenLoopCancellationProofV1 proof)
    {
        var counts = proof.Accounting;
        var planned = (long)counts.NotOffered + counts.HarnessRejected + counts.TimedOutBeforeStart
            + counts.Succeeded + counts.Failed + counts.TargetRejected + counts.TimedOutAfterStart
            + counts.UnfinishedQueued + counts.UnfinishedStarted;
        var started = (long)counts.Succeeded + counts.Failed + counts.TargetRejected
            + counts.TimedOutAfterStart + counts.UnfinishedStarted;
        var completed = (long)counts.Succeeded + counts.Failed + counts.TargetRejected + counts.TimedOutAfterStart;
        if (counts.Planned != OpenLoopNativeTestOracle.PlannedOperations
            || planned != OpenLoopNativeTestOracle.PlannedOperations || counts.Started != started
            || counts.Completed != completed || counts.Completed < proof.Milestone.Completed
            || !proof.CallerCancelled || !proof.ProducerSettled || !proof.NativeCallsSettled
            || !proof.SessionsClosed || !proof.HealthyReadVerified || !proof.HealthyReadSessionClosed)
        {
            throw new InvalidDataException(OpenLoopNativeTestOracle.ProofAccountingInvalid);
        }
    }
    private static string Required(string? value)
        => OpenLoopNativeProvenanceValues.IsPresent(value) ? value!
            : throw new InvalidDataException(OpenLoopNativeTestOracle.ProofIdentityMissing);

    private static long PositiveInt64(string? value)
        => OpenLoopNativeProvenanceValues.TryPositiveInt64(value, out var parsed) ? parsed
            : throw new InvalidDataException(OpenLoopNativeTestOracle.ProofIdentityMissing);

    private static int PositiveInt32(string? value)
        => OpenLoopNativeProvenanceValues.TryPositiveInt32(value, out var parsed) ? parsed
            : throw new InvalidDataException(OpenLoopNativeTestOracle.ProofIdentityMissing);

}
