using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal PartitionControlGrantSnapshot CaptureControlledDocumentGrant(
        PartitionMovePeerEnvelope originalAuthorize, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        PartitionMovePeerEnvelopeValidation.RequireStructure(originalAuthorize, Limits.MaxBatchBytes);
        if (originalAuthorize.Stage != PartitionMovePeerStage.ControlAuthorize || originalAuthorize.Grant is not null)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var body = NativeSerialization.Deserialize<PartitionMoveAuthorizeBody>(originalAuthorize.Body.Span);
        if (body.GrantId == Guid.Empty || body.Phase.Stage != PartitionMovePeerStage.ControlApplyCommand)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var snapshot = Store.Read(view => ReadControlledDocumentGrant(work.CreateView(view), originalAuthorize, body));
        work.MeasureResult(snapshot);
        work.Check();
        return snapshot;
    }

    private PartitionControlGrantSnapshot ReadControlledDocumentGrant(IKeyValueView view,
        PartitionMovePeerEnvelope original, PartitionMoveAuthorizeBody body)
    {
        var directory = RequireMoveDirectory(view);
        if (!PhysicalOwnerEntryValidation.SameOwner(directory.ControlOwner, original.ControlOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var principal = RequireMoveDispatchPrincipal(view, body.OperatorPrincipalId, EvaluationClock.GetUtcNow());
        var control = PartitionMoveControlStorage.ReadHistory(view, original.Partition, original.MoveId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        _ = RequireRetiredCommandControl(view, principal, control, out _);
        var grant = PartitionMoveGrantStorage.Read(view, original.Partition, body.GrantId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        RequireMoveOutcomeDirectory(original, grant, directory);
        RequireMovePhaseIdentity(body.Phase, control);
        var outcome = ResolveMovePhaseOutcome(view, original, body.GrantId, principal.Id,
            OriginalMovePhaseIdentity(original, body.GrantId, principal.Id));
        if (outcome.Error is not null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        var first = outcome.Get<PartitionMovePhaseResult>();
        RequireControlledGrantSnapshot(original, body, grant, first, principal);
        return new(grant, first.Journal);
    }
}
