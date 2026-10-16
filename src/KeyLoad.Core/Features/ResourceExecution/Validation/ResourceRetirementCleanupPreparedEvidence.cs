using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceRetirementCleanupPreparedEvidence
{
    internal static void RequireFailed(StoredOutcome? outcome, ResourceRetirementPreparedOperationV1 prepared,
        DatabaseLimits limits)
    {
        if (outcome is null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Missing);
        }

        if (outcome.Incarnation != prepared.Context.Receiver!.Owner.Incarnation
            || outcome.Fingerprint != NativeOperationFingerprint.Compute(prepared.Original!)
            || outcome.ScopeKind != CommandOutcomeScopeKind.Global || outcome.Partition is not null
            || outcome.PolicyEpoch < ResourceRetirementProtocol.Empty || outcome.Result is null
            || outcome.Result.Error is not { } error
            || !ResourceRetirementAttemptValidation.RetainedFailure(error, prepared.Context)
            || string.IsNullOrWhiteSpace(outcome.Result.SafeDetail)
            || outcome.Result.NativeValue is not null || outcome.Result.Json is not null)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }

        ResourceRetirementValidation.Bound(outcome, limits);
    }
}
