using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.Comparisons;

internal static class OpenLoopCancellationProofValidation
{
    internal static void Validate(OpenLoopCancellationProofV1 proof)
    {
        ArgumentNullException.ThrowIfNull(proof);
        if (proof.Worker is null || proof.Milestone is null || proof.Accounting is null
            || proof.ExecutionPolicy is null || !proof.ExecutionPolicy.IsQualifiedV1()
            || proof.ProfileId is null || proof.DatasetSha256 is null || proof.HealthyReadSha256 is null)
        {
            throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopCancellationProofInvalid);
        }
        var profile = ScaledComparisonProfileParser.Parse(proof.ProfileId);
        ValidateIdentity(proof, profile);
        ValidateMilestone(proof.Milestone, proof.ProfileId, proof.Rate, proof.Scenario);
        ValidateAccounting(proof.Accounting, proof.Milestone);
        if (proof.Version != 1 || proof.DatasetRecords != profile.Documents
            || !ValidHash(proof.DatasetSha256) || !ValidHash(proof.HealthyReadSha256)
            || proof.HealthyReadRevision <= 0 || !proof.CallerCancelled || !proof.ProducerSettled
            || !proof.NativeCallsSettled || !proof.SessionsClosed || !proof.HealthyReadVerified
            || !proof.HealthyReadSessionClosed)
        {
            throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopCancellationProofInvalid);
        }
    }

    internal static string HashJson(string json)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    private static void ValidateIdentity(OpenLoopCancellationProofV1 proof, ScaledComparisonProfile profile)
    {
        var worker = proof.Worker ?? throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopCancellationProofIdentityInvalid);
        if (worker.Target != OpenLoopProtocolIdentities.KeyLoadTarget || worker.NodeCount != proof.ExecutionPolicy.MaximumNodes || worker.Scenario != Scenario.PointRead
            || worker.Profile != profile.Id || worker.RunId < 1 || worker.Attempt < 1 || worker.JobId < 1
            || proof.Scenario != Scenario.PointRead || !OpenLoopRateContract.AcceptedRates.Contains(proof.Rate))
        {
            throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopCancellationProofIdentityInvalid);
        }
    }

    private static void ValidateMilestone(OpenLoopProgressV1 milestone, string profile, int rate, Scenario scenario)
    {
        if (milestone.Completed < OpenLoopRateContract.ProgressInterval
            || milestone.Completed % OpenLoopRateContract.ProgressInterval != 0
            || milestone.Completed > milestone.Started || milestone.Started > milestone.Planned
            || milestone.Planned != OpenLoopRateContract.PlannedOperations
            || milestone.OfferedRatePerSecond != rate || milestone.Scenario != scenario
            || profile != ScaledComparisonProfileParser.Parse(profile).Id)
        {
            throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopCancellationMilestoneInvalid);
        }
    }

    private static void ValidateAccounting(OpenLoopOperationAccounting accounting, OpenLoopProgressV1 milestone)
    {
        _ = OpenLoopAccountingValidator.ValidateAndCreate(accounting.NotOffered, accounting.HarnessRejected,
            accounting.TimedOutBeforeStart, accounting.Succeeded, accounting.Failed, accounting.TargetRejected,
            accounting.TimedOutAfterStart, accounting.UnfinishedQueued, accounting.UnfinishedStarted,
            accounting.Started, accounting.Completed);
        if (accounting.Planned != milestone.Planned || accounting.Completed < milestone.Completed)
        {
            throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopCancellationAccountingInvalid);
        }
    }

    private static bool ValidHash(string value)
        => value is { Length: 64 }
            && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
