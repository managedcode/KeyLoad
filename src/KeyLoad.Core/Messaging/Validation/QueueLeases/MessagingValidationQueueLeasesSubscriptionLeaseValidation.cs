using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes MessagingValidationQueueLeasesSubscriptionLeaseValidation operations against the caller's database context.</summary>
public static class MessagingValidationQueueLeasesSubscriptionLeaseValidation
{
    internal static GroupClaims ValidateGroupClaims(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, SubscriptionRef subscription, string token, DateTimeOffset now)
    {
        var claims = engine.Verify<GroupClaims>(token);
        var state = MessagingExecutionSubscriptionGroups.Group(view, subscription);
        if (claims.Purpose != MessagingExecutionSubscriptionGroups.SubscriptionDeliveryTokenPurpose || claims.Incarnation != engine.Store.Identity.Incarnation || claims.Subscription != subscription || claims.PrincipalId != principal.Id || claims.Generation != state.Generation || claims.OwnershipEpoch != state.OwnershipEpoch || claims.Position < MessagingExecutionSubscriptionGroups.SubscriptionGroupsMinimumPositiveCount)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionTokenScopeOrGenerationIsInvalidDetail);
        }

        var dataPrincipal = engine.Principal(view, state.Definition.DataPrincipalId, now);
        if (claims.PolicyEpoch != principal.PolicyEpoch || claims.DataPolicyEpoch != dataPrincipal.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionPrincipalPolicyChangedAfterDeliveryDetail);
        }

        return claims;
    }

    internal static (GroupClaims Claims, GroupState State, GroupDelivery Delivery) SubscriptionLease(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, SubscriptionRef subscription, string token, DateTimeOffset now, SubscriptionWindowAuthorityReadBudget? budget = null)
    {
        budget ??= new SubscriptionWindowAuthorityReadBudget(engine.OperationLimitsOptions);
        var lease = engine.ReadSubscriptionLease(view, principal, subscription, token, now, budget);
        engine.ValidateSubscriptionGroupProgress(view, subscription, lease.State, engine.SourceHead(view, subscription.Source), budget);
        return lease;
    }

    internal static (GroupClaims Claims, GroupState State, GroupDelivery Delivery) ReadSubscriptionLease(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, SubscriptionRef subscription, string token, DateTimeOffset now, SubscriptionWindowAuthorityReadBudget budget)
    {
        var claims = engine.ValidateGroupClaims(view, principal, subscription, token, now);
        var state = MessagingExecutionSubscriptionGroups.Group(view, subscription);
        var delivery = MessagingValidationQueueLifecycleSubscriptionDeliveryAuthority.ReadSubscriptionWindowDelivery(view, MessagingSerialization.GroupKey(MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionWindowKeySpace, subscription, state.Generation, claims.Position), budget);
        MessagingValidationQueueLifecycleSubscriptionDeliveryAuthority.RequireSubscriptionWindowReference(delivery, state, claims.Position);
        if (delivery is null || delivery.State != GroupDeliveryState.Leased || delivery.PrincipalId != principal.Id || delivery.LeaseVersion != claims.LeaseVersion)
        {
            throw Errors.Fail(ErrorCode.StaleLease, MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionDeliveryLeaseIsStaleDetail);
        }

        if (delivery.LeaseUntil <= now)
        {
            throw Errors.Fail(ErrorCode.LeaseExpired, MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionLeaseExpiredDetail);
        }

        return (claims, state, delivery);
    }
}
