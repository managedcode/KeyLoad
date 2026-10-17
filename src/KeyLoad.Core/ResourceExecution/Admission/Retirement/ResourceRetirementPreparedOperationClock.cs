using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal bool PreservePreparedResourceRetirementEvaluation(ReplicatedOperation operation, DateTimeOffset admittedAt)
        => PreservePreparedResourceRetirementEvaluationCore(operation, admittedAt, null);

    internal bool PreservePreparedResourceRetirementEvaluation(ReplicatedOperation operation, DateTimeOffset admittedAt,
        ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        return PreservePreparedResourceRetirementEvaluationCore(operation, admittedAt, work);
    }

    private bool PreservePreparedResourceRetirementEvaluationCore(ReplicatedOperation operation, DateTimeOffset admittedAt,
        ReadExecutionBudget? work)
    {
        if (!TryReadResourceRetirementPreparedIdentity(operation, out var context, out var attempt))
        {
            return false;
        }
        return Store.Read(raw =>
        {
            var view = work?.CreateView(raw) ?? raw;
            var scope = CommandOutcomePartitionIdentity.Resolve(operation);
            if (CommandOutcomeKeyResolver.Select(view, operation.PrincipalId, operation.Id, scope).Outcome is not null)
            {
                _ = ReadValidatedSubmissionOutcome(view, operation);
                return false;
            }
            _ = RequireExactPreparedResourceRetirementOperation(view, operation, context!, attempt!);
            RequirePreparedResourceRetirementLeaderDeadline(attempt!, admittedAt);
            return true;
        });
    }

    private void RequirePreparedResourceRetirementSubmission(IKeyValueView view, ReplicatedOperation operation,
        DateTimeOffset admittedAt)
    {
        RequireResourceRetirementCloseSubmission(view, operation, admittedAt);
        if (TryReadResourceRetirementPreparedIdentity(operation, out var context, out var attempt))
        {
            _ = RequireExactPreparedResourceRetirementOperation(view, operation, context!, attempt!);
            RequirePreparedResourceRetirementLeaderDeadline(attempt!, admittedAt);
        }
    }

    private bool RequirePreparedResourceRetirementClock(IKeyValueView view, ReplicatedOperation operation)
    {
        if (!TryReadResourceRetirementPreparedIdentity(operation, out var context, out var attempt))
        {
            return false;
        }
        _ = RequireExactPreparedResourceRetirementOperation(view, operation, context!, attempt!);
        if (context!.Purpose == ResourceRetirementAttemptPurpose.SealBarrier)
        {
            try
            {
                _ = ResourceRetirementAttemptStorage.RequirePrepared(view, context, attempt!, Limits);
            }
            catch (KeyLoadException error) when (error.Code == ErrorCode.Conflict)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
            }
        }
        RequirePreparedResourceRetirementCommittedDeadline(view, attempt!);
        return true;
    }

    private static void RequirePreparedResourceRetirementLeaderDeadline(ResourceRetirementAttemptV1 attempt,
        DateTimeOffset admittedAt)
    {
        if (admittedAt >= attempt.ExpiresAt)
        {
            throw Errors.Fail(ErrorCode.CursorExpired, ResourceOwnerInventoryReadValidation.Expired);
        }
    }

    private static void RequirePreparedResourceRetirementCommittedDeadline(IKeyValueView view,
        ResourceRetirementAttemptV1 attempt)
    {
        view.ReadValue(KeySpace.ClockBytes, bytes =>
        {
            if (NativeSerialization.Deserialize<DateTimeOffset>(bytes) >= attempt.ExpiresAt)
            {
                throw Errors.Fail(ErrorCode.CursorExpired, ResourceOwnerInventoryReadValidation.Expired);
            }
        });
    }

    private bool TryReadResourceRetirementPreparedIdentity(ReplicatedOperation operation,
        out ResourceRetirementAttemptContextV1? context, out ResourceRetirementAttemptV1? attempt)
    {
        context = null;
        attempt = null;
        if (operation.Kind != OperationKind.ResourceRetirementPhase || operation.NativePayload.IsEmpty)
        {
            return false;
        }
        var phase = Payload<ResourceRetirementPhaseCommandV1>(operation);
        if (phase.Phase == ResourceRetirementPhase.SealBarrier)
        {
            var body = NativeSerialization.Deserialize<ResourceRetirementSealBodyV1>(phase.Body.Span)
                ?? throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
            context = new(ResourceRetirementAttemptPurpose.SealBarrier, body.Control, null, null);
            attempt = body.Attempt;
        }
        else if (phase.Phase == ResourceRetirementPhase.ReserveOwner)
        {
            var body = NativeSerialization.Deserialize<ResourceRetirementReserveOwnerBodyV1>(phase.Body.Span)
                ?? throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
            context = body.Context;
            attempt = body.Attempt;
        }
        else if (phase.Phase == ResourceRetirementPhase.CleanupPage)
        {
            var body = NativeSerialization.Deserialize<ResourceRetirementCleanupPageBodyV1>(phase.Body.Span);
            _ = ResourceRetirementCleanupPageValidation.Require(body, Limits);
            context = ResourceRetirementCleanupAttemptScope.Context(body);
            attempt = body.Attempt;
        }
        else
        {
            return false;
        }
        if (context is null || attempt is null)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
        }
        return true;
    }
}
