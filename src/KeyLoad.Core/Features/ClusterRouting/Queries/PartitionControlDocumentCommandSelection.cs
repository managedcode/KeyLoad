using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal PartitionControlDocumentCommandContext? TryCaptureControlledDocumentCommand(string principalId,
        ReplicatedOperation originalOperation, ReadExecutionBudget work)
    {
        work.Check();
        var original = VerifyOperationAuthority(originalOperation);
        if (original.PrincipalId != principalId)
        { throw Errors.Fail(ErrorCode.Unauthenticated, ClusterAdministrationRequiredMessage); }
        var scope = CommandOutcomePartitionIdentity.Resolve(original);
        if (scope.Partition is null)
        { return null; }
        var result = Store.Read(view => CaptureControlledDocumentCommandView(work.CreateView(view), original, scope));
        work.MeasureResult(result);
        work.Check();
        return result;
    }

    private PartitionControlDocumentCommandContext? CaptureControlledDocumentCommandView(IKeyValueView view,
        ReplicatedOperation original, CommandOutcomePartitionScope scope)
    {
        var principal = Principal(view, original.PrincipalId, EvaluationClock.GetUtcNow());
        var publication = PartitionMovePublishedPlacementStorage.Read(view, scope.Partition!);
        if (publication is null)
        { return null; }
        var command = RequireControlledDocumentBatch(original, scope.Partition!);
        RequireControlledDocumentPolicies(principal, command, PartitionMoveResources.Capture(view, command.Partition, Limits));
        var control = PartitionMoveControlStorage.ReadHistory(view, command.Partition, publication.MoveId,
            Limits.MaxBatchBytes) ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var controlPrincipal = Principal(view, control.PrincipalId, EvaluationClock.GetUtcNow());
        _ = RequireRetiredCommandControl(view, controlPrincipal, control, out var placement);
        var identity = new PartitionControlCommandIdentity(scope.Kind, scope.Partition, original.PrincipalId, original.Id);
        var record = PartitionControlCommandStorage.Read(view, identity, Limits.MaxBatchBytes);
        if (record is not null && record.Fingerprint != CommandFingerprint(original))
        { throw Errors.Fail(ErrorCode.Conflict, CommandContentConflictMessage); }
        var previous = CommandOutcomeKeyResolver.Select(view, original.PrincipalId, original.Id, scope).Outcome;
        var outcome = previous is null ? null : ResolveControlledDocumentOutcomeView(view, original, command);
        return new(control, placement, controlPrincipal.Id, identity, record, outcome);
    }
}
