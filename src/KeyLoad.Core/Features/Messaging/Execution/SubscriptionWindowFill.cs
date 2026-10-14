using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
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
            if (delivery.Position != sequence)
            {
                throw Errors.Fail(ErrorCode.Corruption, SubscriptionGroupsSubscriptionDeliveryWindowIsInconsistentDetail);
            }
            if (delivery.State is GroupDeliveryState.Acked or GroupDeliveryState.Filtered or GroupDeliveryState.Parked
                || delivery.State == GroupDeliveryState.Leased && delivery.LeaseUntil > now || delivery.AvailableAt > now)
            {
                continue;
            }

            var record = SourceRecord(tx, subscription.Source, sequence);
            if (state.Definition.EventTypes.Length != SubscriptionGroupsEmptyElementCount && !state.Definition.EventTypes.Contains(record.Data.EventType, StringComparer.Ordinal))
            {
                tx.PutRecord(key, delivery with
                {
                    State = GroupDeliveryState.Filtered
                });
                tx.PutRecord(GroupKey(SubscriptionGroupsSubscriptionCompletionKeySpace, subscription, state.Generation, sequence),
                    new SubscriptionCompletion(state.Generation, sequence, SubscriptionGroupsFilteredCompletion, dataPrincipal.PolicyEpoch));
                state = state with
                {
                    IssuedPosition = long.Max(state.IssuedPosition, sequence)
                };
                continue;
            }
            if (delivery.Attempts >= policy.MaxAttempts)
            {
                tx.PutRecord(key, delivery with
                {
                    State = GroupDeliveryState.Parked,
                    LeaseUntil = null,
                    PrincipalId = null
                });
                state = state with
                {
                    Paused = true,
                    SafeFailureCode = SubscriptionGroupsAttemptsExhaustedFailureCode
                };
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
}
