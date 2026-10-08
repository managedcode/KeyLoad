using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal OperationResult ResolveControlledDocumentOutcome(string authenticatedPrincipalId,
        ReplicatedOperation originalOperation, ReadExecutionBudget work)
    {
        work.Check();
        var original = VerifyOperationAuthority(originalOperation);
        if (original.PrincipalId != authenticatedPrincipalId)
        { throw Errors.Fail(ErrorCode.Unauthenticated, ClusterAdministrationRequiredMessage); }
        var scope = CommandOutcomePartitionIdentity.Resolve(original);
        if (scope.Partition is null)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedOperationMessage); }
        var command = RequireControlledDocumentBatch(original, scope.Partition);
        var result = Store.Read(view => ResolveControlledDocumentOutcomeView(work.CreateView(view), original, command));
        work.MeasureResult(result);
        work.Check();
        return result;
    }

    private OperationResult ResolveControlledDocumentOutcomeView(IKeyValueView view,
        ReplicatedOperation original, CommandRequest command)
    {
        var directory = RequireMoveDirectory(view);
        if (directory.ControlOwner.Incarnation != Store.Identity.Incarnation)
        { throw Errors.Fail(ErrorCode.OwnershipLost, Features.ClusterRouting.Contracts.PartitionMoveProtocol.OwnerMismatch); }
        var principal = Principal(view, original.PrincipalId, EvaluationClock.GetUtcNow());
        var resources = PartitionMoveResources.Capture(view, command.Partition, Limits);
        RequireControlledDocumentPolicies(principal, command, resources);
        var scope = CommandOutcomePartitionIdentity.Resolve(original);
        var selected = CommandOutcomeKeyResolver.Select(view, principal.Id, original.Id, scope);
        var previous = selected.Outcome
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, MissingDurableOutcomeMessage);
        if (previous.Incarnation != Store.Identity.Incarnation)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, EarlierOutcomeIncarnationMessage); }
        if (previous.Fingerprint != CommandFingerprint(original))
        { throw Errors.Fail(ErrorCode.Conflict, CommandContentConflictMessage); }
        CommandOutcomeKeyResolver.ValidateSelectedScope(view, original, selected);
        ValidateCachedResult(view, principal, original, previous);
        return previous.Result;
    }
}
