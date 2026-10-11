using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int EventVectorOfferWitnessVersion = 1;
    private const string InvalidEventVectorOfferPreparation = "The current event vector offer preparation is inconsistent.";

    internal EventVectorPreparedOffer PrepareVerifiedEventVectorOffer(string principalId,
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
            if (request.ControlPartition is null || request.Scope is null)
            { throw Errors.Fail(ErrorCode.Validation, InvalidEventVectorOfferPreparation); }
            Authorization.Require(principal, request.ControlPartition, request.Scope.Resource, Capability.SubscriptionsManage);
            _ = StreamTraversalPlacement(bounded, request.ControlPartition, controlOwner);
            var admission = new EventVectorAdmissionPolicy(OperationLimitsOptions);
            _ = EventVectorCurrentSourceAuthorization.Require(bounded, principal, request,
                ImmutableArray<EventVectorAuthorizationScope>.Empty, freshSourceWitness, admission);
            var inventory = EventVectorMapInventory.Read(bounded, request.ControlPartition, request.MapId,
                OperationLimitsOptions, cancellationToken);
            try
            { return PrepareEventVectorOfferOwned(bounded, principal, request, inventory, freshSourceWitness, admission, work); }
            finally { ClearEventVectorOfferInventory(inventory); }
        });
    }

    private EventVectorPreparedOffer PrepareEventVectorOfferOwned(IKeyValueView bounded, PrincipalRecord principal,
        EventFeedControlRequest request, EventVectorOwnedInventory inventory, ReadOnlyMemory<byte> freshSourceWitness,
        EventVectorAdmissionPolicy admission, ReadExecutionBudget work)
    {
        var map = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidEventVectorOfferPreparation);
        if (request.Action is not (EventFeedControlAction.Offer or EventFeedControlAction.Tail)
            || map.State != EventVectorMapState.Active || map.Released || map.PendingPhaseId is not null
            || map.PrincipalId != principal.Id || map.ControlPolicyEpoch != principal.PolicyEpoch
            || map.ControlIncarnation != Store.Identity.Incarnation
            || map.Revision != request.ExpectedRevision || map.CoverageGeneration != request.ExpectedCoverageGeneration
            || map.ExpiresAt <= EvaluationClock.GetUtcNow() || request.Cursor is null)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidEventVectorOfferPreparation); }
        RequireEventVectorToken(map, Verify<EventVectorToken>(request.Cursor), EventVectorMapTokenPurpose, map.SourceManifestDigest);
        EventVectorParentInventoryAuthorization.Require(Authorization, principal, inventory, admission);
        EventVectorInventoryValidation.Require(map, inventory.PayloadRows, admission);
        var offerKey = EventVectorKeys.Offer(map.ControlPartition, map.MapId);
        if (inventory.PayloadRows.Any(row => row.Key.Span.SequenceEqual(offerKey)))
        { throw Errors.Fail(ErrorCode.Conflict, InvalidEventVectorOfferPreparation); }
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
        _ = EventVectorCurrentSourceAuthorization.Require(bounded, principal, request, required, freshSourceWitness, admission);
        var entries = EventVectorControlPages.Entries(inventory, admission);
        var pins = EventVectorOfferPinSelection.Read(inventory, entries, admission);
        return EventVectorOfferOwnedEncoding.Create(map, entries, witness, pins, admission, work);
    }

    private static void ClearEventVectorOfferInventory(EventVectorOwnedInventory inventory)
    {
        if (inventory.HeaderRow is { } header)
        { CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(header.Value).Span); }
        foreach (var row in inventory.PayloadRows)
        { CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(row.Value).Span); }
    }
}
