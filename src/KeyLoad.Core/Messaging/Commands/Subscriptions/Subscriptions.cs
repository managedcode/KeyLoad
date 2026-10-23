using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes Subscriptions operations against the caller's database context.</summary>
public static class Subscriptions
{
    internal const string SubscriptionUpdateShapeDetail = "A subscription definition update requires a positive generation without a seek offset.";
    internal const string SubscriptionUpdatePauseDetail = "The subscription must be paused before its definition is updated.";
    internal const string SubscriptionUpdateWindowDetail = "The subscription delivery window is inconsistent.";
    internal static SubscriptionInfo UpdateSubscriptionDefinition(this DatabaseEngine engine, IAtomicTransaction tx, ConfigureSubscriptionRequest request, SubscriptionDefinition definition, byte[] key)
    {
        if (request.ExpectedGeneration is not > SubscriptionGroups.SubscriptionGroupsInitialSequence || request.Start != SubscriptionStart.FromBeginning || request.Cursor is not null)
        {
            throw Errors.Fail(ErrorCode.Validation, SubscriptionUpdateShapeDetail);
        }

        var state = SubscriptionGroups.Group(tx, request.Subscription);
        if (state.Generation != request.ExpectedGeneration)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, SubscriptionGroups.SubscriptionGroupsSubscriptionGenerationChangedDetail);
        }

        if (!state.Paused)
        {
            throw Errors.Fail(ErrorCode.Conflict, SubscriptionUpdatePauseDetail);
        }

        var head = engine.SourceHead(tx, request.Subscription.Source);
        if (state.Checkpoint < head.FirstAvailablePosition - SubscriptionGroups.SubscriptionGroupsAdjacentElementOffset || state.Checkpoint > head.TailPosition)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, SubscriptionGroups.SubscriptionGroupsSubscriptionHistoryIsUnavailableDetail);
        }

        if (state.IssuedPosition < state.Checkpoint || state.IssuedPosition > head.TailPosition)
        {
            throw Errors.Fail(ErrorCode.Corruption, SubscriptionUpdateWindowDetail);
        }

        var windowBudget = new SubscriptionWindowAuthorityReadBudget(engine.OperationLimitsOptions);
        engine.ValidateSubscriptionGroupProgress(tx, request.Subscription, state, head, windowBudget);
        if (JsonData.Fingerprint(state.Definition) == JsonData.Fingerprint(definition))
        {
            return SubscriptionGroups.ValidatedGroupInfo(request.Subscription, state, head);
        }

        var next = state with
        {
            Definition = definition,
            Generation = checked(state.Generation + SubscriptionGroups.SubscriptionGroupsAdjacentElementOffset),
            OwnershipEpoch = checked(state.OwnershipEpoch + SubscriptionGroups.SubscriptionGroupsAdjacentElementOffset),
            IssuedPosition = state.Checkpoint,
            Paused = true,
            SafeFailureCode = null
        };
        ClearValidatedSubscriptionDefinitionWindow(tx, request.Subscription, state);
        tx.PutRecord(key, next);
        engine.ValidateSubscriptionGroupProgress(tx, request.Subscription, next, head, windowBudget);
        return SubscriptionGroups.ValidatedGroupInfo(request.Subscription, next, head);
    }

    internal static void ClearValidatedSubscriptionDefinitionWindow(IAtomicTransaction tx, SubscriptionRef subscription, GroupState state)
    {
        var count = checked((int)(state.IssuedPosition - state.Checkpoint));
        for (var offset = SubscriptionGroups.SubscriptionGroupsAdjacentElementOffset; offset <= count; offset++)
        {
            tx.Delete(MessagingSerialization.GroupKey(SubscriptionGroups.SubscriptionGroupsSubscriptionWindowKeySpace, subscription, state.Generation, checked(state.Checkpoint + offset)));
        }
    }
}
