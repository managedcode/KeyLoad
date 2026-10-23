using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes SubscriptionDeliveryCommands operations against the caller's database context.</summary>
public static class SubscriptionDeliveryCommands
{
    internal static CommitReceipt CompleteSubscriptionDelivery(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, SubscriptionDeliveryCommand request, DateTimeOffset now, long position, SubscriptionWindowAuthorityReadBudget? budget = null)
    {
        budget ??= new SubscriptionWindowAuthorityReadBudget(engine.OperationLimitsOptions);
        var (_, state, delivery) = engine.SubscriptionLease(tx, principal, request.Subscription, request.Token, now, budget);
        var policy = state.Definition.Policy;
        var key = MessagingSerialization.GroupKey(SubscriptionGroups.SubscriptionGroupsSubscriptionWindowKeySpace, request.Subscription, state.Generation, delivery.Position);
        switch (request.Action)
        {
            case DeliveryAction.Renew:
                var seconds = request.LeaseSeconds ?? SubscriptionGroups.SubscriptionGroupsDefaultSubscriptionRenewalSeconds;
                if (seconds < SubscriptionGroups.SubscriptionGroupsMinimumPositiveCount || seconds > policy.MaxLeaseSeconds)
                {
                    throw Errors.Fail(ErrorCode.Validation, SubscriptionGroups.SubscriptionGroupsSubscriptionRenewalLeaseIsInvalidDetail);
                }

                delivery = delivery with
                {
                    LeaseUntil = now.AddSeconds(seconds)
                };
                break;
            case DeliveryAction.Ack:
                delivery = delivery with
                {
                    State = GroupDeliveryState.Acked,
                    LeaseUntil = null,
                    PrincipalId = null
                };
                tx.PutRecord(MessagingSerialization.GroupKey(SubscriptionGroups.SubscriptionGroupsSubscriptionCompletionKeySpace, request.Subscription, state.Generation, delivery.Position), new SubscriptionCompletion(state.Generation, delivery.Position, SubscriptionGroups.SubscriptionGroupsAckedCompletion, principal.PolicyEpoch));
                break;
            case DeliveryAction.Nack:
                var delay = Math.Min(policy.RetryMaxMilliseconds, policy.RetryBaseMilliseconds * Math.Pow(SubscriptionGroups.SubscriptionGroupsSubscriptionRetryExponentialBase, Math.Min(delivery.Attempts - SubscriptionGroups.SubscriptionGroupsAdjacentElementOffset, engine.MessagingExecution.MaximumRetryExponent)));
                delivery = delivery with
                {
                    State = GroupDeliveryState.Pending,
                    AvailableAt = now.AddMilliseconds(delay),
                    LeaseUntil = null,
                    PrincipalId = null
                };
                break;
            default:
                throw Errors.Fail(ErrorCode.Validation, SubscriptionGroups.SubscriptionGroupsSubscriptionDeliveryActionIsInvalidDetail);
        }

        tx.PutRecord(key, delivery);
        state = SubscriptionGroups.AdvanceCheckpoint(tx, request.Subscription, state, budget);
        tx.PutRecord(MessagingSerialization.GroupKey(SubscriptionGroups.SubscriptionGroupsSubscriptionKeySpace, request.Subscription), state);
        return new(request.CommandId, engine.Token(tx, request.Subscription.Source.Partition, position), [new(request.Action.ToString(), request.Subscription.Source.Resource, delivery.Position.ToString(System.Globalization.CultureInfo.InvariantCulture), delivery.LeaseVersion)], engine.Durability, engine.AcknowledgedPersistedCopies);
    }
}
