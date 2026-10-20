using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes MessagingCommandsSubscriptionsSubscriptionProcessingCommands operations against the caller's database context.</summary>
public static class MessagingCommandsSubscriptionsSubscriptionProcessingCommands
{
    internal static SubscriptionProcessingResult CompleteSubscriptionProcessing(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, SubscriptionProcessingRequest request, DateTimeOffset now, long position)
    {
        JsonData.Identifier(request.HandlerScope);
        if (request.ExecutionGeneration < MessagingExecutionSubscriptionGroups.SubscriptionGroupsMinimumPositiveCount)
        {
            throw Errors.Fail(ErrorCode.Validation, MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionHandlerGenerationIsInvalidDetail);
        }

        var claims = engine.ValidateGroupClaims(tx, principal, request.Subscription, request.Token, now);
        var inboxKey = MessagingSerialization.GroupKey(MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionInboxKeySpace, request.Subscription, claims.Position, request.HandlerScope, request.ExecutionGeneration);
        var fingerprint = JsonData.Fingerprint(new { request.Subscription, claims.Position, request.HandlerScope, request.ExecutionGeneration, request.Effects });
        if (tx.GetRecord<InboxRecord>(inboxKey) is { } completed)
        {
            if (completed.Fingerprint != fingerprint)
            {
                throw Errors.Fail(ErrorCode.Conflict, MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionInputWasCompletedWithDifferentEffectsDetail);
            }

            engine.ReauthorizeEffects(tx, principal, request.Subscription.Source.Partition, request.Effects);
            var state = MessagingExecutionSubscriptionGroups.Group(tx, request.Subscription);
            var budget = new SubscriptionWindowAuthorityReadBudget(engine.OperationLimitsOptions);
            var delivery = MessagingValidationQueueLifecycleSubscriptionDeliveryAuthority.ReadSubscriptionWindowDelivery(tx, MessagingSerialization.GroupKey(MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionWindowKeySpace, request.Subscription, claims.Generation, claims.Position), budget);
            MessagingValidationQueueLifecycleSubscriptionDeliveryAuthority.RequireSubscriptionWindowReference(delivery, state, claims.Position);
            CommitReceipt acknowledgement;
            if (delivery is { State: GroupDeliveryState.Leased } && delivery.LeaseVersion == claims.LeaseVersion)
            {
                acknowledgement = engine.CompleteSubscriptionDelivery(tx, principal, new(request.CommandId, request.Subscription, request.Token, DeliveryAction.Ack), now, position, budget);
            }
            else
            {
                engine.ValidateSubscriptionGroupProgress(tx, request.Subscription, state, engine.SourceHead(tx, request.Subscription.Source), budget);
                acknowledgement = completed.Receipt;
            }

            return new(acknowledgement, true, completed.Receipt.Token);
        }

        var ack = engine.CompleteSubscriptionDelivery(tx, principal, new(request.CommandId, request.Subscription, request.Token, DeliveryAction.Ack), now, position);
        var effects = engine.ApplyMutations(tx, principal, request.Subscription.Source.Partition, request.Effects, now, position);
        var receipt = ack with
        {
            Mutations = effects.Concat(ack.Mutations).ToImmutableArray()
        };
        tx.PutRecord(inboxKey, new InboxRecord(fingerprint, receipt));
        return new(receipt, false, receipt.Token);
    }
}
