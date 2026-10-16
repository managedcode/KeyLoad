using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireRetirementDropParentOutcome(IKeyValueView view, ResourceRetirementDropParentV1 parent, bool completion)
    {
        var operation = completion ? parent.OriginalCompletion! : parent.OriginalIntent;
        var receipt = completion ? parent.CompleteReceipt! : parent.PersistReceipt;
        var expectedPhase = completion ? ResourceRetirementPhase.CompleteDropParent : ResourceRetirementPhase.PersistDropParent;
        RequireRetirementDropParentOriginal(parent, operation, receipt, expectedPhase);
        var outcome = ResourceRetirementStorage.Read<StoredOutcome>(view,
            KeySpace.GlobalOutcome(parent.PrincipalId, operation.Id), Limits)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Missing);
        if (outcome.Incarnation != Store.Identity.Incarnation || outcome.Fingerprint != NativeOperationFingerprint.Compute(operation)
            || outcome.PolicyEpoch != (completion ? parent.CompletionPolicyEpoch : parent.PolicyEpoch)
            || outcome.ScopeKind != CommandOutcomeScopeKind.Global || outcome.Partition is not null
            || outcome.Result.Error is not null || outcome.Result.Json is not null)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        var result = outcome.Result.Get<ResourceRetirementPhaseResultV1>();
        if (result.State is not null || result.Fence is not null || result.PreparedOperation is not null
            || result.PreparationState is not null || result.OwnerReservation is not null || result.WorkOwnerReceipt is not null
            || result.WorkBarrierReceipt is not null || result.CleanupCheckpoint is not null || result.OwnerCleanup is not null
            || result.Finalization is not null || result.OwnerRelease is not null || result.CleanupAttempt is not null
            || !ResourceRetirementValidation.Same(result.Receipt, receipt) || result.DropParent is not { } saved
            || !SameRetirementDropParentIntent(saved, parent)
            || completion && (!ResourceRetirementValidation.Same(saved.OriginalCompletion, parent.OriginalCompletion)
                || saved.ResultJson != parent.ResultJson || saved.CompletionPolicyEpoch != parent.CompletionPolicyEpoch
                || !ResourceRetirementValidation.Same(saved.CompleteReceipt, parent.CompleteReceipt)))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
    }

    private void RequireRetirementDropParentOriginal(ResourceRetirementDropParentV1 parent, ReplicatedOperation original,
        ResourceRetirementPhaseReceiptV1 receipt, ResourceRetirementPhase expectedPhase)
    {
        var operation = VerifyOperationAuthority(original);
        var phase = Payload<ResourceRetirementPhaseCommandV1>(operation);
        if (operation.Kind != OperationKind.ResourceRetirementPhase || operation.PrincipalId != parent.PrincipalId
            || phase.Version != ResourceRetirementProtocol.Version
            || phase.Phase != expectedPhase || phase.RetirementId != parent.Lookup.CommandId
            || operation.Id != ResourceRetirementDropParentIdentity.CommandId(parent.Lookup, expectedPhase)
            || receipt.Version != ResourceRetirementProtocol.Version || receipt.Phase != expectedPhase
            || receipt.CommandId != operation.Id || receipt.RetirementId != parent.Lookup.CommandId
            || receipt.AppliedPosition <= ResourceRetirementProtocol.Empty
            || receipt.IntentDigest != NativeOperationFingerprint.Compute(parent.OriginalIntent)
            || !ResourceRetirementValidation.Same(receipt.Lifetime, parent.Lifetime)
            || !ResourceRetirementValidation.Same(receipt.PhysicalOwner, parent.PersistReceipt.PhysicalOwner))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        var validBody = expectedPhase == ResourceRetirementPhase.PersistDropParent
            ? ResourceRetirementValidation.Same(NativeSerialization.Deserialize<ResourceRetirementPersistDropParentBodyV1>(phase.Body.Span),
                new ResourceRetirementPersistDropParentBodyV1(parent.Lookup, parent.RetirementRequest))
            : ResourceRetirementValidation.Same(NativeSerialization.Deserialize<ResourceRetirementCompleteDropParentBodyV1>(phase.Body.Span),
                new ResourceRetirementCompleteDropParentBodyV1(parent.Lookup));
        if (!validBody)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
    }

    private static bool SameRetirementDropParentIntent(ResourceRetirementDropParentV1 left, ResourceRetirementDropParentV1 right)
        => ResourceRetirementValidation.Same(left with
        {
            ResultJson = null,
            OriginalCompletion = null,
            CompleteReceipt = null,
            CompletionPolicyEpoch = null
        }, right with
        {
            ResultJson = null,
            OriginalCompletion = null,
            CompleteReceipt = null,
            CompletionPolicyEpoch = null
        });
}
