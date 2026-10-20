using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes MessagingCommandsSubscriptions operations against the caller's database context.</summary>
public static class MessagingCommandsSubscriptions
{
    internal const string SubscriptionUpdateShapeDetail = "A subscription definition update requires a positive generation without a seek offset.";
    internal const string SubscriptionUpdatePauseDetail = "The subscription must be paused before its definition is updated.";
    internal const string SubscriptionUpdateWindowDetail = "The subscription delivery window is inconsistent.";
    internal static SubscriptionInfo UpdateSubscriptionDefinition(this DatabaseEngine engine, IAtomicTransaction tx, ConfigureSubscriptionRequest request, SubscriptionDefinition definition, byte[] key)
    {
        if (request.ExpectedGeneration is not > MessagingExecutionSubscriptionGroups.SubscriptionGroupsInitialSequence || request.Start != SubscriptionStart.FromBeginning || request.Cursor is not null)
        {
            throw Errors.Fail(ErrorCode.Validation, SubscriptionUpdateShapeDetail);
        }

        var state = MessagingExecutionSubscriptionGroups.Group(tx, request.Subscription);
        if (state.Generation != request.ExpectedGeneration)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionGenerationChangedDetail);
        }

        if (!state.Paused)
        {
            throw Errors.Fail(ErrorCode.Conflict, SubscriptionUpdatePauseDetail);
        }

        var head = engine.SourceHead(tx, request.Subscription.Source);
        if (state.Checkpoint < head.FirstAvailablePosition - MessagingExecutionSubscriptionGroups.SubscriptionGroupsAdjacentElementOffset || state.Checkpoint > head.TailPosition)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionHistoryIsUnavailableDetail);
        }

        if (state.IssuedPosition < state.Checkpoint || state.IssuedPosition > head.TailPosition)
        {
            throw Errors.Fail(ErrorCode.Corruption, SubscriptionUpdateWindowDetail);
        }

        var windowBudget = new SubscriptionWindowAuthorityReadBudget(engine.OperationLimitsOptions);
        engine.ValidateSubscriptionGroupProgress(tx, request.Subscription, state, head, windowBudget);
        if (JsonData.Fingerprint(state.Definition) == JsonData.Fingerprint(definition))
        {
            return MessagingExecutionSubscriptionGroups.ValidatedGroupInfo(request.Subscription, state, head);
        }

        var next = state with
        {
            Definition = definition,
            Generation = checked(state.Generation + MessagingExecutionSubscriptionGroups.SubscriptionGroupsAdjacentElementOffset),
            OwnershipEpoch = checked(state.OwnershipEpoch + MessagingExecutionSubscriptionGroups.SubscriptionGroupsAdjacentElementOffset),
            IssuedPosition = state.Checkpoint,
            Paused = true,
            SafeFailureCode = null
        };
        ClearValidatedSubscriptionDefinitionWindow(tx, request.Subscription, state);
        tx.PutRecord(key, next);
        engine.ValidateSubscriptionGroupProgress(tx, request.Subscription, next, head, windowBudget);
        return MessagingExecutionSubscriptionGroups.ValidatedGroupInfo(request.Subscription, next, head);
    }

    internal static void ClearValidatedSubscriptionDefinitionWindow(IAtomicTransaction tx, SubscriptionRef subscription, GroupState state)
    {
        var count = checked((int)(state.IssuedPosition - state.Checkpoint));
        for (var offset = MessagingExecutionSubscriptionGroups.SubscriptionGroupsAdjacentElementOffset; offset <= count; offset++)
        {
            tx.Delete(MessagingSerialization.GroupKey(MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionWindowKeySpace, subscription, state.Generation, checked(state.Checkpoint + offset)));
        }
    }
}
