using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes MessagingValidationQueueLifecycleSubscriptionDeliveryAuthority operations against the caller's database context.</summary>
public static class MessagingValidationQueueLifecycleSubscriptionDeliveryAuthority
{
    internal const string SubscriptionGroupsDeliveryLeaseVersionExhaustedDetail = "The subscription delivery lease version is exhausted.";
    internal static void ValidateSubscriptionGroupLowerBounds(GroupState state)
    {
        if (state.Checkpoint < MessagingExecutionSubscriptionGroups.SubscriptionGroupsInitialSequence || state.IssuedPosition < state.Checkpoint)
        {
            throw Errors.Fail(ErrorCode.Corruption, MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionDeliveryWindowIsInconsistentDetail);
        }
    }

    internal static void ValidateSubscriptionGroupProgress(this DatabaseEngine engine, IKeyValueView view, SubscriptionRef subscription, GroupState state, EventSourceHead head, SubscriptionWindowAuthorityReadBudget? budget = null)
    {
        ValidateSubscriptionGroupLowerBounds(state);
        if (state.Checkpoint > head.TailPosition || state.IssuedPosition < state.Checkpoint || state.IssuedPosition > head.TailPosition || state.IssuedPosition - state.Checkpoint > state.Definition.Policy.MaxWindow)
        {
            throw Errors.Fail(ErrorCode.Corruption, MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionDeliveryWindowIsInconsistentDetail);
        }

        ValidateSubscriptionGroupWindow(view, subscription, state, budget ?? new SubscriptionWindowAuthorityReadBudget(engine.OperationLimitsOptions));
    }

    internal static void ValidateSubscriptionGroupWindow(IKeyValueView view, SubscriptionRef subscription, GroupState state, SubscriptionWindowAuthorityReadBudget budget)
    {
        var maximum = state.Definition.Policy.MaxWindow;
        if (maximum is < MessagingExecutionSubscriptionGroups.SubscriptionGroupsMinimumPositiveCount or > MessagingExecutionSubscriptionGroups.SubscriptionGroupsMaximumSubscriptionWindow)
        {
            throw Errors.Fail(ErrorCode.Corruption, MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionDeliveryWindowIsInconsistentDetail);
        }

        var rows = MessagingExecutionSubscriptionGroups.SubscriptionGroupsEmptyElementCount;
        var scan = view.VisitRange(MessagingSerialization.GroupKey(MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionWindowKeySpace, subscription, state.Generation), checked(maximum + MessagingExecutionSubscriptionGroups.SubscriptionGroupsAdjacentElementOffset), (key, value) =>
        {
            var delivery = OrleansSerialization.Deserialize<GroupDelivery>(value) ?? throw Errors.Fail(ErrorCode.Corruption, MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionDeliveryWindowIsInconsistentDetail);
            ValidateSubscriptionDeliveryAuthority(delivery, delivery.Position);
            if (delivery.Position <= state.Checkpoint || delivery.Position > state.IssuedPosition || !key.SequenceEqual(MessagingSerialization.GroupKey(MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionWindowKeySpace, subscription, state.Generation, delivery.Position)))
            {
                throw Errors.Fail(ErrorCode.Corruption, MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionDeliveryWindowIsInconsistentDetail);
            }

            rows++;
            return true;
        }, observer: budget.Observe);
        if (scan.HasMore || scan.StoppedByVisitor || rows != state.IssuedPosition - state.Checkpoint)
        {
            throw Errors.Fail(ErrorCode.Corruption, MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionDeliveryWindowIsInconsistentDetail);
        }
    }

    internal static GroupDelivery? ReadSubscriptionWindowDelivery(IKeyValueView view, byte[] key, SubscriptionWindowAuthorityReadBudget budget)
    {
        GroupDelivery? delivery = null;
        view.ReadValue(key, value => delivery = OrleansSerialization.Deserialize<GroupDelivery>(value), budget.Observe);
        return delivery;
    }

    internal static void RequireSubscriptionWindowReference(GroupDelivery? delivery, GroupState state, long expectedPosition)
    {
        if (delivery is null && expectedPosition > state.Checkpoint && expectedPosition <= state.IssuedPosition)
        {
            throw Errors.Fail(ErrorCode.Corruption, MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionDeliveryWindowIsInconsistentDetail);
        }

        if (delivery is not null)
        {
            ValidateSubscriptionDeliveryAuthority(delivery, expectedPosition);
        }
    }

    internal static void ValidateSubscriptionDeliveryAuthority(GroupDelivery delivery, long expectedPosition)
    {
        if (delivery.Position != expectedPosition || delivery.Position < MessagingExecutionSubscriptionGroups.SubscriptionGroupsMinimumPositiveCount || delivery.Attempts < MessagingExecutionSubscriptionGroups.SubscriptionGroupsInitialSequence || delivery.LeaseVersion < MessagingExecutionSubscriptionGroups.SubscriptionGroupsInitialSequence || !Enum.IsDefined(delivery.State) || delivery.State == GroupDeliveryState.Leased && (delivery.LeaseUntil is null || string.IsNullOrEmpty(delivery.PrincipalId) || delivery.Attempts < MessagingExecutionSubscriptionGroups.SubscriptionGroupsMinimumPositiveCount || delivery.LeaseVersion < MessagingExecutionSubscriptionGroups.SubscriptionGroupsMinimumPositiveCount))
        {
            throw Errors.Fail(ErrorCode.Corruption, MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionDeliveryWindowIsInconsistentDetail);
        }
    }
}
