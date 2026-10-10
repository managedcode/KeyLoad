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

    private static byte[] GroupKey(string space, SubscriptionRef subscription, params object?[] suffix)
    {
        var source = subscription.Source;
        return KeySpace.Partition(space, source.Partition, new object?[] { source.Resource, source.Kind.ToString(), source.StreamId,
            source.Generation, subscription.GroupId }.Concat(suffix).ToArray());
    }
    private static GroupState Group(IKeyValueView view, SubscriptionRef subscription)
        => view.GetRecord<GroupState>(GroupKey(SubscriptionGroupsSubscriptionKeySpace, subscription)) ?? throw Errors.Fail(ErrorCode.NotFound, SubscriptionGroupsSubscriptionGroupIsUnavailableDetail);
    private ResourceDefinition AuthorizeSubscription(IKeyValueView view, PrincipalRecord principal, SubscriptionRef subscription,
        Capability capability, DateTimeOffset now, bool requireDataPrincipal = false)
    {
        JsonData.Identifier(subscription.GroupId);
        Authorization.Require(principal, subscription.Source.Partition, subscription.Source.Resource, capability);
        Authorization.Require(principal, subscription.Source.Partition, subscription.Source.Resource, SourceReadCapability(subscription.Source));
        var resource = SourceResource(view, subscription.Source);
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
        => new(subscription, state.Definition, state.Generation, state.OwnershipEpoch, state.Checkpoint, state.IssuedPosition,
            SourceHead(view, subscription.Source).TailPosition, state.Paused, state.SafeFailureCode);
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
        { return UpdateSubscriptionDefinition(tx, request, definition, key); }
        if (tx.GetRecord<GroupState>(key) is { } existing)
        {
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

        state = state with { Paused = request.Paused };
        tx.PutRecord(GroupKey(SubscriptionGroupsSubscriptionKeySpace, request.Subscription), state);
        return GroupInfo(tx, request.Subscription, state);
    }
    private static GroupState AdvanceCheckpoint(IAtomicTransaction tx, SubscriptionRef subscription, GroupState state)
    {
        var checkpoint = state.Checkpoint;
        while (checkpoint < state.IssuedPosition)
        {
            var key = GroupKey(SubscriptionGroupsSubscriptionWindowKeySpace, subscription, state.Generation, checkpoint + SubscriptionGroupsAdjacentElementOffset);
            var delivery = tx.GetRecord<GroupDelivery>(key);
            if (delivery?.State is not (GroupDeliveryState.Acked or GroupDeliveryState.Filtered))
            {
                break;
            }

            checkpoint++;
            tx.Delete(key);
        }
        return state with { Checkpoint = checkpoint };
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

        if (state.Checkpoint < head.FirstAvailablePosition - SubscriptionGroupsAdjacentElementOffset)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, SubscriptionGroupsSubscriptionHistoryWasRetainedAwayDetail);
        }

        var result = new List<SubscriptionDelivery>();
        state = FillSubscriptionWindow(tx, principal, request, now, state, policy, resource, head, dataPrincipal, result);
        state = AdvanceCheckpoint(tx, subscription, state);
        tx.PutRecord(GroupKey(SubscriptionGroupsSubscriptionKeySpace, subscription), state);
        return new(request.RequestId, result.ToImmutableArray(), GroupInfo(tx, subscription, state), Token(tx, subscription.Source.Partition, position));
    }
    private GroupState FillSubscriptionWindow(IAtomicTransaction tx, PrincipalRecord principal, ReceiveSubscriptionRequest request,
        DateTimeOffset now, GroupState state, SubscriptionPolicy policy, ResourceDefinition resource, EventSourceHead head,
        PrincipalRecord dataPrincipal, List<SubscriptionDelivery> result)
    {
        var subscription = request.Subscription;
        long bytes = SubscriptionGroupsNoRetainedBytes;
        var windowEnd = long.Min(head.TailPosition, checked(state.Checkpoint + policy.MaxWindow));
        for (var sequence = state.Checkpoint + SubscriptionGroupsAdjacentElementOffset; sequence <= windowEnd && result.Count < request.MaxEvents; sequence++)
        {
            var key = GroupKey(SubscriptionGroupsSubscriptionWindowKeySpace, subscription, state.Generation, sequence);
            var delivery = tx.GetRecord<GroupDelivery>(key) ?? new(sequence, GroupDeliveryState.Pending, SubscriptionGroupsEmptyElementCount, SubscriptionGroupsInitialSequence);
            if (delivery.State is GroupDeliveryState.Acked or GroupDeliveryState.Filtered or GroupDeliveryState.Parked
                || delivery.State == GroupDeliveryState.Leased && delivery.LeaseUntil > now || delivery.AvailableAt > now)
            {
                continue;
            }

            var record = SourceRecord(tx, subscription.Source, sequence);
            if (state.Definition.EventTypes.Length != SubscriptionGroupsEmptyElementCount && !state.Definition.EventTypes.Contains(record.Data.EventType, StringComparer.Ordinal))
            {
                tx.PutRecord(key, delivery with { State = GroupDeliveryState.Filtered });
                tx.PutRecord(GroupKey(SubscriptionGroupsSubscriptionCompletionKeySpace, subscription, state.Generation, sequence),
                    new SubscriptionCompletion(state.Generation, sequence, SubscriptionGroupsFilteredCompletion, dataPrincipal.PolicyEpoch));
                state = state with { IssuedPosition = long.Max(state.IssuedPosition, sequence) };
                continue;
            }
            if (delivery.Attempts >= policy.MaxAttempts)
            {
                tx.PutRecord(key, delivery with { State = GroupDeliveryState.Parked, LeaseUntil = null, PrincipalId = null });
                state = state with { Paused = true, SafeFailureCode = SubscriptionGroupsAttemptsExhaustedFailureCode };
                break;
            }
            if (!TryLeaseSubscriptionEvent(tx, principal, request, now, state, resource, dataPrincipal,
                result, record, key, delivery, sequence, ref bytes, out var issuedState))
            {
                break;
            }
            state = issuedState;
        }
        return state;
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
        issuedState = state with { IssuedPosition = long.Max(state.IssuedPosition, sequence) };
        var subscription = request.Subscription;
        var token = Sign(new GroupClaims(SubscriptionDeliveryTokenPurpose, Store.Identity.Incarnation, subscription, state.Generation, state.OwnershipEpoch,
            sequence, delivery.LeaseVersion, principal.Id, principal.PolicyEpoch, dataPrincipal.PolicyEpoch));
        result.Add(new(projected, token, delivery.LeaseVersion, delivery.LeaseUntil.Value, delivery.Attempts));
        return true;
    }
    private GroupClaims ValidateGroupClaims(IKeyValueView view, PrincipalRecord principal, SubscriptionRef subscription, string token, DateTimeOffset now)
    {
        var claims = Verify<GroupClaims>(token);
        var state = Group(view, subscription);
        if (claims.Purpose != SubscriptionDeliveryTokenPurpose || claims.Incarnation != Store.Identity.Incarnation || claims.Subscription != subscription
            || claims.PrincipalId != principal.Id || claims.Generation != state.Generation || claims.OwnershipEpoch != state.OwnershipEpoch || claims.Position < SubscriptionGroupsMinimumPositiveCount)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, SubscriptionGroupsSubscriptionTokenScopeOrGenerationIsInvalidDetail);
        }

        var dataPrincipal = Principal(view, state.Definition.DataPrincipalId, now);
        if (claims.PolicyEpoch != principal.PolicyEpoch || claims.DataPolicyEpoch != dataPrincipal.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, SubscriptionGroupsSubscriptionPrincipalPolicyChangedAfterDeliveryDetail);
        }

        return claims;
    }
    private (GroupClaims Claims, GroupState State, GroupDelivery Delivery) SubscriptionLease(IKeyValueView view, PrincipalRecord principal,
        SubscriptionRef subscription, string token, DateTimeOffset now)
    {
        var claims = ValidateGroupClaims(view, principal, subscription, token, now);
        var state = Group(view, subscription);
        var delivery = view.GetRecord<GroupDelivery>(GroupKey(SubscriptionGroupsSubscriptionWindowKeySpace, subscription, state.Generation, claims.Position));
        if (delivery is null || delivery.State != GroupDeliveryState.Leased || delivery.PrincipalId != principal.Id || delivery.LeaseVersion != claims.LeaseVersion)
        {
            throw Errors.Fail(ErrorCode.StaleLease, SubscriptionGroupsSubscriptionDeliveryLeaseIsStaleDetail);
        }

        if (delivery.LeaseUntil <= now)
        {
            throw Errors.Fail(ErrorCode.LeaseExpired, SubscriptionGroupsSubscriptionLeaseExpiredDetail);
        }

        return (claims, state, delivery);
    }
    private CommitReceipt CompleteSubscriptionDelivery(IAtomicTransaction tx, PrincipalRecord principal, SubscriptionDeliveryCommand request,
        DateTimeOffset now, long position)
    {
        var (_, state, delivery) = SubscriptionLease(tx, principal, request.Subscription, request.Token, now);
        var policy = state.Definition.Policy;
        var key = GroupKey(SubscriptionGroupsSubscriptionWindowKeySpace, request.Subscription, state.Generation, delivery.Position);
        switch (request.Action)
        {
            case DeliveryAction.Renew:
                var seconds = request.LeaseSeconds ?? SubscriptionGroupsDefaultSubscriptionRenewalSeconds;
                if (seconds < SubscriptionGroupsMinimumPositiveCount || seconds > policy.MaxLeaseSeconds)
                {
                    throw Errors.Fail(ErrorCode.Validation, SubscriptionGroupsSubscriptionRenewalLeaseIsInvalidDetail);
                }

                delivery = delivery with { LeaseUntil = now.AddSeconds(seconds) };
                break;
            case DeliveryAction.Ack:
                delivery = delivery with { State = GroupDeliveryState.Acked, LeaseUntil = null, PrincipalId = null };
                tx.PutRecord(GroupKey(SubscriptionGroupsSubscriptionCompletionKeySpace, request.Subscription, state.Generation, delivery.Position),
                    new SubscriptionCompletion(state.Generation, delivery.Position, SubscriptionGroupsAckedCompletion, principal.PolicyEpoch));
                break;
            case DeliveryAction.Nack:
                var delay = Math.Min(policy.RetryMaxMilliseconds, policy.RetryBaseMilliseconds * Math.Pow(SubscriptionGroupsSubscriptionRetryExponentialBase, Math.Min(delivery.Attempts - SubscriptionGroupsAdjacentElementOffset, messagingExecution.MaximumRetryExponent)));
                delivery = delivery with { State = GroupDeliveryState.Pending, AvailableAt = now.AddMilliseconds(delay), LeaseUntil = null, PrincipalId = null };
                break;
            default:
                throw Errors.Fail(ErrorCode.Validation, SubscriptionGroupsSubscriptionDeliveryActionIsInvalidDetail);
        }
        tx.PutRecord(key, delivery);
        state = AdvanceCheckpoint(tx, request.Subscription, state);
        tx.PutRecord(GroupKey(SubscriptionGroupsSubscriptionKeySpace, request.Subscription), state);
        return new(request.CommandId, Token(tx, request.Subscription.Source.Partition, position),
            [new(request.Action.ToString(), request.Subscription.Source.Resource, delivery.Position.ToString(System.Globalization.CultureInfo.InvariantCulture), delivery.LeaseVersion)], Durability);
    }
    private SubscriptionProcessingResult CompleteSubscriptionProcessing(IAtomicTransaction tx, PrincipalRecord principal,
        SubscriptionProcessingRequest request, DateTimeOffset now, long position)
    {
        JsonData.Identifier(request.HandlerScope);
        if (request.ExecutionGeneration < SubscriptionGroupsMinimumPositiveCount)
        {
            throw Errors.Fail(ErrorCode.Validation, SubscriptionGroupsSubscriptionHandlerGenerationIsInvalidDetail);
        }

        var claims = ValidateGroupClaims(tx, principal, request.Subscription, request.Token, now);
        var inboxKey = GroupKey(SubscriptionGroupsSubscriptionInboxKeySpace, request.Subscription, claims.Position, request.HandlerScope, request.ExecutionGeneration);
        var fingerprint = JsonData.Fingerprint(new { request.Subscription, claims.Position, request.HandlerScope, request.ExecutionGeneration, request.Effects });
        if (tx.GetRecord<InboxRecord>(inboxKey) is { } completed)
        {
            if (completed.Fingerprint != fingerprint)
            {
                throw Errors.Fail(ErrorCode.Conflict, SubscriptionGroupsSubscriptionInputWasCompletedWithDifferentEffectsDetail);
            }

            ReauthorizeEffects(tx, principal, request.Subscription.Source.Partition, request.Effects);
            var delivery = tx.GetRecord<GroupDelivery>(GroupKey(SubscriptionGroupsSubscriptionWindowKeySpace, request.Subscription, claims.Generation, claims.Position));
            var acknowledgement = delivery is { State: GroupDeliveryState.Leased } && delivery.LeaseVersion == claims.LeaseVersion
                ? CompleteSubscriptionDelivery(tx, principal, new(request.CommandId, request.Subscription, request.Token, DeliveryAction.Ack), now, position)
                : completed.Receipt;
            return new(acknowledgement, true, completed.Receipt.Token);
        }
        var ack = CompleteSubscriptionDelivery(tx, principal, new(request.CommandId, request.Subscription, request.Token, DeliveryAction.Ack), now, position);
        var effects = ApplyMutations(tx, principal, request.Subscription.Source.Partition, request.Effects, now, position);
        var receipt = ack with { Mutations = effects.Concat(ack.Mutations).ToImmutableArray() };
        tx.PutRecord(inboxKey, new InboxRecord(fingerprint, receipt));
        return new(receipt, false, receipt.Token);
    }
    /// <summary>Reads an authorized subscription and its current checkpoint state.</summary>
    /// <param name="principalId">Persisted principal identifier.</param>
    /// <param name="subscription">Subscription identity.</param>
    /// <returns>The persisted subscription state.</returns>
    public SubscriptionInfo GetSubscription(string principalId, SubscriptionRef subscription) => Store.Read(view =>
    {
        var now = Clock.GetUtcNow();
        var principal = Principal(view, principalId, now);
        try
        { AuthorizeSubscription(view, principal, subscription, Capability.SubscriptionsManage, now); }
        catch (KeyLoadException exception) when (exception.Code == ErrorCode.PermissionDenied)
        { AuthorizeSubscription(view, principal, subscription, Capability.SubscriptionsConsume, now); }
        return GroupInfo(view, subscription, Group(view, subscription));
    });
}
