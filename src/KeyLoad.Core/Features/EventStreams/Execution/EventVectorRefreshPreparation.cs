using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int EventVectorRefreshVersion = 1;
    private const long EventVectorRefreshCoverageStep = 1;
    private const string InvalidEventVectorRefreshPreparation = "The current event vector refresh requires recovery.";

    internal EventVectorPreparedRefresh PrepareVerifiedEventVectorRefresh(string principalId,
        EventFeedControlRequest request, PhysicalShardRecord controlOwner,
        ReadOnlyMemory<byte> freshSourceWitness, DateTimeOffset originalExpiry, Guid originalRequestId,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(controlOwner);
        ArgumentNullException.ThrowIfNull(work);
        cancellationToken.ThrowIfCancellationRequested();
        return Store.Read(view =>
        {
            var bounded = work.CreateView(view);
            var principal = Principal(bounded, principalId, EvaluationClock.GetUtcNow());
            if (request.ControlPartition is null || request.Scope is null
                || request.Action != EventFeedControlAction.Refresh
                || request.OriginalFirstIssuancePublicCommandId != Guid.Empty
                || originalRequestId == Guid.Empty || originalExpiry <= EvaluationClock.GetUtcNow())
            { throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidEventVectorRefreshPreparation); }
            Authorization.Require(principal, request.ControlPartition, request.Scope.Resource, Capability.SubscriptionsManage);
            _ = StreamTraversalPlacement(bounded, request.ControlPartition, controlOwner);
            var admission = new EventVectorAdmissionPolicy(OperationLimitsOptions);
            _ = EventVectorCurrentSourceAuthorization.Require(bounded, principal, request,
                ImmutableArray<EventVectorAuthorizationScope>.Empty, freshSourceWitness, admission);
            var inventory = EventVectorMapInventory.Read(bounded, request.ControlPartition, request.MapId,
                OperationLimitsOptions, cancellationToken);
            try
            {
                return PrepareEventVectorRefreshOwned(bounded, principal, request, inventory,
                    controlOwner, freshSourceWitness, originalExpiry, originalRequestId, admission, work);
            }
            finally { ClearEventVectorOfferInventory(inventory); }
        });
    }

    private EventVectorPreparedRefresh PrepareEventVectorRefreshOwned(IKeyValueView bounded,
        PrincipalRecord principal, EventFeedControlRequest request, EventVectorOwnedInventory inventory,
        PhysicalShardRecord controlOwner, ReadOnlyMemory<byte> freshSourceWitness, DateTimeOffset originalExpiry, Guid originalRequestId,
        EventVectorAdmissionPolicy admission, ReadExecutionBudget work)
    {
        var map = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidEventVectorRefreshPreparation);
        if (map.State != EventVectorMapState.Active || map.Released || map.PendingPhaseId is not null
            || map.PrincipalId != principal.Id || map.ControlPolicyEpoch != principal.PolicyEpoch
            || map.ControlIncarnation != Store.Identity.Incarnation || map.Revision != request.ExpectedRevision
            || map.CoverageGeneration != request.ExpectedCoverageGeneration)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidEventVectorRefreshPreparation); }
        EventVectorParentInventoryAuthorization.Require(Authorization, principal, inventory, admission);
        EventVectorInventoryValidation.Require(map, inventory.PayloadRows, admission);
        if (inventory.PayloadRows.Any(row => row.Key.Span.SequenceEqual(
                EventVectorKeys.Offer(map.ControlPartition, map.MapId))))
        { throw Errors.Fail(ErrorCode.Conflict, InvalidEventVectorRefreshPreparation); }
        var phase = new EventVectorControlPhase(EventVectorRefreshVersion, request, EventVectorControlAction.PublishRefresh,
            map.Revision, map.CoverageGeneration, map.CleanupGeneration, null, null, null,
            ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty)
        {
            OriginalCoverageWitness = freshSourceWitness,
            OriginalParentExpiresAt = originalExpiry,
            OriginalParentRequestId = originalRequestId
        };
        RequireEventVectorControlTokens(map, inventory, phase);
        var scopes = EventVectorParentScopes.Derive(phase, inventory, admission);
        EventVectorParentScopes.Authorize(Authorization, principal, scopes, admission);
        var fresh = EventVectorCurrentSourceAuthorization.Require(bounded, principal, request,
            scopes, freshSourceWitness, admission);
        var proof = EventVectorControlProof.Create(phase,
            checked(map.CoverageGeneration + EventVectorRefreshCoverageStep), admission);
        using var decoded = new EventVectorFirstOpenDecodedOwner(fresh);
        var prior = EventVectorControlPages.Entries(inventory, admission);
        var candidates = EventVectorRefreshEntries.Create(prior, proof, admission);
        var pins = EventVectorSettledPinSelection.Read(inventory, prior, admission);
        return OwnEventVectorRefresh(request, map, controlOwner, prior, candidates, fresh, pins, admission, work);
    }

    private static EventVectorPreparedRefresh OwnEventVectorRefresh(EventFeedControlRequest request,
        EventVectorMap map, PhysicalShardRecord controlOwner,
        ImmutableArray<EventVectorEntry> prior, ImmutableArray<EventVectorEntry> candidates,
        EventVectorCoverageWitness witness, ImmutableArray<EventVectorSourcePhase> pins,
        EventVectorAdmissionPolicy admission, ReadExecutionBudget work)
    {
        var encoded = EventVectorSettlementTargetEncoding.Encode(candidates, admission, work);
        var encodedWitness = ReadOnlyMemory<byte>.Empty;
        var encodedPins = ImmutableArray.CreateBuilder<ReadOnlyMemory<byte>>(pins.Length);
        var transferred = false;
        try
        {
            encodedWitness = EventVectorOfferOwnedEncoding.Encode(witness, admission, work);
            foreach (var pin in pins)
            { encodedPins.Add(EventVectorOfferOwnedEncoding.Encode(pin, admission, work)); }
            work.Check();
            var result = new EventVectorPreparedRefresh(request, map, controlOwner, prior, encodedWitness, encodedPins.ToImmutable(),
                candidates, encoded.Bytes, encoded.Digest);
            transferred = true;
            return result;
        }
        finally
        {
            if (!transferred)
            {
                EventVectorSettlementTargetEncoding.Clear(encoded.Bytes);
                EventVectorSettlementTargetEncoding.Clear(encoded.Digest);
                EventVectorSettlementTargetEncoding.Clear(encodedWitness);
                foreach (var pin in encodedPins)
                { EventVectorSettlementTargetEncoding.Clear(pin); }
            }
        }
    }
}
