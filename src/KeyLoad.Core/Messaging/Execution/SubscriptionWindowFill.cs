using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private GroupState FillSubscriptionWindow(IAtomicTransaction tx, PrincipalRecord principal, ReceiveSubscriptionRequest request,
        DateTimeOffset now, GroupState state, SubscriptionPolicy policy, ResourceDefinition resource, EventSourceHead head,
        PrincipalRecord dataPrincipal, List<SubscriptionDelivery> result, SubscriptionWindowAuthorityReadBudget budget)
    {
        var subscription = request.Subscription;
        long bytes = SubscriptionGroupsNoRetainedBytes;
        var windowEnd = long.Min(head.TailPosition, checked(state.Checkpoint + policy.MaxWindow));
        for (var sequence = state.Checkpoint + SubscriptionGroupsAdjacentElementOffset; sequence <= windowEnd && result.Count < request.MaxEvents; sequence++)
        {
            var key = GroupKey(SubscriptionGroupsSubscriptionWindowKeySpace, subscription, state.Generation, sequence);
            var delivery = ReadSubscriptionWindowDelivery(tx, key, budget);
            RequireSubscriptionWindowReference(delivery, state, sequence);
            delivery ??= new(sequence, GroupDeliveryState.Pending, SubscriptionGroupsEmptyElementCount, SubscriptionGroupsInitialSequence);

            if (delivery.State is GroupDeliveryState.Acked or GroupDeliveryState.Filtered or GroupDeliveryState.Parked
                || delivery.State == GroupDeliveryState.Leased && delivery.LeaseUntil > now || delivery.AvailableAt > now)
            {
                continue;
            }

            var record = SourceEventReader.Read(tx, subscription.Source, sequence, budget.Observe);
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
