using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private OperationResult SealResourceRetirement(IAtomicTransaction transaction,
        PrincipalRecord principal, ReplicatedOperation operation, ResourceRetirementPhaseCommandV1 phase, long position)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementSealBodyV1>(phase.Body.Span);
        if (body?.Attempt is null)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }
        ResourceRetirementValidation.Control(body.Control, Limits);
        var attempt = ResourceRetirementAttemptStorage.RequirePrepared(transaction, body.Control, body.Attempt, Limits);
        if (phase.RetirementId != body.Control.Request.RetirementId || operation.Id != attempt.OriginalCommandId)
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
        }
        var prepared = RequireExactPreparedResourceRetirementSeal(transaction, operation, body);
        var fingerprint = NativeOperationFingerprint.Compute(operation);
        ResourceRetirementPhaseResultV1 result;
        try
        {
            result = BuildResourceRetirementSeal(transaction, principal, operation, phase, body, attempt, prepared, fingerprint, position);
        }
        catch (KeyLoadException error) when (ResourceRetirementAttemptValidation.Recoverable(error.Code))
        {
            var failed = attempt with
            {
                Stage = ResourceRetirementAttemptStage.Failed,
                OriginalFingerprint = fingerprint,
                Error = error.Code,
                SafeDetail = error.Message
            };
            var state = ResourceRetirementStorage.ReadControl(transaction, body.Control.Lifetime, Limits)!;
            ResourceRetirementAttemptStorage.Write(transaction, body.Control, failed, Limits);
            ResourceRetirementStorage.WriteState(transaction, state with
            {
                SealAttempt = failed
            }, Limits);
            return new(null, error.Code, error.Message);
        }
        ResourceRetirementAttemptStorage.Write(transaction, body.Control, result.State!.SealAttempt!, Limits);
        ResourceRetirementStorage.WriteState(transaction, result.State, Limits);
        return Result(result);
    }

    private ResourceRetirementPhaseResultV1 BuildResourceRetirementSeal(IAtomicTransaction transaction, PrincipalRecord principal,
        ReplicatedOperation operation, ResourceRetirementPhaseCommandV1 phase, ResourceRetirementSealBodyV1 body,
        ResourceRetirementAttemptV1 attempt, ResourceRetirementPreparedOperationV1 prepared, string fingerprint, long position)
    {
        var state = RequireRetirementSource(transaction, body.Control);
        ResourceRetirementStateValidation.AllOwners(state);
        ResourceRetirementInventoryValidation.Require(body.Inventory, body.Control, Limits);
        if (!ResourceRetirementValidation.Same(body.Inventory.Control.RetirementAttempt, attempt)
            || attempt.PrincipalId != principal.Id || attempt.PolicyEpoch != principal.PolicyEpoch
            || body.Inventory.Control.PrincipalId != principal.Id || body.Inventory.Control.PolicyEpoch != principal.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
        }
        if (attempt.ExpiresAt <= operation.EvaluatedAt || body.Inventory.Control.Request.ExpiresAt <= operation.EvaluatedAt)
        {
            throw Errors.Fail(ErrorCode.CursorExpired, ResourceOwnerInventoryReadValidation.Expired);
        }
        RequireRetirementLocalSealCut(transaction, body.Inventory, prepared);
        var receipt = RetirementReceipt(transaction, body.Control, operation, phase.Phase, position);
        var sealedAttempt = attempt with
        {
            Stage = ResourceRetirementAttemptStage.Completed,
            OriginalFingerprint = fingerprint,
            TerminalReceipt = receipt
        };
        state = state with
        {
            Stage = ResourceRetirementStage.BarrierSealed,
            FinalInventory = body.Inventory,
            SealedReceipt = receipt,
            SealAttempt = sealedAttempt
        };
        ResourceRetirementStateValidation.State(state, Limits);
        var result = new ResourceRetirementPhaseResultV1(receipt, state, null);
        ResourceRetirementValidation.Bound(result, Limits);
        return result;
    }

    private void RequireRetirementLocalSealCut(IKeyValueView view, ResourceOwnerInventoryResultV1 inventory,
        ResourceRetirementPreparedOperationV1? prepared = null)
    {
        var source = inventory.Control.Directory.Owners.Single(member =>
            PhysicalOwnerEntryValidation.SameOwner(member.Owner, inventory.Control.Catalog.DefaultShard));
        var expected = inventory.OwnerCuts.SingleOrDefault(cut => cut.NodeId == Store.Identity.NodeId
            && PhysicalOwnerEntryValidation.SameOwner(cut.ObservedOwner, source.Owner))
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        var current = CaptureInventoryOwnerCut(view, inventory.Control, source);
        if (prepared is not null)
        {
            if (expected.StorePosition == long.MaxValue)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
            }
            expected = expected with
            {
                StorePosition = expected.StorePosition + ResourceRetirementProtocol.First,
                AppliedIndex = prepared.PersistReceipt!.AppliedPosition
            };
        }
        ResourceOwnerInventoryReadValidation.Cut(expected, current);
    }
}
