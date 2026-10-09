using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal OperationResult ResolveVerifiedPartitionMovementOutcome(string localPrincipalId,
        PartitionMovePeerEnvelope original, Guid phaseCommandId, ReadExecutionBudget work, ReadExecutionBudgetReadGrant grant)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(grant);
        work.Check();
        if (original.Grant is { RequireReceiverIssuance: true })
        { movementCheckpointVerifier.RequireReceiverAdministrator(this, localPrincipalId, work); }
        PartitionMovePeerEnvelopeValidation.RequireStructure(original, Limits.MaxBatchBytes);
        if (phaseCommandId == Guid.Empty || string.IsNullOrWhiteSpace(localPrincipalId))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        if (original.Grant is { } originalGrant && originalGrant.PhaseCommandId != phaseCommandId)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        var expectedIdentity = OriginalMovePhaseIdentity(original, phaseCommandId, localPrincipalId);
        var result = Store.Read(view => ResolveMovePhaseOutcome(work.CreateView(view, grant), original,
            phaseCommandId, localPrincipalId, expectedIdentity));
        work.MeasureResult(result);
        work.Check();
        return result;
    }

    private OperationResult ResolveMovePhaseOutcome(IKeyValueView view, PartitionMovePeerEnvelope original,
        Guid phaseCommandId, string principalId, string expectedIdentity)
    {
        var principal = RequireMoveDispatchPrincipal(view, principalId, EvaluationClock.GetUtcNow());
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        RequireOriginalMoveOutcomeReceiver(original, catalog.DefaultShard);
        var originalEpoch = original.Grant is { RequireReceiverIssuance: true }
            ? RequireMoveReceiverOriginalObservationEpoch(view, principal, original, phaseCommandId)
            : principal.PolicyEpoch;
        var scope = new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, original.Partition);
        var selected = CommandOutcomeKeyResolver.Select(view, principal.Id, phaseCommandId, scope);
        var outcome = selected.Outcome
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, MissingDurableOutcomeMessage);
        if (outcome.Incarnation != Store.Identity.Incarnation)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, EarlierOutcomeIncarnationMessage); }
        if (outcome.Fingerprint != expectedIdentity)
        { throw Errors.Fail(ErrorCode.Conflict, CommandContentConflictMessage); }
        if (outcome.PolicyEpoch != originalEpoch)
        { throw Errors.Fail(ErrorCode.PermissionDenied, ChangedOutcomePrincipalPolicyMessage); }
        return outcome.Result;
    }

    private string OriginalMovePhaseIdentity(PartitionMovePeerEnvelope original, Guid commandId,
        string principalId)
    {
        var command = new PartitionMovePhaseCommand(original.Version, original.MoveId, original.Partition,
            original.ControlOwner, original.SourcePlacement, original.DestinationOwner,
            original.ControlIntentDigest, original.Stage, original.PageOrdinal, original.Body,
            original.Grant?.GrantId, original.Grant?.Resources
                ?? System.Collections.Immutable.ImmutableArray<ResourceDefinition>.Empty);
        var identity = MovementPhaseIdentityJson(command);
        return CommandFingerprint(new(commandId, OperationKind.PartitionMovementPhase, principalId,
            default, identity));
    }
}
