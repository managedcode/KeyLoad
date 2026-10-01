using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private sealed record GroupState(SubscriptionDefinition Definition, long Generation, long OwnershipEpoch,
        long Checkpoint, long IssuedPosition, bool Paused = false, string? SafeFailureCode = null);
    private enum GroupDeliveryState { Pending, Leased, Acked, Filtered, Parked }
    private sealed record GroupDelivery(long Position, GroupDeliveryState State, int Attempts, long LeaseVersion,
        DateTimeOffset? AvailableAt = null, DateTimeOffset? LeaseUntil = null, string? PrincipalId = null);
    private sealed record GroupClaims(string Purpose, Guid Incarnation, SubscriptionRef Subscription, long Generation, long OwnershipEpoch,
        long Position, long LeaseVersion, string PrincipalId, long PolicyEpoch, long DataPolicyEpoch);
    private sealed record SubscriptionCompletion(long Generation, long Position, string Outcome, long PolicyEpoch);
    private static byte[] GroupKey(string space, SubscriptionRef subscription, params object?[] suffix)
    {
        var source = subscription.Source;
        return KeySpace.Partition(space, source.Partition, new object?[] { source.Resource, source.Kind.ToString(), source.StreamId,
            source.Generation, subscription.GroupId }.Concat(suffix).ToArray());
    }
    private static GroupState Group(IKeyValueView view, SubscriptionRef subscription)
        => view.GetRecord<GroupState>(GroupKey("subscription", subscription)) ?? throw Errors.Fail(ErrorCode.NotFound, "The subscription group is unavailable.");
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
            SubscriptionStart.FromBeginning when cursor is null => head.FirstAvailablePosition - 1,
            SubscriptionStart.FromNow when cursor is null => head.TailPosition,
            SubscriptionStart.FromCursor when cursor is not null => SourceCursorPosition(view, principal, source, cursor, now),
            _ => throw Errors.Fail(ErrorCode.Validation, "The subscription start mode or cursor is invalid.")
        };
        if (position < head.FirstAvailablePosition - 1) throw Errors.Fail(ErrorCode.HistoryUnavailable, "The subscription history is unavailable.");
        if (position > head.TailPosition) throw Errors.Fail(ErrorCode.Validation, "The subscription start is beyond the source tail.");
        return position;
    }
    private SubscriptionInfo ConfigureSubscription(IAtomicTransaction tx, PrincipalRecord principal, ConfigureSubscriptionRequest request, DateTimeOffset now)
    {
        var definition = request.Definition; JsonData.Identifier(definition.DataPrincipalId);
        var policy = definition.Policy;
        if (policy.MaxWindow is < 1 or > 4_096 || policy.MaxAttempts is < 1 or > 1_000 || policy.MaxLeaseSeconds is < 1 or > 3_600
            || policy.RetryBaseMilliseconds < 1 || policy.RetryMaxMilliseconds < policy.RetryBaseMilliseconds
            || definition.EventTypes.Length > 128 || definition.EventTypes.Distinct(StringComparer.Ordinal).Count() != definition.EventTypes.Length)
            throw Errors.Fail(ErrorCode.Validation, "The subscription policy exceeds its bounds.");
        foreach (var type in definition.EventTypes) JsonData.Identifier(type);
        var resource = SourceResource(tx, request.Subscription.Source);
        var dataPrincipal = Principal(tx, definition.DataPrincipalId, now);
        Authorization.Require(dataPrincipal, request.Subscription.Source.Partition, request.Subscription.Source.Resource, SourceReadCapability(request.Subscription.Source));
        Authorization.RequireWorkerInput(dataPrincipal, resource);
        var key = GroupKey("subscription", request.Subscription);
        if (tx.GetRecord<GroupState>(key) is { } existing)
        {
            if (JsonData.Fingerprint(existing.Definition) != JsonData.Fingerprint(definition))
                throw Errors.Fail(ErrorCode.Conflict, "Changing a subscription definition requires a new group identity.");
            return GroupInfo(tx, request.Subscription, existing);
        }
        var cut = StartPosition(tx, principal, request.Subscription.Source, request.Start, request.Cursor, now);
        var state = new GroupState(definition, 1, 1, cut, cut);
        tx.PutRecord(key, state);
        return GroupInfo(tx, request.Subscription, state);
    }
    private SubscriptionInfo SeekSubscription(IAtomicTransaction tx, PrincipalRecord principal, SeekSubscriptionRequest request, DateTimeOffset now)
    {
        var state = Group(tx, request.Subscription);
        if (request.ExpectedGeneration != state.Generation) throw Errors.Fail(ErrorCode.RevisionConflict, "The subscription generation changed.");
        var cut = StartPosition(tx, principal, request.Subscription.Source, request.Start, request.Cursor, now);
        foreach (var item in tx.Scan(GroupKey("subscription-window", request.Subscription, state.Generation), state.Definition.Policy.MaxWindow + 1).Records)
            tx.Delete(item.Key);
        // Explicit seek fences old leases and pauses delivery until an authorized resume.
        state = state with { Generation = checked(state.Generation + 1), OwnershipEpoch = checked(state.OwnershipEpoch + 1),
            Checkpoint = cut, IssuedPosition = cut, Paused = true, SafeFailureCode = null };
        tx.PutRecord(GroupKey("subscription", request.Subscription), state);
        return GroupInfo(tx, request.Subscription, state);
    }
    private SubscriptionInfo SetSubscriptionPaused(IAtomicTransaction tx, SetSubscriptionPausedRequest request)
    {
        var state = Group(tx, request.Subscription);
        if (state.Generation != request.ExpectedGeneration) throw Errors.Fail(ErrorCode.RevisionConflict, "The subscription generation changed.");
        if (!request.Paused && state.SafeFailureCode is not null)
            throw Errors.Fail(ErrorCode.Conflict, "A parked subscription requires an explicit seek or new group before resuming.");
        state = state with { Paused = request.Paused }; tx.PutRecord(GroupKey("subscription", request.Subscription), state);
        return GroupInfo(tx, request.Subscription, state);
    }
    private GroupState AdvanceCheckpoint(IAtomicTransaction tx, SubscriptionRef subscription, GroupState state)
    {
        var checkpoint = state.Checkpoint;
        while (checkpoint < state.IssuedPosition)
        {
            var key = GroupKey("subscription-window", subscription, state.Generation, checkpoint + 1);
            var delivery = tx.GetRecord<GroupDelivery>(key);
            if (delivery?.State is not (GroupDeliveryState.Acked or GroupDeliveryState.Filtered)) break;
            checkpoint++; tx.Delete(key);
        }
        return state with { Checkpoint = checkpoint };
    }
    private ReceiveSubscriptionResult ReceiveSubscription(IAtomicTransaction tx, PrincipalRecord principal, ReceiveSubscriptionRequest request,
        DateTimeOffset now, long position)
    {
        var subscription = request.Subscription; var state = Group(tx, subscription); var policy = state.Definition.Policy;
        var resource = SourceResource(tx, subscription.Source); var head = SourceHead(tx, subscription.Source);
        var dataPrincipal = Principal(tx, state.Definition.DataPrincipalId, now);
        if (request.MaxEvents is < 1 or > 100 || request.MaxBytes < 1 || request.MaxBytes > Limits.MaxBatchBytes
            || request.LeaseSeconds < 1 || request.LeaseSeconds > policy.MaxLeaseSeconds)
            throw Errors.Fail(ErrorCode.Validation, "The subscription receive budget or lease is invalid.");
        if (state.Paused || resource.Paused || DispatchPaused(tx)) throw Errors.Fail(ErrorCode.DispatchPaused, "Subscription delivery is paused.");
        if (state.Checkpoint < head.FirstAvailablePosition - 1) throw Errors.Fail(ErrorCode.HistoryUnavailable, "The subscription history was retained away.");
        var result = new List<SubscriptionDelivery>(); long bytes = 0;
        var windowEnd = long.Min(head.TailPosition, checked(state.Checkpoint + policy.MaxWindow));
        for (var sequence = state.Checkpoint + 1; sequence <= windowEnd && result.Count < request.MaxEvents; sequence++)
        {
            var key = GroupKey("subscription-window", subscription, state.Generation, sequence);
            var delivery = tx.GetRecord<GroupDelivery>(key) ?? new(sequence, GroupDeliveryState.Pending, 0, 0);
            if (delivery.State is GroupDeliveryState.Acked or GroupDeliveryState.Filtered or GroupDeliveryState.Parked
                || delivery.State == GroupDeliveryState.Leased && delivery.LeaseUntil > now || delivery.AvailableAt > now) continue;
            var record = SourceRecord(tx, subscription.Source, sequence);
            if (state.Definition.EventTypes.Length != 0 && !state.Definition.EventTypes.Contains(record.Data.EventType, StringComparer.Ordinal))
            {
                tx.PutRecord(key, delivery with { State = GroupDeliveryState.Filtered });
                tx.PutRecord(GroupKey("subscription-completion", subscription, state.Generation, sequence),
                    new SubscriptionCompletion(state.Generation, sequence, "Filtered", dataPrincipal.PolicyEpoch));
                state = state with { IssuedPosition = long.Max(state.IssuedPosition, sequence) }; continue;
            }
            if (delivery.Attempts >= policy.MaxAttempts)
            {
                tx.PutRecord(key, delivery with { State = GroupDeliveryState.Parked, LeaseUntil = null, PrincipalId = null });
                state = state with { Paused = true, SafeFailureCode = "AttemptsExhausted" }; break;
            }
            var projected = ProjectEvent(principal, resource, ProjectEvent(dataPrincipal, resource, record));
            var size = JsonDefaults.Serialize(projected).LongLength;
            if (size > request.MaxBytes && result.Count == 0) throw Errors.Fail(ErrorCode.ResourceExhausted, "The next subscription event exceeds the receive byte budget.");
            if (bytes + size > request.MaxBytes) break;
            bytes += size;
            delivery = delivery with { State = GroupDeliveryState.Leased, Attempts = delivery.Attempts + 1,
                LeaseVersion = checked(delivery.LeaseVersion + 1), LeaseUntil = now.AddSeconds(request.LeaseSeconds), PrincipalId = principal.Id, AvailableAt = null };
            tx.PutRecord(key, delivery);
            state = state with { IssuedPosition = long.Max(state.IssuedPosition, sequence) };
            var token = Sign(new GroupClaims("subscription-delivery", Store.Identity.Incarnation, subscription, state.Generation, state.OwnershipEpoch,
                sequence, delivery.LeaseVersion, principal.Id, principal.PolicyEpoch, dataPrincipal.PolicyEpoch));
            result.Add(new(projected, token, delivery.LeaseVersion, delivery.LeaseUntil.Value, delivery.Attempts));
        }
        state = AdvanceCheckpoint(tx, subscription, state); tx.PutRecord(GroupKey("subscription", subscription), state);
        return new(request.RequestId, result.ToArray(), GroupInfo(tx, subscription, state), Token(subscription.Source.Partition, position));
    }
    private GroupClaims ValidateGroupClaims(IKeyValueView view, PrincipalRecord principal, SubscriptionRef subscription, string token, DateTimeOffset now)
    {
        var claims = Verify<GroupClaims>(token); var state = Group(view, subscription);
        if (claims.Purpose != "subscription-delivery" || claims.Incarnation != Store.Identity.Incarnation || claims.Subscription != subscription
            || claims.PrincipalId != principal.Id || claims.Generation != state.Generation || claims.OwnershipEpoch != state.OwnershipEpoch || claims.Position < 1)
            throw Errors.Fail(ErrorCode.TokenInvalidated, "The subscription token scope or generation is invalid.");
        var dataPrincipal = Principal(view, state.Definition.DataPrincipalId, now);
        if (claims.PolicyEpoch != principal.PolicyEpoch || claims.DataPolicyEpoch != dataPrincipal.PolicyEpoch)
            throw Errors.Fail(ErrorCode.PermissionDenied, "A subscription principal policy changed after delivery.");
        return claims;
    }
    private (GroupClaims Claims, GroupState State, GroupDelivery Delivery) SubscriptionLease(IKeyValueView view, PrincipalRecord principal,
        SubscriptionRef subscription, string token, DateTimeOffset now)
    {
        var claims = ValidateGroupClaims(view, principal, subscription, token, now); var state = Group(view, subscription);
        var delivery = view.GetRecord<GroupDelivery>(GroupKey("subscription-window", subscription, state.Generation, claims.Position));
        if (delivery is null || delivery.State != GroupDeliveryState.Leased || delivery.PrincipalId != principal.Id || delivery.LeaseVersion != claims.LeaseVersion)
            throw Errors.Fail(ErrorCode.StaleLease, "The subscription delivery lease is stale.");
        if (delivery.LeaseUntil <= now) throw Errors.Fail(ErrorCode.LeaseExpired, "The subscription lease expired.");
        return (claims, state, delivery);
    }
    private CommitReceipt CompleteSubscriptionDelivery(IAtomicTransaction tx, PrincipalRecord principal, SubscriptionDeliveryCommand request,
        DateTimeOffset now, long position)
    {
        var (_, state, delivery) = SubscriptionLease(tx, principal, request.Subscription, request.Token, now);
        var policy = state.Definition.Policy; var key = GroupKey("subscription-window", request.Subscription, state.Generation, delivery.Position);
        switch (request.Action)
        {
            case DeliveryAction.Renew:
                var seconds = request.LeaseSeconds ?? 30;
                if (seconds < 1 || seconds > policy.MaxLeaseSeconds) throw Errors.Fail(ErrorCode.Validation, "The subscription renewal lease is invalid.");
                delivery = delivery with { LeaseUntil = now.AddSeconds(seconds) }; break;
            case DeliveryAction.Ack:
                delivery = delivery with { State = GroupDeliveryState.Acked, LeaseUntil = null, PrincipalId = null };
                tx.PutRecord(GroupKey("subscription-completion", request.Subscription, state.Generation, delivery.Position),
                    new SubscriptionCompletion(state.Generation, delivery.Position, "Acked", principal.PolicyEpoch)); break;
            case DeliveryAction.Nack:
                var delay = Math.Min(policy.RetryMaxMilliseconds, policy.RetryBaseMilliseconds * Math.Pow(2, Math.Min(delivery.Attempts - 1, 20)));
                delivery = delivery with { State = GroupDeliveryState.Pending, AvailableAt = now.AddMilliseconds(delay), LeaseUntil = null, PrincipalId = null }; break;
            default: throw Errors.Fail(ErrorCode.Validation, "The subscription delivery action is invalid.");
        }
        tx.PutRecord(key, delivery); state = AdvanceCheckpoint(tx, request.Subscription, state);
        tx.PutRecord(GroupKey("subscription", request.Subscription), state);
        return new(request.CommandId, Token(request.Subscription.Source.Partition, position),
            [new(request.Action.ToString(), request.Subscription.Source.Resource, delivery.Position.ToString(System.Globalization.CultureInfo.InvariantCulture), delivery.LeaseVersion)], Durability);
    }
    private SubscriptionProcessingResult CompleteSubscriptionProcessing(IAtomicTransaction tx, PrincipalRecord principal,
        SubscriptionProcessingRequest request, DateTimeOffset now, long position)
    {
        JsonData.Identifier(request.HandlerScope);
        if (request.ExecutionGeneration < 1) throw Errors.Fail(ErrorCode.Validation, "The subscription handler generation is invalid.");
        var claims = ValidateGroupClaims(tx, principal, request.Subscription, request.Token, now);
        var inboxKey = GroupKey("subscription-inbox", request.Subscription, claims.Position, request.HandlerScope, request.ExecutionGeneration);
        var fingerprint = JsonData.Fingerprint(new { request.Subscription, claims.Position, request.HandlerScope, request.ExecutionGeneration, request.Effects });
        if (tx.GetRecord<InboxRecord>(inboxKey) is { } completed)
        {
            if (completed.Fingerprint != fingerprint) throw Errors.Fail(ErrorCode.Conflict, "The subscription input was completed with different effects.");
            ReauthorizeEffects(tx, principal, request.Subscription.Source.Partition, request.Effects);
            var delivery = tx.GetRecord<GroupDelivery>(GroupKey("subscription-window", request.Subscription, claims.Generation, claims.Position));
            var acknowledgement = delivery is { State: GroupDeliveryState.Leased } && delivery.LeaseVersion == claims.LeaseVersion
                ? CompleteSubscriptionDelivery(tx, principal, new(request.CommandId, request.Subscription, request.Token, DeliveryAction.Ack), now, position)
                : completed.Receipt;
            return new(acknowledgement, true, completed.Receipt.Token);
        }
        var ack = CompleteSubscriptionDelivery(tx, principal, new(request.CommandId, request.Subscription, request.Token, DeliveryAction.Ack), now, position);
        var effects = ApplyMutations(tx, principal, request.Subscription.Source.Partition, request.Effects, now, position);
        var receipt = ack with { Mutations = effects.Concat(ack.Mutations).ToArray() };
        tx.PutRecord(inboxKey, new InboxRecord(fingerprint, receipt));
        return new(receipt, false, receipt.Token);
    }
    public SubscriptionInfo GetSubscription(string principalId, SubscriptionRef subscription) => Store.Read(view =>
    {
        var principal = Principal(view, principalId, DateTimeOffset.UtcNow);
        try { AuthorizeSubscription(view, principal, subscription, Capability.SubscriptionsManage, DateTimeOffset.UtcNow); }
        catch (KeyLoadException exception) when (exception.Code == ErrorCode.PermissionDenied)
        { AuthorizeSubscription(view, principal, subscription, Capability.SubscriptionsConsume, DateTimeOffset.UtcNow); }
        return GroupInfo(view, subscription, Group(view, subscription));
    });
}
