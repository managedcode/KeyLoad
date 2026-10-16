using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int EventVectorTailVersion = 1;
    private const string InvalidEventVectorTail = "The current event feed tail observation requires recovery.";

    internal EventVectorParentObservation ReadEventVectorTailOffer(string principalId,
        EventFeedControlRequest request, PhysicalShardRecord controlOwner,
        ReadOnlyMemory<byte> freshSourceWitness, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(controlOwner);
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        return Store.Read(view =>
        {
            var bounded = work.CreateView(view);
            var principal = Principal(bounded, principalId, EvaluationClock.GetUtcNow());
            if (request.Action != EventFeedControlAction.Tail || request.ControlPartition is null
                || request.Scope is null || request.Cursor is null || freshSourceWitness.IsEmpty)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidEventVectorTail);
            }
            Authorization.Require(principal, request.ControlPartition, request.Scope.Resource, Capability.SubscriptionsManage);
            var lifetimeBinding = new EventVectorReadLifetimeBinding(this, bounded, work);
            lifetimeBinding.Scope(request);
            _ = StreamTraversalPlacement(bounded, request.ControlPartition, controlOwner);
            var admission = new EventVectorAdmissionPolicy(OperationLimitsOptions);
            _ = EventVectorCurrentSourceAuthorization.Require(bounded, principal, request,
                ImmutableArray<EventVectorAuthorizationScope>.Empty, freshSourceWitness, admission, lifetimeBinding);
            var inventory = EventVectorMapInventory.Read(bounded, request.ControlPartition,
                request.MapId, OperationLimitsOptions, cancellationToken);
            try
            {
                var original = ReadEventVectorTailOwned(bounded, principal, request, inventory,
                    freshSourceWitness, admission, lifetimeBinding, cancellationToken);
                RequireEventVectorOriginalPublicTokens(original.Phase, original.Outcome, principal);
                var identity = Store.Identity;
                var result = new EventVectorParentObservation(EventVectorTailVersion, principal.Id,
                    original.PublicCommandId, controlOwner, identity.NodeId, identity.ReadGeneration,
                    original.Phase, original.Outcome);
                work.CheckResult(result);
                return result;
            }
            finally { ClearEventVectorOfferInventory(inventory); }
        });
    }

    private (Guid PublicCommandId, ReadOnlyMemory<byte> Phase, ReadOnlyMemory<byte> Outcome) ReadEventVectorTailOwned(
        IKeyValueView view, PrincipalRecord principal, EventFeedControlRequest request,
        EventVectorOwnedInventory inventory, ReadOnlyMemory<byte> freshSourceWitness,
        EventVectorAdmissionPolicy admission, EventVectorReadLifetimeBinding lifetimeBinding, CancellationToken cancellationToken)
    {
        var map = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidEventVectorTail);
        RequireEventVectorTailCursor(map, Verify<EventVectorToken>(request.Cursor!), request, principal);
        EventVectorParentInventoryAuthorization.Require(Authorization, principal, inventory, admission, lifetimeBinding);
        EventVectorInventoryValidation.Require(map, inventory.PayloadRows, admission);
        var key = EventVectorKeys.Offer(map.ControlPartition, map.MapId);
        var row = inventory.PayloadRows.FirstOrDefault(item => item.Key.Span.SequenceEqual(key));
        if (row.Key.IsEmpty)
        {
            return (request.CommandId, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty);
        }
        var offer = NativeSerialization.Deserialize<EventVectorOffer>(row.Value.Span);
        if (offer.Version != EventVectorTailVersion || offer.MapId != map.MapId
            || offer.MapRevision != map.Revision || offer.ExpiresAt != map.ExpiresAt
            || offer.OriginalReadCommandId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidEventVectorTail);
        }
        var indexKey = EventVectorKeys.ParentOutcome(map.ControlPartition, principal.Id, offer.OriginalReadCommandId);
        EventVectorParentOutcomeIndex? index = null;
        view.ReadValue(indexKey, bytes =>
        {
            admission.RequireEncodedBytes(checked(indexKey.Length + bytes.Length));
            index = NativeSerialization.Deserialize<EventVectorParentOutcomeIndex>(bytes);
        });
        if (index is null || index.OriginalPublicCommandId != offer.OriginalReadCommandId
            || index.PrincipalId != principal.Id || index.MapId != map.MapId
            || index.ControlPartition != map.ControlPartition || index.OriginalNativeTerminalPhaseBytes.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidEventVectorTail);
        }
        lifetimeBinding.Authorize(Authorization, principal, index.OriginalAuthorizedSourceScopes, admission);
        _ = EventVectorCurrentSourceAuthorization.Require(view, principal, request,
            index.OriginalAuthorizedSourceScopes, freshSourceWitness, admission, lifetimeBinding);
        admission.RequireEncodedBytes(index.OriginalNativeTerminalPhaseBytes.Length);
        var phase = NativeSerialization.Deserialize<EventVectorControlPhase>(index.OriginalNativeTerminalPhaseBytes.Span);
        if (phase.Action != EventVectorControlAction.Offer || phase.OriginalRequest.CommandId != offer.OriginalReadCommandId
            || phase.OriginalRequest.MapId != map.MapId || phase.ExpectedMapRevision != map.Revision
            || phase.ExpectedCoverageGeneration != map.CoverageGeneration)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidEventVectorTail);
        }
        var original = EventVectorParentObservationRead.Read(view, Authorization, principal, phase.OriginalRequest,
            Store.Identity.Incarnation, freshSourceWitness, OperationLimitsOptions, cancellationToken, lifetimeBinding);
        if (original.Phase.IsEmpty || original.Outcome.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidEventVectorTail);
        }
        var outcome = NativeSerialization.Deserialize<StoredOutcome>(original.Outcome.Span);
        var actual = outcome.Result.Get<EventVectorControlResult>().Offer;
        if (actual is null || actual.OriginalReadCommandId != offer.OriginalReadCommandId
            || actual.MapRevision != offer.MapRevision || !actual.PageDigest.Span.SequenceEqual(offer.PageDigest.Span)
            || !actual.Entries.Span.SequenceEqual(offer.Entries.Span))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidEventVectorTail);
        }
        return (offer.OriginalReadCommandId, original.Phase, original.Outcome);
    }

    private void RequireEventVectorTailCursor(EventVectorMap map, EventVectorToken token,
        EventFeedControlRequest request, PrincipalRecord principal)
    {
        if (map.State != EventVectorMapState.Active || map.Released || map.PendingPhaseId is not null
            || token.Version != EventVectorTokenVersion || token.Purpose != EventVectorMapTokenPurpose
            || token.MapId != map.MapId || token.ControlPartition != map.ControlPartition
            || token.MapRevision != request.ExpectedRevision || map.Revision < token.MapRevision
            || token.CoverageGeneration != request.ExpectedCoverageGeneration
            || map.CoverageGeneration != token.CoverageGeneration || token.PrincipalId != principal.Id
            || map.PrincipalId != principal.Id || token.ControlPolicyEpoch != principal.PolicyEpoch
            || map.ControlPolicyEpoch != principal.PolicyEpoch || token.ControlIncarnation != Store.Identity.Incarnation
            || map.ControlIncarnation != Store.Identity.Incarnation || token.ExpiresAt != map.ExpiresAt
            || !token.ManifestDigest.Span.SequenceEqual(map.SourceManifestDigest.Span))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidEventVectorTail);
        }
        if (token.ExpiresAt <= EvaluationClock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.CursorExpired, InvalidEventVectorTail);
        }
    }
}
