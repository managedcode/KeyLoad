using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private ResourceRetirementPreparedOperationV1 CloseResourceRetirementOriginal(IKeyValueView view,
        ResourceRetirementPreparedOperationBodyV1 body, ResourceRetirementPhaseReceiptV1 receipt,
        ResourceRetirementPreparedReceiptAuthorityV1 authority)
    {
        var (previous, outcome) = ReadResourceRetirementCloseEligibility(view, body, authority.EvaluatedAt);
        return new(body.Context, ResourceRetirementPreparedOperationStorage.Prepared(body.Attempt),
            previous?.Original, previous?.Fingerprint, ResourceRetirementPreparedOperationStage.Closed,
            receipt, previous?.PersistReceipt, outcome?.Result.Error ?? ErrorCode.CursorExpired,
            outcome?.Result.SafeDetail ?? ResourceOwnerInventoryReadValidation.Expired)
        {
            ReceiptAuthority = authority,
            PersistAuthority = previous?.PersistAuthority
        };
    }

    private void RequirePreparedResourceRetirementFailedOutcome(StoredOutcome outcome,
        ResourceRetirementPreparedOperationV1 prepared)
    {
        var original = prepared.Original!;
        if (outcome.Incarnation != ResourceRetirementAttemptContextValidation.ExecutionOwner(prepared.Context).Incarnation
            || outcome.Fingerprint != NativeOperationFingerprint.Compute(original)
            || outcome.ScopeKind != CommandOutcomeScopeKind.Global || outcome.Partition is not null
            || outcome.PolicyEpoch < ResourceRetirementProtocol.Empty || outcome.Result is null
            || outcome.Result.Error is not { } error || !Enum.IsDefined(error)
            || prepared.Context.Purpose == ResourceRetirementAttemptPurpose.CleanupPage
                && !ResourceRetirementAttemptValidation.RetainedFailure(error, prepared.Context)
            || string.IsNullOrWhiteSpace(outcome.Result.SafeDetail) || outcome.Result.NativeValue is not null
            || outcome.Result.Json is not null)
        {
            if (outcome.Result is { Error: null })
            {
                throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
            }
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        ResourceRetirementValidation.Bound(outcome, Limits);
    }
}
