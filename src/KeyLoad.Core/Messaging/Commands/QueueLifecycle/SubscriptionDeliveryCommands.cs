using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private CommitReceipt CompleteSubscriptionDelivery(IAtomicTransaction tx, PrincipalRecord principal, SubscriptionDeliveryCommand request,
        DateTimeOffset now, long position, SubscriptionWindowAuthorityReadBudget? budget = null)
    {
        budget ??= new SubscriptionWindowAuthorityReadBudget(OperationLimitsOptions);
        var (_, state, delivery) = SubscriptionLease(tx, principal, request.Subscription, request.Token, now, budget);
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
                tx.PutRecord(GroupKey(SubscriptionGroupsSubscriptionCompletionKeySpace, request.Subscription, state.Generation, delivery.Position),
                    new SubscriptionCompletion(state.Generation, delivery.Position, SubscriptionGroupsAckedCompletion, principal.PolicyEpoch));
                break;
            case DeliveryAction.Nack:
                var delay = Math.Min(policy.RetryMaxMilliseconds, policy.RetryBaseMilliseconds * Math.Pow(SubscriptionGroupsSubscriptionRetryExponentialBase, Math.Min(delivery.Attempts - SubscriptionGroupsAdjacentElementOffset, messagingExecution.MaximumRetryExponent)));
                delivery = delivery with
                {
                    State = GroupDeliveryState.Pending,
                    AvailableAt = now.AddMilliseconds(delay),
                    LeaseUntil = null,
                    PrincipalId = null
                };
                break;
            default:
                throw Errors.Fail(ErrorCode.Validation, SubscriptionGroupsSubscriptionDeliveryActionIsInvalidDetail);
        }
        tx.PutRecord(key, delivery);
        state = AdvanceCheckpoint(tx, request.Subscription, state, budget);
        tx.PutRecord(GroupKey(SubscriptionGroupsSubscriptionKeySpace, request.Subscription), state);
        return new(request.CommandId, Token(tx, request.Subscription.Source.Partition, position),
            [new(request.Action.ToString(), request.Subscription.Source.Resource, delivery.Position.ToString(System.Globalization.CultureInfo.InvariantCulture), delivery.LeaseVersion)], Durability);
    }
}
