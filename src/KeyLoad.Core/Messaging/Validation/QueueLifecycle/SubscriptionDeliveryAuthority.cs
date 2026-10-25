using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes SubscriptionDeliveryAuthority operations against the caller's database context.</summary>
public static class SubscriptionDeliveryAuthority
{
    internal const string SubscriptionGroupsDeliveryLeaseVersionExhaustedDetail = "The subscription delivery lease version is exhausted.";
    internal static void ValidateSubscriptionGroupLowerBounds(GroupState state)
    {
        if (state.Checkpoint < SubscriptionGroups.SubscriptionGroupsInitialSequence || state.IssuedPosition < state.Checkpoint)
        {
            throw Errors.Fail(ErrorCode.Corruption, SubscriptionGroups.SubscriptionGroupsSubscriptionDeliveryWindowIsInconsistentDetail);
        }
    }

    internal static void ValidateSubscriptionGroupProgress(IKeyValueView view, SubscriptionRef subscription, GroupState state, EventSourceHead head)
    {
        ValidateSubscriptionGroupLowerBounds(state);
        if (state.Checkpoint > head.TailPosition || state.IssuedPosition < state.Checkpoint || state.IssuedPosition > head.TailPosition || state.IssuedPosition - state.Checkpoint > state.Definition.Policy.MaxWindow)
        {
            throw Errors.Fail(ErrorCode.Corruption, SubscriptionGroups.SubscriptionGroupsSubscriptionDeliveryWindowIsInconsistentDetail);
        }

        ValidateSubscriptionGroupWindow(view, subscription, state);
    }

    internal static void ValidateSubscriptionGroupWindow(IKeyValueView view, SubscriptionRef subscription, GroupState state)
    {
        var maximum = state.Definition.Policy.MaxWindow;
        if (maximum is < SubscriptionGroups.SubscriptionGroupsMinimumPositiveCount or > SubscriptionGroups.SubscriptionGroupsMaximumSubscriptionWindow)
        {
            throw Errors.Fail(ErrorCode.Corruption, SubscriptionGroups.SubscriptionGroupsSubscriptionDeliveryWindowIsInconsistentDetail);
        }

        var rows = SubscriptionGroups.SubscriptionGroupsEmptyElementCount;
        var scan = view.VisitRange(MessagingSerialization.GroupKey(SubscriptionGroups.SubscriptionGroupsSubscriptionWindowKeySpace, subscription, state.Generation), checked(maximum + SubscriptionGroups.SubscriptionGroupsAdjacentElementOffset), (key, value) =>
        {
            var delivery = OrleansSerialization.Deserialize<GroupDelivery>(value) ?? throw Errors.Fail(ErrorCode.Corruption, SubscriptionGroups.SubscriptionGroupsSubscriptionDeliveryWindowIsInconsistentDetail);
            ValidateSubscriptionDeliveryAuthority(delivery, delivery.Position);
            if (delivery.Position <= state.Checkpoint || delivery.Position > state.IssuedPosition || !key.SequenceEqual(MessagingSerialization.GroupKey(SubscriptionGroups.SubscriptionGroupsSubscriptionWindowKeySpace, subscription, state.Generation, delivery.Position)))
            {
                throw Errors.Fail(ErrorCode.Corruption, SubscriptionGroups.SubscriptionGroupsSubscriptionDeliveryWindowIsInconsistentDetail);
            }

            rows++;
            return true;
        });
        if (scan.HasMore || scan.StoppedByVisitor || rows != state.IssuedPosition - state.Checkpoint)
        {
            throw Errors.Fail(ErrorCode.Corruption, SubscriptionGroups.SubscriptionGroupsSubscriptionDeliveryWindowIsInconsistentDetail);
        }
    }

    internal static GroupDelivery? ReadSubscriptionWindowDelivery(IKeyValueView view, byte[] key)
    {
        GroupDelivery? delivery = null;
        view.ReadValue(key, value => delivery = OrleansSerialization.Deserialize<GroupDelivery>(value));
        return delivery;
    }

    internal static void RequireSubscriptionWindowReference(GroupDelivery? delivery, GroupState state, long expectedPosition)
    {
        if (delivery is null && expectedPosition > state.Checkpoint && expectedPosition <= state.IssuedPosition)
        {
            throw Errors.Fail(ErrorCode.Corruption, SubscriptionGroups.SubscriptionGroupsSubscriptionDeliveryWindowIsInconsistentDetail);
        }

        if (delivery is not null)
        {
            ValidateSubscriptionDeliveryAuthority(delivery, expectedPosition);
        }
    }

    internal static void ValidateSubscriptionDeliveryAuthority(GroupDelivery delivery, long expectedPosition)
    {
        if (delivery.Position != expectedPosition || delivery.Position < SubscriptionGroups.SubscriptionGroupsMinimumPositiveCount || delivery.Attempts < SubscriptionGroups.SubscriptionGroupsInitialSequence || delivery.LeaseVersion < SubscriptionGroups.SubscriptionGroupsInitialSequence || !Enum.IsDefined(delivery.State) || delivery.State == GroupDeliveryState.Leased && (delivery.LeaseUntil is null || string.IsNullOrEmpty(delivery.PrincipalId) || delivery.Attempts < SubscriptionGroups.SubscriptionGroupsMinimumPositiveCount || delivery.LeaseVersion < SubscriptionGroups.SubscriptionGroupsMinimumPositiveCount))
        {
            throw Errors.Fail(ErrorCode.Corruption, SubscriptionGroups.SubscriptionGroupsSubscriptionDeliveryWindowIsInconsistentDetail);
        }
    }
}
