using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private OperationResult ExecuteResourceRetirementPreparedOperation(IAtomicTransaction transaction,
        PrincipalRecord principal, ReplicatedOperation operation, ResourceRetirementPhaseCommandV1 phase, long position)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementPreparedOperationBodyV1>(phase.Body.Span);
        if (body is null || !principal.ClusterAdministrator)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, ResourceRetirementProtocol.Invalid);
        }
        ResourceRetirementAttemptContextValidation.Require(body.Context, Limits);
        ResourceRetirementAttemptValidation.Witness(body.Attempt, body.Context, Limits);
        var request = ResourceRetirementAttemptContextValidation.Request(body.Context);
        var owner = ResourceRetirementAttemptContextValidation.ExecutionOwner(body.Context);
        var catalog = ReadInventoryPhysicalCatalog(transaction);
        if (phase.RetirementId != request.RetirementId
            || operation.Id != ResourceRetirementIdentity.PreparedOperationCommandId(body.Context, body.Attempt.AttemptId, body.Action)
            || !PhysicalOwnerEntryValidation.SameOwner(catalog.DefaultShard, owner))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
        }
        var receipt = new ResourceRetirementPhaseReceiptV1(ResourceRetirementProtocol.Version,
            request.RetirementId, ResourceRetirementPhase.PreparedOperation, operation.Id,
            ResourceRetirementAttemptContextValidation.Lifetime(body.Context), owner, position,
            ResourceRetirementAttemptContextValidation.IntentDigest(body.Context));
        var authority = new ResourceRetirementPreparedReceiptAuthorityV1(principal.Id, principal.PolicyEpoch,
            NativeOperationFingerprint.Compute(operation), operation.EvaluatedAt);
        var prepared = body.Action switch
        {
            ResourceRetirementPreparedOperationAction.Persist => PersistResourceRetirementOriginal(transaction,
                operation, body, receipt, authority),
            ResourceRetirementPreparedOperationAction.Close => CloseResourceRetirementOriginal(transaction,
                body, receipt, authority),
            _ => throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid)
        };
        var result = new ResourceRetirementPhaseResultV1(receipt, null, null) { PreparedOperation = prepared };
        ResourceRetirementValidation.Bound(result, Limits);
        ResourceRetirementPreparedOperationStorage.Write(transaction, prepared, Limits);
        return Result(result);
    }

    private ResourceRetirementPreparedOperationV1 PersistResourceRetirementOriginal(IKeyValueView view,
        ReplicatedOperation operation, ResourceRetirementPreparedOperationBodyV1 body,
        ResourceRetirementPhaseReceiptV1 receipt, ResourceRetirementPreparedReceiptAuthorityV1 authority)
    {
        if (body.Original is null || body.Original.EvaluatedAt > operation.EvaluatedAt
            || body.Attempt.ExpiresAt <= operation.EvaluatedAt)
        {
            throw Errors.Fail(ErrorCode.CursorExpired, ResourceOwnerInventoryReadValidation.Expired);
        }
        if (ResourceRetirementPreparedOperationStorage.Read(view, body.Context, body.Attempt, Limits) is not null)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
        }
        var original = VerifyOperationAuthority(body.Original);
        var prepared = new ResourceRetirementPreparedOperationV1(body.Context,
            ResourceRetirementPreparedOperationStorage.Prepared(body.Attempt), original,
            NativeOperationFingerprint.Compute(original), ResourceRetirementPreparedOperationStage.Persisted,
            receipt, receipt, null, null)
        {
            ReceiptAuthority = authority,
            PersistAuthority = authority
        };
        RequirePreparedResourceRetirementOriginal(prepared);
        if (body.Context.Purpose == ResourceRetirementAttemptPurpose.SealBarrier)
        {
            var control = body.Context.Retirement!;
            var state = RequireRetirementSource(view, control);
            ResourceRetirementStateValidation.AllOwners(state);
            _ = ResourceRetirementAttemptStorage.RequirePrepared(view, body.Context, body.Attempt, Limits);
            var seal = NativeSerialization.Deserialize<ResourceRetirementSealBodyV1>(
                Payload<ResourceRetirementPhaseCommandV1>(original).Body.Span);
            if (seal.Inventory.Control.Request.ExpiresAt <= operation.EvaluatedAt)
            {
                throw Errors.Fail(ErrorCode.CursorExpired, ResourceOwnerInventoryReadValidation.Expired);
            }
            RequireRetirementLocalSealCut(view, seal.Inventory);
        }
        return prepared;
    }
}
