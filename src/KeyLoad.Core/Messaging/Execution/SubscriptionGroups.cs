using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes SubscriptionGroups operations against the caller's database context.</summary>
public static class SubscriptionGroups
{
    internal const string SubscriptionGroupsSubscriptionKeySpace = "subscription";
    internal const string SubscriptionGroupsSubscriptionGroupIsUnavailableDetail = "The subscription group is unavailable.";
    internal const int SubscriptionGroupsAdjacentElementOffset = 1;
    internal const string SubscriptionGroupsSubscriptionStartModeOrCursorIsInvalidDetail = "The subscription start mode or cursor is invalid.";
    internal const string SubscriptionGroupsSubscriptionHistoryIsUnavailableDetail = "The subscription history is unavailable.";
    internal const string SubscriptionGroupsSubscriptionStartIsBeyondTheSourceTailDetail = "The subscription start is beyond the source tail.";
    internal const int SubscriptionGroupsMinimumPositiveCount = 1;
    internal const int SubscriptionGroupsMaximumSubscriptionWindow = 4_096;
    internal const int SubscriptionGroupsMaximumSubscriptionAttempts = 1_000;
    internal const int SubscriptionGroupsMaximumSubscriptionLeaseSeconds = 3_600;
    internal const int SubscriptionGroupsMaximumSubscriptionEventTypes = 128;
    internal const string SubscriptionGroupsSubscriptionPolicyExceedsItsBoundsDetail = "The subscription policy exceeds its bounds.";
    internal const string SubscriptionGroupsChangingASubscriptionDefinitionRequiresANewGroupIdentityDetail = "Changing a subscription definition requires a new group identity.";
    internal const int SubscriptionGroupsSingleElementCount = 1;
    internal const string SubscriptionGroupsSubscriptionGenerationChangedDetail = "The subscription generation changed.";
    internal const string SubscriptionGroupsSubscriptionWindowKeySpace = "subscription-window";
    internal const string SubscriptionGroupsParkedSubscriptionRequiresAnExplicitSeekOrNewGroupBeforeResumingDetail = "A parked subscription requires an explicit seek or new group before resuming.";
    internal const string SubscriptionGroupsSubscriptionReceiveBudgetOrLeaseIsInvalidDetail = "The subscription receive budget or lease is invalid.";
    internal const string SubscriptionGroupsSubscriptionDeliveryIsPausedDetail = "Subscription delivery is paused.";
    internal const string SubscriptionGroupsSubscriptionHistoryWasRetainedAwayDetail = "The subscription history was retained away.";
    internal const int SubscriptionGroupsNoRetainedBytes = 0;
    internal const int SubscriptionGroupsEmptyElementCount = 0;
    internal const int SubscriptionGroupsInitialSequence = 0;
    internal const string SubscriptionGroupsSubscriptionCompletionKeySpace = "subscription-completion";
    internal const string SubscriptionGroupsFilteredCompletion = "Filtered";
    internal const string SubscriptionGroupsAttemptsExhaustedFailureCode = "AttemptsExhausted";
    internal const string SubscriptionGroupsNextSubscriptionEventExceedsTheReceiveByteBudgetDetail = "The next subscription event exceeds the receive byte budget.";
    internal const int SubscriptionGroupsVersionOne = 1;
    internal const string SubscriptionGroupsSubscriptionTokenScopeOrGenerationIsInvalidDetail = "The subscription token scope or generation is invalid.";
    internal const string SubscriptionGroupsSubscriptionPrincipalPolicyChangedAfterDeliveryDetail = "A subscription principal policy changed after delivery.";
    internal const string SubscriptionGroupsSubscriptionDeliveryLeaseIsStaleDetail = "The subscription delivery lease is stale.";
    internal const string SubscriptionGroupsSubscriptionDeliveryWindowIsInconsistentDetail = "The subscription delivery window is inconsistent.";
    internal const string SubscriptionGroupsSubscriptionLeaseExpiredDetail = "The subscription lease expired.";
    internal const int SubscriptionGroupsDefaultSubscriptionRenewalSeconds = 30;
    internal const string SubscriptionGroupsSubscriptionRenewalLeaseIsInvalidDetail = "The subscription renewal lease is invalid.";
    internal const string SubscriptionGroupsAckedCompletion = "Acked";
    internal const int SubscriptionGroupsSubscriptionRetryExponentialBase = 2;
    internal const string SubscriptionGroupsSubscriptionDeliveryActionIsInvalidDetail = "The subscription delivery action is invalid.";
    internal const string SubscriptionGroupsSubscriptionHandlerGenerationIsInvalidDetail = "The subscription handler generation is invalid.";
    internal const string SubscriptionGroupsSubscriptionInboxKeySpace = "subscription-inbox";
    internal const string SubscriptionGroupsSubscriptionInputWasCompletedWithDifferentEffectsDetail = "The subscription input was completed with different effects.";
    internal const string SubscriptionDeliveryTokenPurpose = "subscription-delivery";
    internal static GroupState Group(IKeyValueView view, SubscriptionRef subscription) => view.GetRecord<GroupState>(MessagingSerialization.GroupKey(SubscriptionGroupsSubscriptionKeySpace, subscription)) ?? throw Errors.Fail(ErrorCode.NotFound, SubscriptionGroupsSubscriptionGroupIsUnavailableDetail);
    internal static ResourceDefinition AuthorizeSubscription(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, SubscriptionRef subscription, Capability capability, DateTimeOffset now, bool requireDataPrincipal = false, bool authorizedRead = false)
    {
        JsonData.Identifier(subscription.GroupId);
        engine.Authorization.Require(principal, subscription.Source.Partition, subscription.Source.Resource, capability);
        engine.Authorization.Require(principal, subscription.Source.Partition, subscription.Source.Resource, EventSources.SourceReadCapability(subscription.Source));
        var resource = engine.SourceResource(view, subscription.Source, authorizedRead);
        if (requireDataPrincipal)
        {
            engine.Authorization.RequireWorkerInput(principal, resource);
            var dataPrincipal = engine.Principal(view, Group(view, subscription).Definition.DataPrincipalId, now);
            engine.Authorization.Require(dataPrincipal, subscription.Source.Partition, subscription.Source.Resource, EventSources.SourceReadCapability(subscription.Source));
            engine.Authorization.RequireWorkerInput(dataPrincipal, resource);
        }

        return resource;
    }

