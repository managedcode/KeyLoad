using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private ResourceRetirementPhaseResultV1 PrepareResourceRetirementAttempt(IAtomicTransaction transaction,
        PrincipalRecord principal, ReplicatedOperation operation, ResourceRetirementPhaseCommandV1 phase, long position)
    {
        var body = NativeSerialization.Deserialize<ResourceRetirementPrepareAttemptBodyV1>(phase.Body.Span);
        if (body is null || body.AttemptId == Guid.Empty || body.ExpectedOrdinal < ResourceRetirementProtocol.Empty
            || body.ExpiresAt <= operation.EvaluatedAt)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }
        ResourceRetirementAttemptContextValidation.Require(body.Context, Limits);
        RequireRetirementAttemptSource(transaction, body.Context);
        if (phase.RetirementId != ResourceRetirementAttemptContextValidation.Request(body.Context).RetirementId
            || operation.Id != ResourceRetirementIdentity.AttemptCommandId(body.Context, phase.Phase, body.AttemptId))
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
        }
        var previous = ResourceRetirementAttemptStorage.Current(transaction, body.Context, Limits);
        if (body.ExpectedOrdinal != (previous?.Ordinal ?? ResourceRetirementProtocol.Empty))
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, ResourceRetirementProtocol.Changed);
        }
        if (body.ExpectedOrdinal >= Limits.MaxResults || body.ExpectedOrdinal == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ResourceRetirementProtocol.Capacity);
        }
        if (ResourceRetirementAttemptStorage.Read(transaction, body.Context, body.AttemptId, Limits) is not null)
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
        }
        var terminal = ClosePreviousRetirementAttempt(transaction, body.Context, previous, body.PreviousClosed);
        var receipt = new ResourceRetirementPhaseReceiptV1(ResourceRetirementProtocol.Version, phase.RetirementId,
            phase.Phase, operation.Id, ResourceRetirementAttemptContextValidation.Lifetime(body.Context),
            ReadInventoryPhysicalCatalog(transaction).DefaultShard, position,
            ResourceRetirementAttemptContextValidation.IntentDigest(body.Context));
        var originalPhase = ResourceRetirementCleanupAttemptScope.OriginalPhase(body.Context);
        var attempt = new ResourceRetirementAttemptV1(body.AttemptId,
            body.ExpectedOrdinal + ResourceRetirementProtocol.First, body.ExpiresAt, receipt,
            ResourceRetirementAttemptStage.Prepared, principal.Id, principal.PolicyEpoch,
            ResourceRetirementIdentity.AttemptCommandId(body.Context, originalPhase, body.AttemptId),
            null, null, null, null);
        var result = WriteRetirementAttemptPointer(transaction, body.Context, attempt, receipt);
        if (terminal is not null)
        {
            ResourceRetirementAttemptStorage.Write(transaction, body.Context, terminal, Limits);
        }
        ResourceRetirementAttemptStorage.Write(transaction, body.Context, attempt, Limits);
        return result;
    }

    private ResourceRetirementAttemptV1? ClosePreviousRetirementAttempt(IAtomicTransaction transaction, ResourceRetirementAttemptContextV1 context,
        ResourceRetirementAttemptV1? previous, ResourceRetirementPreparedOperationV1? closed)
    {
        if (previous is null)
        {
            if (closed is not null)
            {
                throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
            }
            return null;
        }
        var retained = ResourceRetirementAttemptStorage.Read(transaction, context, previous.AttemptId, Limits);
        if (!ResourceRetirementValidation.Same(retained, previous) || previous.Stage == ResourceRetirementAttemptStage.Completed)
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
        }
        if (previous.Stage != ResourceRetirementAttemptStage.Prepared)
        {
            return null;
        }
        if (closed is null)
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
        }
        ResourceRetirementPreparedOperationStorage.RequireClosed(closed, context, previous, Limits);
        return previous with
        {
            Stage = closed.Error == ErrorCode.CursorExpired
            ? ResourceRetirementAttemptStage.Expired : ResourceRetirementAttemptStage.Failed,
            OriginalFingerprint = closed.Fingerprint,
            Error = closed.Error,
            SafeDetail = closed.SafeDetail
        };
    }
}
