using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string InvalidEventVectorSettlement = "The original event vector settlement requires recovery.";

    internal EventVectorPreparedSettlement PrepareVerifiedEventVectorSettlement(string principalId,
        EventFeedControlRequest request, PhysicalShardRecord controlOwner,
        ReadOnlyMemory<byte> freshSourceWitness, ReadExecutionBudget work, CancellationToken cancellationToken)
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
                || request.Action is not (EventFeedControlAction.Acknowledge or EventFeedControlAction.Release)
                || request.OriginalFirstIssuancePublicCommandId != Guid.Empty)
            { throw Errors.Fail(ErrorCode.Validation, InvalidEventVectorSettlement); }
            Authorization.Require(principal, request.ControlPartition, request.Scope.Resource, Capability.SubscriptionsManage);
            _ = StreamTraversalPlacement(bounded, request.ControlPartition, controlOwner);
            var admission = new EventVectorAdmissionPolicy(OperationLimitsOptions);
            _ = EventVectorCurrentSourceAuthorization.Require(bounded, principal, request,
                ImmutableArray<EventVectorAuthorizationScope>.Empty, freshSourceWitness, admission);
            var inventory = EventVectorMapInventory.Read(bounded, request.ControlPartition, request.MapId,
                OperationLimitsOptions, cancellationToken);
            try
            {
                return PrepareEventVectorSettlementOwned(bounded, principal, request, inventory,
                freshSourceWitness, admission, work);
            }
            finally { ClearEventVectorOfferInventory(inventory); }
        });
    }

    private EventVectorPreparedSettlement PrepareEventVectorSettlementOwned(IKeyValueView bounded,
        PrincipalRecord principal, EventFeedControlRequest request, EventVectorOwnedInventory inventory,
        ReadOnlyMemory<byte> freshSourceWitness, EventVectorAdmissionPolicy admission, ReadExecutionBudget work)
    {
        var map = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidEventVectorSettlement);
        if (map.State != EventVectorMapState.Active || map.Released || map.PendingPhaseId is not null
            || map.PrincipalId != principal.Id || map.ControlPolicyEpoch != principal.PolicyEpoch
            || map.ControlIncarnation != Store.Identity.Incarnation || map.Revision != request.ExpectedRevision
            || map.CoverageGeneration != request.ExpectedCoverageGeneration)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidEventVectorSettlement); }
        EventVectorParentInventoryAuthorization.Require(Authorization, principal, inventory, admission);
        EventVectorInventoryValidation.Require(map, inventory.PayloadRows, admission);
        var tokenPhase = new EventVectorControlPhase(EventVectorTokenVersion, request,
            EventVectorControlAction.AdmitSource, map.Revision, map.CoverageGeneration,
            map.CleanupGeneration, null, null, null, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty);
        RequireEventVectorControlTokens(map, inventory, tokenPhase);
        var entries = EventVectorControlPages.Entries(inventory, admission);
        var targets = ReadEventVectorSettlementTargets(request, map, inventory, entries, admission);
        var proof = EventVectorControlPublication.Proof(inventory, admission);
        var witness = new EventVectorCoverageWitness
        {
            Version = EventVectorOfferWitnessVersion,
            OriginalSemanticDigest = proof.OriginalSemanticDigest,
            OriginalPackets = proof.OriginalPackets,
            FreshPackets = proof.OriginalPackets,
            OriginalSemanticProjectionBytes = proof.OriginalSemanticProjectionBytes
        };
        using var decoded = new EventVectorFirstOpenDecodedOwner(witness);
        var scopes = new SortedDictionary<string, EventVectorAuthorizationScope>(StringComparer.Ordinal);
        EventVectorParentCaptureScopes.Add(scopes, decoded.Projection, request.Scope, admission);
        var required = scopes.Values.ToImmutableArray();
        EventVectorParentScopes.Authorize(Authorization, principal, required, admission);
        var fresh = EventVectorCurrentSourceAuthorization.Require(bounded, principal, request, required, freshSourceWitness, admission);
        var pins = EventVectorSettledPinSelection.Read(inventory, entries, admission);
        var currentPolicyEpochs = EventVectorSettlementCurrentPolicy.Read(request, entries, pins, fresh, admission, work);
        return OwnEventVectorSettlementTargets(request, map, inventory, entries, witness, pins,
            targets, currentPolicyEpochs, admission, work);
    }
    private static EventVectorPreparedSettlement OwnEventVectorSettlementTargets(EventFeedControlRequest request,
        EventVectorMap map, EventVectorOwnedInventory inventory, ImmutableArray<EventVectorEntry> entries,
        EventVectorCoverageWitness witness, ImmutableArray<EventVectorSourcePhase> pins,
        ImmutableArray<EventVectorEntry> targets, ImmutableArray<long> currentPolicyEpochs, EventVectorAdmissionPolicy admission, ReadExecutionBudget work)
    {
        var encoded = request.Action == EventFeedControlAction.Acknowledge
            ? EventVectorSettlementTargetEncoding.Copy(ReadEventVectorOffer(map, inventory), admission, work)
            : EventVectorSettlementTargetEncoding.Encode(targets, admission, work);
        var transferred = false;
        try
        {
            var result = EventVectorSettlementOwnedEncoding.Create(map, entries, witness, pins,
                targets, encoded.Bytes, encoded.Digest, currentPolicyEpochs, admission, work);
            transferred = true;
            return result;
        }
        finally
        {
            if (!transferred)
            {
                EventVectorSettlementTargetEncoding.Clear(encoded.Bytes);
                EventVectorSettlementTargetEncoding.Clear(encoded.Digest);
            }
        }
    }

    private static ImmutableArray<EventVectorEntry> ReadEventVectorSettlementTargets(EventFeedControlRequest request,
        EventVectorMap map, EventVectorOwnedInventory inventory, ImmutableArray<EventVectorEntry> entries,
        EventVectorAdmissionPolicy admission)
    {
        if (request.Action == EventFeedControlAction.Release)
        { return entries; }
        var offer = ReadEventVectorOffer(map, inventory);
        return EventVectorEntryEncoding.Decode(offer.Entries, offer.PageDigest, admission);
    }
}
