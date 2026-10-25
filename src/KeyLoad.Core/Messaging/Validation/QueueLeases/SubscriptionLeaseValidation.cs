using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes SubscriptionLeaseValidation operations against the caller's database context.</summary>
public static class SubscriptionLeaseValidation
{
    internal static GroupClaims ValidateGroupClaims(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, SubscriptionRef subscription, string token, DateTimeOffset now)
    {
        var claims = engine.Verify<GroupClaims>(token);
        var state = SubscriptionGroups.Group(view, subscription);
        if (claims.Purpose != SubscriptionGroups.SubscriptionDeliveryTokenPurpose || claims.Incarnation != engine.Store.Identity.Incarnation || claims.Subscription != subscription || claims.PrincipalId != principal.Id || claims.Generation != state.Generation || claims.OwnershipEpoch != state.OwnershipEpoch || claims.Position < SubscriptionGroups.SubscriptionGroupsMinimumPositiveCount)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, SubscriptionGroups.SubscriptionGroupsSubscriptionTokenScopeOrGenerationIsInvalidDetail);
        }

        var dataPrincipal = engine.Principal(view, state.Definition.DataPrincipalId, now);
        if (claims.PolicyEpoch != principal.PolicyEpoch || claims.DataPolicyEpoch != dataPrincipal.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, SubscriptionGroups.SubscriptionGroupsSubscriptionPrincipalPolicyChangedAfterDeliveryDetail);
        }

        return claims;
    }

    internal static (GroupClaims Claims, GroupState State, GroupDelivery Delivery) SubscriptionLease(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, SubscriptionRef subscription, string token, DateTimeOffset now)
    {
        var lease = engine.ReadSubscriptionLease(view, principal, subscription, token, now);
        SubscriptionDeliveryAuthority.ValidateSubscriptionGroupProgress(view, subscription, lease.State, engine.SourceHead(view, subscription.Source));
        return lease;
    }

    internal static (GroupClaims Claims, GroupState State, GroupDelivery Delivery) ReadSubscriptionLease(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, SubscriptionRef subscription, string token, DateTimeOffset now)
    {
        var claims = engine.ValidateGroupClaims(view, principal, subscription, token, now);
        var state = SubscriptionGroups.Group(view, subscription);
        var delivery = SubscriptionDeliveryAuthority.ReadSubscriptionWindowDelivery(view, MessagingSerialization.GroupKey(SubscriptionGroups.SubscriptionGroupsSubscriptionWindowKeySpace, subscription, state.Generation, claims.Position));
        SubscriptionDeliveryAuthority.RequireSubscriptionWindowReference(delivery, state, claims.Position);
        if (delivery is null || delivery.State != GroupDeliveryState.Leased || delivery.PrincipalId != principal.Id || delivery.LeaseVersion != claims.LeaseVersion)
        {
            throw Errors.Fail(ErrorCode.StaleLease, SubscriptionGroups.SubscriptionGroupsSubscriptionDeliveryLeaseIsStaleDetail);
        }

        if (delivery.LeaseUntil <= now)
        {
            throw Errors.Fail(ErrorCode.LeaseExpired, SubscriptionGroups.SubscriptionGroupsSubscriptionLeaseExpiredDetail);
        }

        return (claims, state, delivery);
    }
}
