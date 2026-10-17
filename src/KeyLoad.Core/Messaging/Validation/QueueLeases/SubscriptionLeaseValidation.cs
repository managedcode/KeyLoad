using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private GroupClaims ValidateGroupClaims(IKeyValueView view, PrincipalRecord principal, SubscriptionRef subscription, string token, DateTimeOffset now)
    {
        var claims = Verify<GroupClaims>(token);
        var state = Group(view, subscription);
        if (claims.Purpose != SubscriptionDeliveryTokenPurpose || claims.Incarnation != Store.Identity.Incarnation || claims.Subscription != subscription
            || claims.PrincipalId != principal.Id || claims.Generation != state.Generation || claims.OwnershipEpoch != state.OwnershipEpoch || claims.Position < SubscriptionGroupsMinimumPositiveCount)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, SubscriptionGroupsSubscriptionTokenScopeOrGenerationIsInvalidDetail);
        }

        var dataPrincipal = Principal(view, state.Definition.DataPrincipalId, now);
        if (claims.PolicyEpoch != principal.PolicyEpoch || claims.DataPolicyEpoch != dataPrincipal.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, SubscriptionGroupsSubscriptionPrincipalPolicyChangedAfterDeliveryDetail);
        }

        return claims;
    }
    private (GroupClaims Claims, GroupState State, GroupDelivery Delivery) SubscriptionLease(IKeyValueView view, PrincipalRecord principal,
        SubscriptionRef subscription, string token, DateTimeOffset now, SubscriptionWindowAuthorityReadBudget? budget = null)
    {
        budget ??= new SubscriptionWindowAuthorityReadBudget(OperationLimitsOptions);
        var lease = ReadSubscriptionLease(view, principal, subscription, token, now, budget);
        ValidateSubscriptionGroupProgress(view, subscription, lease.State, SourceHead(view, subscription.Source), budget);
        return lease;
    }
    private (GroupClaims Claims, GroupState State, GroupDelivery Delivery) ReadSubscriptionLease(IKeyValueView view,
        PrincipalRecord principal, SubscriptionRef subscription, string token, DateTimeOffset now,
        SubscriptionWindowAuthorityReadBudget budget)
    {
        var claims = ValidateGroupClaims(view, principal, subscription, token, now);
        var state = Group(view, subscription);
        var delivery = ReadSubscriptionWindowDelivery(view,
            GroupKey(SubscriptionGroupsSubscriptionWindowKeySpace, subscription, state.Generation, claims.Position), budget);
        RequireSubscriptionWindowReference(delivery, state, claims.Position);
        if (delivery is null || delivery.State != GroupDeliveryState.Leased || delivery.PrincipalId != principal.Id || delivery.LeaseVersion != claims.LeaseVersion)
        {
            throw Errors.Fail(ErrorCode.StaleLease, SubscriptionGroupsSubscriptionDeliveryLeaseIsStaleDetail);
        }

        if (delivery.LeaseUntil <= now)
        {
            throw Errors.Fail(ErrorCode.LeaseExpired, SubscriptionGroupsSubscriptionLeaseExpiredDetail);
        }

        return (claims, state, delivery);
    }
}
