using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Recovery requires the original durable native result; staged authority reads do not invoke this check.</summary>
internal static class ResourceRetirementOutcomeValidation
{
    internal static void Identity(ResourceRetirementOutcomeIdentityV1? identity, DatabaseLimits limits)
    {
        if (identity is null || identity.PolicyEpoch < ResourceRetirementProtocol.Empty || !ResourceRetirementValidation.Digest(identity.Fingerprint)
            || string.IsNullOrWhiteSpace(identity.PrincipalId))
        {
            throw Invalid();
        }

        JsonData.Identifier(identity.PrincipalId);
        ResourceRetirementValidation.Bound(identity, limits);
    }

    internal static ResourceRetirementPhaseResultV1 Read(IKeyValueView view,
        ResourceRetirementOutcomeIdentityV1? identity, ResourceRetirementPhaseReceiptV1 receipt,
        ResourceRetirementPhase expected, DatabaseLimits limits)
    {
        Identity(identity, limits);
        if (receipt is null || receipt.Phase != expected || receipt.PhysicalOwner is null)
        {
            throw Invalid();
        }

        var outcome = ResourceRetirementStorage.Read<StoredOutcome>(view,
            KeySpace.GlobalOutcome(identity!.PrincipalId, receipt.CommandId), limits)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        if (outcome.Incarnation != receipt.PhysicalOwner.Incarnation
            || outcome.PolicyEpoch != identity.PolicyEpoch || outcome.Fingerprint != identity.Fingerprint
            || outcome.ScopeKind != CommandOutcomeScopeKind.Global || outcome.Partition is not null
            || outcome.Result is null || outcome.Result.Error is not null || outcome.Result.Json is not null
            || outcome.Result.NativeValue is not ResourceRetirementPhaseResultV1 result
            || !ResourceRetirementValidation.Same(result.Receipt, receipt)
            || result.State is not null || result.Fence is not null || result.PreparedOperation is not null
            || result.PreparationState is not null || result.OwnerReservation is not null
            || result.WorkOwnerReceipt is not null || result.WorkBarrierReceipt is not null
            || result.Finalization is not null || result.DropParent is not null || result.CleanupAttempt is not null)
        {
            throw Invalid();
        }

        var valid = expected switch
        {
            ResourceRetirementPhase.CleanupPage => result.CleanupCheckpoint is { } checkpoint
                && result.OwnerCleanup is null && result.OwnerRelease is null
                && ResourceRetirementValidation.Same(checkpoint.OutcomeIdentity, identity),
            ResourceRetirementPhase.CompleteOwnerCleanup => result.OwnerCleanup is { } cleanup
                && result.CleanupCheckpoint is null && result.OwnerRelease is null
                && ResourceRetirementValidation.Same(cleanup.OutcomeIdentity, identity),
            ResourceRetirementPhase.ReleaseOwnerTopology => result.OwnerRelease is { } release
                && result.CleanupCheckpoint is null && result.OwnerCleanup is null
                && ResourceRetirementValidation.Same(release.OutcomeIdentity, identity),
            _ => false
        };
        if (!valid)
        {
            throw Invalid();
        }

        return result;
    }

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
}