    internal static SubscriptionInfo GroupInfo(this DatabaseEngine engine, IKeyValueView view, SubscriptionRef subscription, GroupState state)
    {
        var head = engine.SourceHead(view, subscription.Source);
        engine.ValidateSubscriptionGroupProgress(view, subscription, state, head);
        return ValidatedGroupInfo(subscription, state, head);
    }

    internal static SubscriptionInfo ValidatedGroupInfo(SubscriptionRef subscription, GroupState state, EventSourceHead head) => new(subscription, state.Definition, state.Generation, state.OwnershipEpoch, state.Checkpoint, state.IssuedPosition, head.TailPosition, state.Paused, state.SafeFailureCode);
    internal static long StartPosition(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, EventSourceRef source, SubscriptionStart start, string? cursor, DateTimeOffset now)
    {
        var head = engine.SourceHead(view, source);
        var position = start switch
        {
            SubscriptionStart.FromBeginning when cursor is null => head.FirstAvailablePosition - SubscriptionGroupsAdjacentElementOffset,
            SubscriptionStart.FromNow when cursor is null => head.TailPosition,
            SubscriptionStart.FromCursor when cursor is not null => engine.SourceCursorPosition(view, principal, source, cursor, now),
            _ => throw Errors.Fail(ErrorCode.Validation, SubscriptionGroupsSubscriptionStartModeOrCursorIsInvalidDetail)
        };
        if (position < head.FirstAvailablePosition - SubscriptionGroupsAdjacentElementOffset)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, SubscriptionGroupsSubscriptionHistoryIsUnavailableDetail);
        }

        if (position > head.TailPosition)
        {
            throw Errors.Fail(ErrorCode.Validation, SubscriptionGroupsSubscriptionStartIsBeyondTheSourceTailDetail);
        }

        return position;
    }

    internal static SubscriptionInfo ConfigureSubscription(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, ConfigureSubscriptionRequest request, DateTimeOffset now)
    {
        var definition = request.Definition;
        JsonData.Identifier(definition.DataPrincipalId);
        var policy = definition.Policy;
        if (policy.MaxWindow is < SubscriptionGroupsMinimumPositiveCount or > SubscriptionGroupsMaximumSubscriptionWindow || policy.MaxAttempts is < SubscriptionGroupsMinimumPositiveCount or > SubscriptionGroupsMaximumSubscriptionAttempts || policy.MaxLeaseSeconds is < SubscriptionGroupsMinimumPositiveCount or > SubscriptionGroupsMaximumSubscriptionLeaseSeconds || policy.RetryBaseMilliseconds < SubscriptionGroupsMinimumPositiveCount || policy.RetryMaxMilliseconds < policy.RetryBaseMilliseconds || definition.EventTypes.IsDefault || definition.EventTypes.Length > SubscriptionGroupsMaximumSubscriptionEventTypes || definition.EventTypes.Distinct(StringComparer.Ordinal).Count() != definition.EventTypes.Length)
        {
            throw Errors.Fail(ErrorCode.Validation, SubscriptionGroupsSubscriptionPolicyExceedsItsBoundsDetail);
        }

        foreach (var type in definition.EventTypes)
        {
            JsonData.Identifier(type);
        }

        var resource = engine.SourceResource(tx, request.Subscription.Source);
        var dataPrincipal = engine.Principal(tx, definition.DataPrincipalId, now);
        engine.Authorization.Require(dataPrincipal, request.Subscription.Source.Partition, request.Subscription.Source.Resource, EventSources.SourceReadCapability(request.Subscription.Source));
        engine.Authorization.RequireWorkerInput(dataPrincipal, resource);
        var key = MessagingSerialization.GroupKey(SubscriptionGroupsSubscriptionKeySpace, request.Subscription);
        if (request.ExpectedGeneration is not null)
        {
            return engine.UpdateSubscriptionDefinition(tx, request, definition, key);
        }

        if (tx.GetRecord<GroupState>(key) is not null)
        {
            var existing = Group(tx, request.Subscription);
            if (JsonData.Fingerprint(existing.Definition) != JsonData.Fingerprint(definition))
            {
                throw Errors.Fail(ErrorCode.Conflict, SubscriptionGroupsChangingASubscriptionDefinitionRequiresANewGroupIdentityDetail);
            }

            return engine.GroupInfo(tx, request.Subscription, existing);
        }

        var cut = engine.StartPosition(tx, principal, request.Subscription.Source, request.Start, request.Cursor, now);
        var state = new GroupState(definition, SubscriptionGroupsSingleElementCount, SubscriptionGroupsSingleElementCount, cut, cut);
        tx.PutRecord(key, state);
        return engine.GroupInfo(tx, request.Subscription, state);
    }

    internal static SubscriptionInfo SeekSubscription(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, SeekSubscriptionRequest request, DateTimeOffset now)
    {
        var state = Group(tx, request.Subscription);
        if (request.ExpectedGeneration != state.Generation)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, SubscriptionGroupsSubscriptionGenerationChangedDetail);
        }

        var cut = engine.StartPosition(tx, principal, request.Subscription.Source, request.Start, request.Cursor, now);
        foreach (var item in tx.Scan(MessagingSerialization.GroupKey(SubscriptionGroupsSubscriptionWindowKeySpace, request.Subscription, state.Generation), state.Definition.Policy.MaxWindow + SubscriptionGroupsAdjacentElementOffset).Records)
        {
            tx.Delete(item.Key.ToArray());
        }

        // Explicit seek fences old leases and pauses delivery until an authorized resume.
        state = state with
        {
            Generation = checked(state.Generation + SubscriptionGroupsAdjacentElementOffset),
            OwnershipEpoch = checked(state.OwnershipEpoch + SubscriptionGroupsAdjacentElementOffset),
            Checkpoint = cut,
            IssuedPosition = cut,
            Paused = true,
            SafeFailureCode = null
        };
        tx.PutRecord(MessagingSerialization.GroupKey(SubscriptionGroupsSubscriptionKeySpace, request.Subscription), state);
        return engine.GroupInfo(tx, request.Subscription, state);
    }

    internal static SubscriptionInfo SetSubscriptionPaused(this DatabaseEngine engine, IAtomicTransaction tx, SetSubscriptionPausedRequest request)
    {
        var state = Group(tx, request.Subscription);
        if (state.Generation != request.ExpectedGeneration)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, SubscriptionGroupsSubscriptionGenerationChangedDetail);
        }

        if (!request.Paused && state.SafeFailureCode is not null)
        {
            throw Errors.Fail(ErrorCode.Conflict, SubscriptionGroupsParkedSubscriptionRequiresAnExplicitSeekOrNewGroupBeforeResumingDetail);
        }

        state = state with
        {
            Paused = request.Paused
        };
        tx.PutRecord(MessagingSerialization.GroupKey(SubscriptionGroupsSubscriptionKeySpace, request.Subscription), state);
        return engine.GroupInfo(tx, request.Subscription, state);
    }

    internal static GroupState AdvanceCheckpoint(IAtomicTransaction tx, SubscriptionRef subscription, GroupState state, SubscriptionWindowAuthorityReadBudget budget)
    {
        var checkpoint = state.Checkpoint;
        while (checkpoint < state.IssuedPosition)
        {
            var key = MessagingSerialization.GroupKey(SubscriptionGroupsSubscriptionWindowKeySpace, subscription, state.Generation, checkpoint + SubscriptionGroupsAdjacentElementOffset);
            var delivery = SubscriptionDeliveryAuthority.ReadSubscriptionWindowDelivery(tx, key, budget);
            SubscriptionDeliveryAuthority.RequireSubscriptionWindowReference(delivery, state, checkpoint + SubscriptionGroupsAdjacentElementOffset);
            if (delivery?.State is not (GroupDeliveryState.Acked or GroupDeliveryState.Filtered))
            {
                break;
            }

            checkpoint++;
            tx.Delete(key);
        }

        return state with
        {
            Checkpoint = checkpoint
        };
    }
}
