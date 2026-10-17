using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
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
        var fingerprint = JsonData.Fingerprint(new
        {
            request.Subscription,
            claims.Position,
            request.HandlerScope,
            request.ExecutionGeneration,
            request.Effects
        });
        if (tx.GetRecord<InboxRecord>(inboxKey) is { } completed)
        {
            if (completed.Fingerprint != fingerprint)
            {
                throw Errors.Fail(ErrorCode.Conflict, SubscriptionGroupsSubscriptionInputWasCompletedWithDifferentEffectsDetail);
            }

            ReauthorizeEffects(tx, principal, request.Subscription.Source.Partition, request.Effects);
            var state = Group(tx, request.Subscription);
            var budget = new SubscriptionWindowAuthorityReadBudget(OperationLimitsOptions);
            var delivery = ReadSubscriptionWindowDelivery(tx,
                GroupKey(SubscriptionGroupsSubscriptionWindowKeySpace, request.Subscription, claims.Generation, claims.Position), budget);
            RequireSubscriptionWindowReference(delivery, state, claims.Position);
            CommitReceipt acknowledgement;
            if (delivery is { State: GroupDeliveryState.Leased } && delivery.LeaseVersion == claims.LeaseVersion)
            {
                acknowledgement = CompleteSubscriptionDelivery(tx, principal,
                    new(request.CommandId, request.Subscription, request.Token, DeliveryAction.Ack), now, position, budget);
            }
            else
            {
                ValidateSubscriptionGroupProgress(tx, request.Subscription, state,
                    SourceHead(tx, request.Subscription.Source), budget);
                acknowledgement = completed.Receipt;
            }
            return new(acknowledgement, true, completed.Receipt.Token);
        }
        var ack = CompleteSubscriptionDelivery(tx, principal, new(request.CommandId, request.Subscription, request.Token, DeliveryAction.Ack), now, position);
        var effects = ApplyMutations(tx, principal, request.Subscription.Source.Partition, request.Effects, now, position);
        var receipt = ack with
        {
            Mutations = effects.Concat(ack.Mutations).ToImmutableArray()
        };
        tx.PutRecord(inboxKey, new InboxRecord(fingerprint, receipt));
        return new(receipt, false, receipt.Token);
    }
}
