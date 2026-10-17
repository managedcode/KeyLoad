using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string SubscriptionGroupsSubscriptionKeySpace = "subscription";
    private const string SubscriptionGroupsSubscriptionGroupIsUnavailableDetail = "The subscription group is unavailable.";
    private const int SubscriptionGroupsAdjacentElementOffset = 1;
    private const string SubscriptionGroupsSubscriptionStartModeOrCursorIsInvalidDetail = "The subscription start mode or cursor is invalid.";
    private const string SubscriptionGroupsSubscriptionHistoryIsUnavailableDetail = "The subscription history is unavailable.";
    private const string SubscriptionGroupsSubscriptionStartIsBeyondTheSourceTailDetail = "The subscription start is beyond the source tail.";
    private const int SubscriptionGroupsMinimumPositiveCount = 1;
    private const int SubscriptionGroupsMaximumSubscriptionWindow = 4_096;
    private const int SubscriptionGroupsMaximumSubscriptionAttempts = 1_000;
    private const int SubscriptionGroupsMaximumSubscriptionLeaseSeconds = 3_600;
    private const int SubscriptionGroupsMaximumSubscriptionEventTypes = 128;
    private const string SubscriptionGroupsSubscriptionPolicyExceedsItsBoundsDetail = "The subscription policy exceeds its bounds.";
    private const string SubscriptionGroupsChangingASubscriptionDefinitionRequiresANewGroupIdentityDetail = "Changing a subscription definition requires a new group identity.";
    private const int SubscriptionGroupsSingleElementCount = 1;
    private const string SubscriptionGroupsSubscriptionGenerationChangedDetail = "The subscription generation changed.";
    private const string SubscriptionGroupsSubscriptionWindowKeySpace = "subscription-window";
    private const string SubscriptionGroupsParkedSubscriptionRequiresAnExplicitSeekOrNewGroupBeforeResumingDetail = "A parked subscription requires an explicit seek or new group before resuming.";
    private const string SubscriptionGroupsSubscriptionReceiveBudgetOrLeaseIsInvalidDetail = "The subscription receive budget or lease is invalid.";
    private const string SubscriptionGroupsSubscriptionDeliveryIsPausedDetail = "Subscription delivery is paused.";
    private const string SubscriptionGroupsSubscriptionHistoryWasRetainedAwayDetail = "The subscription history was retained away.";
    private const int SubscriptionGroupsNoRetainedBytes = 0;
    private const int SubscriptionGroupsEmptyElementCount = 0;
    private const int SubscriptionGroupsInitialSequence = 0;
    private const string SubscriptionGroupsSubscriptionCompletionKeySpace = "subscription-completion";
    private const string SubscriptionGroupsFilteredCompletion = "Filtered";
    private const string SubscriptionGroupsAttemptsExhaustedFailureCode = "AttemptsExhausted";
    private const string SubscriptionGroupsNextSubscriptionEventExceedsTheReceiveByteBudgetDetail = "The next subscription event exceeds the receive byte budget.";
    private const int SubscriptionGroupsVersionOne = 1;
    private const string SubscriptionGroupsSubscriptionTokenScopeOrGenerationIsInvalidDetail = "The subscription token scope or generation is invalid.";
    private const string SubscriptionGroupsSubscriptionPrincipalPolicyChangedAfterDeliveryDetail = "A subscription principal policy changed after delivery.";
    private const string SubscriptionGroupsSubscriptionDeliveryLeaseIsStaleDetail = "The subscription delivery lease is stale.";
    private const string SubscriptionGroupsSubscriptionDeliveryWindowIsInconsistentDetail = "The subscription delivery window is inconsistent.";
    private const string SubscriptionGroupsSubscriptionLeaseExpiredDetail = "The subscription lease expired.";
    private const int SubscriptionGroupsDefaultSubscriptionRenewalSeconds = 30;
    private const string SubscriptionGroupsSubscriptionRenewalLeaseIsInvalidDetail = "The subscription renewal lease is invalid.";
    private const string SubscriptionGroupsAckedCompletion = "Acked";
    private const int SubscriptionGroupsSubscriptionRetryExponentialBase = 2;
    private const string SubscriptionGroupsSubscriptionDeliveryActionIsInvalidDetail = "The subscription delivery action is invalid.";
    private const string SubscriptionGroupsSubscriptionHandlerGenerationIsInvalidDetail = "The subscription handler generation is invalid.";
    private const string SubscriptionGroupsSubscriptionInboxKeySpace = "subscription-inbox";
    private const string SubscriptionGroupsSubscriptionInputWasCompletedWithDifferentEffectsDetail = "The subscription input was completed with different effects.";

    private const string SubscriptionDeliveryTokenPurpose = "subscription-delivery";

    private static GroupState Group(IKeyValueView view, SubscriptionRef subscription)
        => view.GetRecord<GroupState>(GroupKey(SubscriptionGroupsSubscriptionKeySpace, subscription)) ?? throw Errors.Fail(ErrorCode.NotFound, SubscriptionGroupsSubscriptionGroupIsUnavailableDetail);
    private ResourceDefinition AuthorizeSubscription(IKeyValueView view, PrincipalRecord principal, SubscriptionRef subscription,
        Capability capability, DateTimeOffset now, bool requireDataPrincipal = false, bool authorizedRead = false)
    {
        JsonData.Identifier(subscription.GroupId);
        Authorization.Require(principal, subscription.Source.Partition, subscription.Source.Resource, capability);
        Authorization.Require(principal, subscription.Source.Partition, subscription.Source.Resource, SourceReadCapability(subscription.Source));
        var resource = SourceResource(view, subscription.Source, authorizedRead);
        if (requireDataPrincipal)
        {
            Authorization.RequireWorkerInput(principal, resource);
            var dataPrincipal = Principal(view, Group(view, subscription).Definition.DataPrincipalId, now);
            Authorization.Require(dataPrincipal, subscription.Source.Partition, subscription.Source.Resource, SourceReadCapability(subscription.Source));
            Authorization.RequireWorkerInput(dataPrincipal, resource);
        }
        return resource;
    }
    private SubscriptionInfo GroupInfo(IKeyValueView view, SubscriptionRef subscription, GroupState state)
    {
        var head = SourceHead(view, subscription.Source);
        ValidateSubscriptionGroupProgress(view, subscription, state, head);
        return ValidatedGroupInfo(subscription, state, head);
    }
    private static SubscriptionInfo ValidatedGroupInfo(SubscriptionRef subscription, GroupState state, EventSourceHead head)
        => new(subscription, state.Definition, state.Generation, state.OwnershipEpoch, state.Checkpoint,
            state.IssuedPosition, head.TailPosition, state.Paused, state.SafeFailureCode);
    private long StartPosition(IKeyValueView view, PrincipalRecord principal, EventSourceRef source, SubscriptionStart start,
        string? cursor, DateTimeOffset now)
    {
        var head = SourceHead(view, source);
        var position = start switch
        {
            SubscriptionStart.FromBeginning when cursor is null => head.FirstAvailablePosition - SubscriptionGroupsAdjacentElementOffset,
            SubscriptionStart.FromNow when cursor is null => head.TailPosition,
            SubscriptionStart.FromCursor when cursor is not null => SourceCursorPosition(view, principal, source, cursor, now),
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
    private SubscriptionInfo ConfigureSubscription(IAtomicTransaction tx, PrincipalRecord principal, ConfigureSubscriptionRequest request, DateTimeOffset now)
    {
        var definition = request.Definition;
        JsonData.Identifier(definition.DataPrincipalId);
        var policy = definition.Policy;
        if (policy.MaxWindow is < SubscriptionGroupsMinimumPositiveCount or > SubscriptionGroupsMaximumSubscriptionWindow || policy.MaxAttempts is < SubscriptionGroupsMinimumPositiveCount or > SubscriptionGroupsMaximumSubscriptionAttempts || policy.MaxLeaseSeconds is < SubscriptionGroupsMinimumPositiveCount or > SubscriptionGroupsMaximumSubscriptionLeaseSeconds
            || policy.RetryBaseMilliseconds < SubscriptionGroupsMinimumPositiveCount || policy.RetryMaxMilliseconds < policy.RetryBaseMilliseconds
            || definition.EventTypes.IsDefault || definition.EventTypes.Length > SubscriptionGroupsMaximumSubscriptionEventTypes
            || definition.EventTypes.Distinct(StringComparer.Ordinal).Count() != definition.EventTypes.Length)
        {
            throw Errors.Fail(ErrorCode.Validation, SubscriptionGroupsSubscriptionPolicyExceedsItsBoundsDetail);
        }

        foreach (var type in definition.EventTypes)
        {
            JsonData.Identifier(type);
        }

        var resource = SourceResource(tx, request.Subscription.Source);
        var dataPrincipal = Principal(tx, definition.DataPrincipalId, now);
        Authorization.Require(dataPrincipal, request.Subscription.Source.Partition, request.Subscription.Source.Resource, SourceReadCapability(request.Subscription.Source));
        Authorization.RequireWorkerInput(dataPrincipal, resource);
        var key = GroupKey(SubscriptionGroupsSubscriptionKeySpace, request.Subscription);
        if (request.ExpectedGeneration is not null)
        {
            return UpdateSubscriptionDefinition(tx, request, definition, key);
        }
        if (tx.GetRecord<GroupState>(key) is not null)
        {
            var existing = Group(tx, request.Subscription);
            if (JsonData.Fingerprint(existing.Definition) != JsonData.Fingerprint(definition))
            {
                throw Errors.Fail(ErrorCode.Conflict, SubscriptionGroupsChangingASubscriptionDefinitionRequiresANewGroupIdentityDetail);
            }

            return GroupInfo(tx, request.Subscription, existing);
        }
        var cut = StartPosition(tx, principal, request.Subscription.Source, request.Start, request.Cursor, now);
        var state = new GroupState(definition, SubscriptionGroupsSingleElementCount, SubscriptionGroupsSingleElementCount, cut, cut);
        tx.PutRecord(key, state);
        return GroupInfo(tx, request.Subscription, state);
    }
    private SubscriptionInfo SeekSubscription(IAtomicTransaction tx, PrincipalRecord principal, SeekSubscriptionRequest request, DateTimeOffset now)
    {
        var state = Group(tx, request.Subscription);
        if (request.ExpectedGeneration != state.Generation)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, SubscriptionGroupsSubscriptionGenerationChangedDetail);
        }

        var cut = StartPosition(tx, principal, request.Subscription.Source, request.Start, request.Cursor, now);
        foreach (var item in tx.Scan(GroupKey(SubscriptionGroupsSubscriptionWindowKeySpace, request.Subscription, state.Generation), state.Definition.Policy.MaxWindow + SubscriptionGroupsAdjacentElementOffset).Records)
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
        tx.PutRecord(GroupKey(SubscriptionGroupsSubscriptionKeySpace, request.Subscription), state);
        return GroupInfo(tx, request.Subscription, state);
    }
    private SubscriptionInfo SetSubscriptionPaused(IAtomicTransaction tx, SetSubscriptionPausedRequest request)
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
        tx.PutRecord(GroupKey(SubscriptionGroupsSubscriptionKeySpace, request.Subscription), state);
        return GroupInfo(tx, request.Subscription, state);
    }
    private static GroupState AdvanceCheckpoint(IAtomicTransaction tx, SubscriptionRef subscription, GroupState state,
        SubscriptionWindowAuthorityReadBudget budget)
    {
        var checkpoint = state.Checkpoint;
        while (checkpoint < state.IssuedPosition)
        {
            var key = GroupKey(SubscriptionGroupsSubscriptionWindowKeySpace, subscription, state.Generation, checkpoint + SubscriptionGroupsAdjacentElementOffset);
            var delivery = ReadSubscriptionWindowDelivery(tx, key, budget);
            RequireSubscriptionWindowReference(delivery, state, checkpoint + SubscriptionGroupsAdjacentElementOffset);
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
    private ReceiveSubscriptionResult ReceiveSubscription(IAtomicTransaction tx, PrincipalRecord principal, ReceiveSubscriptionRequest request,
        DateTimeOffset now, long position)
    {
        var subscription = request.Subscription;
        var state = Group(tx, subscription);
        var policy = state.Definition.Policy;
        var resource = SourceResource(tx, subscription.Source);
        var head = SourceHead(tx, subscription.Source);
        var dataPrincipal = Principal(tx, state.Definition.DataPrincipalId, now);
        if (request.MaxEvents < SubscriptionGroupsMinimumPositiveCount || request.MaxEvents > messagingExecution.MaximumReceiveEvents || request.MaxBytes < SubscriptionGroupsMinimumPositiveCount || request.MaxBytes > Limits.MaxBatchBytes
            || request.LeaseSeconds < SubscriptionGroupsMinimumPositiveCount || request.LeaseSeconds > policy.MaxLeaseSeconds)
        {
            throw Errors.Fail(ErrorCode.Validation, SubscriptionGroupsSubscriptionReceiveBudgetOrLeaseIsInvalidDetail);
        }

        if (state.Paused || resource.Paused || DispatchPaused(tx))
        {
            throw Errors.Fail(ErrorCode.DispatchPaused, SubscriptionGroupsSubscriptionDeliveryIsPausedDetail);
        }

        ValidateSubscriptionGroupLowerBounds(state);
        if (state.Checkpoint < head.FirstAvailablePosition - SubscriptionGroupsAdjacentElementOffset)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, SubscriptionGroupsSubscriptionHistoryWasRetainedAwayDetail);
        }

        var windowBudget = new SubscriptionWindowAuthorityReadBudget(OperationLimitsOptions);
        ValidateSubscriptionGroupProgress(tx, subscription, state, head, windowBudget);
        var result = new List<SubscriptionDelivery>();
        state = FillSubscriptionWindow(tx, principal, request, now, state, policy, resource, head, dataPrincipal, result, windowBudget);
        state = AdvanceCheckpoint(tx, subscription, state, windowBudget);
        tx.PutRecord(GroupKey(SubscriptionGroupsSubscriptionKeySpace, subscription), state);
        return new(request.RequestId, result.ToImmutableArray(), ValidatedGroupInfo(subscription, state, head), Token(tx, subscription.Source.Partition, position));
    }
    private bool TryLeaseSubscriptionEvent(IAtomicTransaction tx, PrincipalRecord principal, ReceiveSubscriptionRequest request,
        DateTimeOffset now, GroupState state, ResourceDefinition resource, PrincipalRecord dataPrincipal,
        List<SubscriptionDelivery> result, SourceEventRecord record, byte[] key, GroupDelivery delivery, long sequence,
        ref long bytes, out GroupState issuedState)
    {
        issuedState = state;
        var projected = ProjectEvent(principal, resource, ProjectEvent(dataPrincipal, resource, record));
        var size = JsonDefaults.Serialize(projected).LongLength;
        if (size > request.MaxBytes && result.Count == SubscriptionGroupsNoRetainedBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, SubscriptionGroupsNextSubscriptionEventExceedsTheReceiveByteBudgetDetail);
        }
        if (bytes + size > request.MaxBytes)
        {
            return false;
        }
        if (delivery.LeaseVersion == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, SubscriptionGroupsDeliveryLeaseVersionExhaustedDetail);
        }
        bytes += size;
        delivery = delivery with
        {
            State = GroupDeliveryState.Leased,
            Attempts = delivery.Attempts + SubscriptionGroupsAdjacentElementOffset,
            LeaseVersion = checked(delivery.LeaseVersion + SubscriptionGroupsVersionOne),
            LeaseUntil = now.AddSeconds(request.LeaseSeconds),
            PrincipalId = principal.Id,
            AvailableAt = null
        };
        tx.PutRecord(key, delivery);
        issuedState = state with
        {
            IssuedPosition = long.Max(state.IssuedPosition, sequence)
        };
        var subscription = request.Subscription;
        var token = Sign(new GroupClaims(SubscriptionDeliveryTokenPurpose, Store.Identity.Incarnation, subscription, state.Generation, state.OwnershipEpoch,
            sequence, delivery.LeaseVersion, principal.Id, principal.PolicyEpoch, dataPrincipal.PolicyEpoch));
        result.Add(new(projected, token, delivery.LeaseVersion, delivery.LeaseUntil.Value, delivery.Attempts));
        return true;
    }
    /// <summary>Reads an authorized subscription and its current checkpoint state.</summary>
    /// <param name="principalId">Persisted principal identifier.</param>
    /// <param name="subscription">Subscription identity.</param>
    /// <returns>The persisted subscription state.</returns>
    public SubscriptionInfo GetSubscription(string principalId, SubscriptionRef subscription)
        => Store.Read(view => GetSubscriptionRead(view, principalId, subscription));

    private SubscriptionInfo GetSubscriptionRead(IKeyValueView view, string principalId, SubscriptionRef subscription)
    {
        var now = Clock.GetUtcNow();
        var principal = Principal(view, principalId, now);
        try
        {
            AuthorizeSubscription(view, principal, subscription, Capability.SubscriptionsManage, now, authorizedRead: true);
        }
        catch (KeyLoadException exception) when (exception.Code == ErrorCode.PermissionDenied)
        {
            AuthorizeSubscription(view, principal, subscription, Capability.SubscriptionsConsume, now, authorizedRead: true);
        }
        return GroupInfo(view, subscription, Group(view, subscription));
    }
}
