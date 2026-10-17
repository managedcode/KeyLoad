using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string InvalidEventVectorControlAuthority = "The native event vector control authority is inconsistent.";

    private AtomicPartitionPlacementResolution AuthorizeEventVectorControl(IKeyValueView view,
        PrincipalRecord principal, ReplicatedOperation operation)
    {
        var current = Principal(view, principal.Id, EvaluationClock.GetUtcNow());
        var phase = Payload<EventVectorControlPhase>(operation);
        var placement = RequireEventVectorControlView(view, current, phase);
        var admission = new EventVectorAdmissionPolicy(OperationLimitsOptions);
        var identity = EventVectorControlOperationIdentity.Create(phase, current.Id, admission);
        if (operation.Id != identity.CommandId || operation.Kind != OperationKind.EventFeedControl
            || operation.PayloadJson != identity.Metadata)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidEventVectorControlAuthority);
        }
        return placement;
    }

    private AtomicPartitionPlacementResolution RequireEventVectorControlView(IKeyValueView view,
        PrincipalRecord principal, EventVectorControlPhase phase)
    {
        var admission = new EventVectorAdmissionPolicy(OperationLimitsOptions);
        ArgumentNullException.ThrowIfNull(phase);
        var request = phase.OriginalRequest;
        if (request?.ControlPartition is null || request.Scope is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidEventVectorControlAuthority);
        }
        Authorization.Require(principal, request.ControlPartition, request.Scope.Resource, Capability.SubscriptionsManage);
        EventVectorControlFields.Require(phase, admission);
        RequireEventVectorControlSources(principal, phase, admission);
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, InvalidEventVectorControlAuthority);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        var placement = ResolveRegisteredPlacement(view, request.ControlPartition, catalog.DefaultShard);
        var owner = ResolveRegisteredPlacementOwner(view, catalog.DefaultShard,
            placement.PhysicalShardId, ErrorCode.OwnershipLost);
        placement = StreamTraversalPlacement(view, request.ControlPartition, owner);
        var originalOutcome = EventVectorControlReplay.Read(view, principal, phase, admission, Store.Identity.Incarnation);
        if (!originalOutcome && phase.OriginalParentExpiresAt <= EvaluationClock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidEventVectorControlAuthority);
        }
        var inventory = EventVectorMapInventory.Read(view, request.ControlPartition, request.MapId,
            OperationLimitsOptions, CancellationToken.None);
        if (inventory.Header is { } map)
        {
            EventVectorParentInventoryAuthorization.Require(Authorization, principal, inventory, admission);
            EventVectorInventoryValidation.Require(map, inventory.PayloadRows, admission);
            RequireEventVectorMapAuthority(map, phase, principal, originalOutcome, view, inventory, admission);
            if (!originalOutcome)
            {
                RequireEventVectorControlTokens(map, inventory, phase);
            }
        }
        else if (EventVectorFirstControlFields.IsIssuance(phase.Action))
        {
            EventVectorFirstIssuanceAdmission.Require(view, principal, phase, originalOutcome,
                Store.Identity.Incarnation, Authorization, admission);
        }
        else if (phase.Action != EventVectorControlAction.Open && !originalOutcome)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidEventVectorControlAuthority);
        }
        return placement;
    }

    private void RequireEventVectorControlSources(PrincipalRecord principal,
        EventVectorControlPhase phase, EventVectorAdmissionPolicy admission)
    {
        var scopes = EventVectorParentScopes.Derive(phase, null, admission);
        EventVectorParentScopes.Authorize(Authorization, principal, scopes, admission);
    }

    private void RequireEventVectorMapAuthority(EventVectorMap map,
        EventVectorControlPhase phase, PrincipalRecord principal, bool originalOutcome,
        IKeyValueView view, EventVectorOwnedInventory inventory, EventVectorAdmissionPolicy admission)
    {
        if (map.ControlIncarnation != Store.Identity.Incarnation)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, InvalidEventVectorControlAuthority);
        }
        if (map.ExpiresAt <= EvaluationClock.GetUtcNow()
            && phase.OriginalRequest.Action != EventFeedControlAction.Release)
        {
            throw Errors.Fail(ErrorCode.CursorExpired, InvalidEventVectorControlAuthority);
        }
        if (map.PrincipalId != principal.Id || map.ControlPolicyEpoch != principal.PolicyEpoch
            || map.Released && !originalOutcome)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidEventVectorControlAuthority);
        }
        if (originalOutcome)
        {
            return;
        }
        if (phase.Action == EventVectorControlAction.Open)
        {
            if (map.OriginalOpenCommandId != phase.OriginalRequest.CommandId)
            {
                throw Errors.Fail(ErrorCode.Conflict, InvalidEventVectorControlAuthority);
            }
            throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidEventVectorControlAuthority);
        }
        var pendingMatches = map.PendingPhaseId == phase.ExpectedPendingSourcePhaseId
            || phase.Action == EventVectorControlAction.AdmitSource
                && map.PendingPhaseId == phase.IntendedSourcePhase?.PhaseCommandId
            || EventVectorCleanupParentCommit.MatchesInterruptedPending(view, inventory, principal, phase, Authorization, admission);
        if (map.Revision != phase.ExpectedMapRevision || map.CoverageGeneration != phase.ExpectedCoverageGeneration
            || map.CleanupGeneration != phase.ExpectedCleanupGeneration || !pendingMatches)
        {
            throw Errors.Fail(ErrorCode.Conflict, InvalidEventVectorControlAuthority);
        }
    }
}
