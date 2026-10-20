using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes MessagingExecutionSubscriptionWindowFill operations against the caller's database context.</summary>
public static class MessagingExecutionSubscriptionWindowFill
{
    internal static GroupState FillSubscriptionWindow(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, ReceiveSubscriptionRequest request, DateTimeOffset now, GroupState state, SubscriptionPolicy policy, ResourceDefinition resource, EventSourceHead head, PrincipalRecord dataPrincipal, List<SubscriptionDelivery> result, SubscriptionWindowAuthorityReadBudget budget)
    {
        var subscription = request.Subscription;
        long bytes = MessagingExecutionSubscriptionGroups.SubscriptionGroupsNoRetainedBytes;
        var windowEnd = long.Min(head.TailPosition, checked(state.Checkpoint + policy.MaxWindow));
        for (var sequence = state.Checkpoint + MessagingExecutionSubscriptionGroups.SubscriptionGroupsAdjacentElementOffset; sequence <= windowEnd && result.Count < request.MaxEvents; sequence++)
        {
            var key = MessagingSerialization.GroupKey(MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionWindowKeySpace, subscription, state.Generation, sequence);
            var delivery = MessagingValidationQueueLifecycleSubscriptionDeliveryAuthority.ReadSubscriptionWindowDelivery(tx, key, budget);
            MessagingValidationQueueLifecycleSubscriptionDeliveryAuthority.RequireSubscriptionWindowReference(delivery, state, sequence);
            delivery ??= new(sequence, GroupDeliveryState.Pending, MessagingExecutionSubscriptionGroups.SubscriptionGroupsEmptyElementCount, MessagingExecutionSubscriptionGroups.SubscriptionGroupsInitialSequence);
            if (delivery.State is GroupDeliveryState.Acked or GroupDeliveryState.Filtered or GroupDeliveryState.Parked || delivery.State == GroupDeliveryState.Leased && delivery.LeaseUntil > now || delivery.AvailableAt > now)
            {
                continue;
            }

            var record = SourceEventReader.Read(tx, subscription.Source, sequence, budget.Observe);
            if (state.Definition.EventTypes.Length != MessagingExecutionSubscriptionGroups.SubscriptionGroupsEmptyElementCount && !state.Definition.EventTypes.Contains(record.Data.EventType, StringComparer.Ordinal))
            {
                tx.PutRecord(key, delivery with { State = GroupDeliveryState.Filtered });
                tx.PutRecord(MessagingSerialization.GroupKey(MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionCompletionKeySpace, subscription, state.Generation, sequence), new SubscriptionCompletion(state.Generation, sequence, MessagingExecutionSubscriptionGroups.SubscriptionGroupsFilteredCompletion, dataPrincipal.PolicyEpoch));
                state = state with
                {
                    IssuedPosition = long.Max(state.IssuedPosition, sequence)
                };
                continue;
            }

            if (delivery.Attempts >= policy.MaxAttempts)
            {
                tx.PutRecord(key, delivery with { State = GroupDeliveryState.Parked, LeaseUntil = null, PrincipalId = null });
                state = state with
                {
                    Paused = true,
                    SafeFailureCode = MessagingExecutionSubscriptionGroups.SubscriptionGroupsAttemptsExhaustedFailureCode
                };
                break;
            }

            if (!engine.TryLeaseSubscriptionEvent(tx, principal, request, now, state, resource, dataPrincipal, result, record, key, delivery, sequence, ref bytes, out var issuedState))
            {
                break;
            }

            state = issuedState;
        }

        return state;
    }
}
