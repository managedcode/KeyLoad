using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes SubscriptionProcessingCommands operations against the caller's database context.</summary>
public static class SubscriptionProcessingCommands
{
    internal static SubscriptionProcessingResult CompleteSubscriptionProcessing(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, SubscriptionProcessingRequest request, DateTimeOffset now, long position)
    {
        JsonData.Identifier(request.HandlerScope);
        if (request.ExecutionGeneration < SubscriptionGroups.SubscriptionGroupsMinimumPositiveCount)
        {
            throw Errors.Fail(ErrorCode.Validation, SubscriptionGroups.SubscriptionGroupsSubscriptionHandlerGenerationIsInvalidDetail);
        }

        var claims = engine.ValidateGroupClaims(tx, principal, request.Subscription, request.Token, now);
        var inboxKey = MessagingSerialization.GroupKey(SubscriptionGroups.SubscriptionGroupsSubscriptionInboxKeySpace, request.Subscription, claims.Position, request.HandlerScope, request.ExecutionGeneration);
        var fingerprint = JsonData.Fingerprint(new { request.Subscription, claims.Position, request.HandlerScope, request.ExecutionGeneration, request.Effects });
        if (tx.GetRecord<InboxRecord>(inboxKey) is { } completed)
        {
            if (completed.Fingerprint != fingerprint)
            {
                throw Errors.Fail(ErrorCode.Conflict, SubscriptionGroups.SubscriptionGroupsSubscriptionInputWasCompletedWithDifferentEffectsDetail);
            }

            engine.ReauthorizeEffects(tx, principal, request.Subscription.Source.Partition, request.Effects);
            var state = SubscriptionGroups.Group(tx, request.Subscription);
            var delivery = SubscriptionDeliveryAuthority.ReadSubscriptionWindowDelivery(tx, MessagingSerialization.GroupKey(SubscriptionGroups.SubscriptionGroupsSubscriptionWindowKeySpace, request.Subscription, claims.Generation, claims.Position));
            SubscriptionDeliveryAuthority.RequireSubscriptionWindowReference(delivery, state, claims.Position);
            CommitReceipt acknowledgement;
            if (delivery is { State: GroupDeliveryState.Leased } && delivery.LeaseVersion == claims.LeaseVersion)
            {
                acknowledgement = engine.CompleteSubscriptionDelivery(tx, principal, new(request.CommandId, request.Subscription, request.Token, DeliveryAction.Ack), now, position);
            }
            else
            {
                SubscriptionDeliveryAuthority.ValidateSubscriptionGroupProgress(tx, request.Subscription, state, engine.SourceHead(tx, request.Subscription.Source));
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
