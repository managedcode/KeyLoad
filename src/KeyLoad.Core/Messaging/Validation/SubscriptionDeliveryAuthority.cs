using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string SubscriptionGroupsDeliveryLeaseVersionExhaustedDetail = "The subscription delivery lease version is exhausted.";

    private static void ValidateSubscriptionGroupLowerBounds(GroupState state)
    {
        if (state.Checkpoint < SubscriptionGroupsInitialSequence || state.IssuedPosition < state.Checkpoint)
        {
            throw Errors.Fail(ErrorCode.Corruption, SubscriptionGroupsSubscriptionDeliveryWindowIsInconsistentDetail);
        }
    }

    private void ValidateSubscriptionGroupProgress(IKeyValueView view, SubscriptionRef subscription,
        GroupState state, EventSourceHead head, SubscriptionWindowAuthorityReadBudget? budget = null)
    {
        ValidateSubscriptionGroupLowerBounds(state);
        if (state.Checkpoint > head.TailPosition || state.IssuedPosition < state.Checkpoint
            || state.IssuedPosition > head.TailPosition
            || state.IssuedPosition - state.Checkpoint > state.Definition.Policy.MaxWindow)
        {
            throw Errors.Fail(ErrorCode.Corruption, SubscriptionGroupsSubscriptionDeliveryWindowIsInconsistentDetail);
        }
        ValidateSubscriptionGroupWindow(view, subscription, state,
            budget ?? new SubscriptionWindowAuthorityReadBudget(OperationLimitsOptions));
    }

    private static void ValidateSubscriptionGroupWindow(IKeyValueView view, SubscriptionRef subscription, GroupState state,
        SubscriptionWindowAuthorityReadBudget budget)
    {
        var maximum = state.Definition.Policy.MaxWindow;
        if (maximum is < SubscriptionGroupsMinimumPositiveCount or > SubscriptionGroupsMaximumSubscriptionWindow)
        {
            throw Errors.Fail(ErrorCode.Corruption, SubscriptionGroupsSubscriptionDeliveryWindowIsInconsistentDetail);
        }
        var rows = SubscriptionGroupsEmptyElementCount;
        var scan = view.VisitRange(GroupKey(SubscriptionGroupsSubscriptionWindowKeySpace, subscription, state.Generation),
            checked(maximum + SubscriptionGroupsAdjacentElementOffset), (key, value) =>
            {
                var delivery = NativeSerialization.Deserialize<GroupDelivery>(value)
                    ?? throw Errors.Fail(ErrorCode.Corruption, SubscriptionGroupsSubscriptionDeliveryWindowIsInconsistentDetail);
                ValidateSubscriptionDeliveryAuthority(delivery, delivery.Position);
                if (delivery.Position <= state.Checkpoint || delivery.Position > state.IssuedPosition
                    || !key.SequenceEqual(GroupKey(SubscriptionGroupsSubscriptionWindowKeySpace,
                        subscription, state.Generation, delivery.Position)))
                {
                    throw Errors.Fail(ErrorCode.Corruption, SubscriptionGroupsSubscriptionDeliveryWindowIsInconsistentDetail);
                }
                rows++;
                return true;
            }, observer: budget.Observe);
        if (scan.HasMore || scan.StoppedByVisitor || rows != state.IssuedPosition - state.Checkpoint)
        {
            throw Errors.Fail(ErrorCode.Corruption, SubscriptionGroupsSubscriptionDeliveryWindowIsInconsistentDetail);
        }
    }

    private static GroupDelivery? ReadSubscriptionWindowDelivery(IKeyValueView view, byte[] key,
        SubscriptionWindowAuthorityReadBudget budget)
    {
        GroupDelivery? delivery = null;
        view.ReadValue(key, value => delivery = NativeSerialization.Deserialize<GroupDelivery>(value), budget.Observe);
        return delivery;
    }

    private static void RequireSubscriptionWindowReference(GroupDelivery? delivery, GroupState state, long expectedPosition)
    {
        if (delivery is null && expectedPosition > state.Checkpoint && expectedPosition <= state.IssuedPosition)
        {
            throw Errors.Fail(ErrorCode.Corruption, SubscriptionGroupsSubscriptionDeliveryWindowIsInconsistentDetail);
        }
        if (delivery is not null)
        {
            ValidateSubscriptionDeliveryAuthority(delivery, expectedPosition);
        }
    }

    private static void ValidateSubscriptionDeliveryAuthority(GroupDelivery delivery, long expectedPosition)
    {
        if (delivery.Position != expectedPosition || delivery.Position < SubscriptionGroupsMinimumPositiveCount
            || delivery.Attempts < SubscriptionGroupsInitialSequence || delivery.LeaseVersion < SubscriptionGroupsInitialSequence
            || !Enum.IsDefined(delivery.State)
            || delivery.State == GroupDeliveryState.Leased
                && (delivery.LeaseUntil is null || string.IsNullOrEmpty(delivery.PrincipalId)
                    || delivery.Attempts < SubscriptionGroupsMinimumPositiveCount
                    || delivery.LeaseVersion < SubscriptionGroupsMinimumPositiveCount))
        {
            throw Errors.Fail(ErrorCode.Corruption, SubscriptionGroupsSubscriptionDeliveryWindowIsInconsistentDetail);
        }
    }
}
