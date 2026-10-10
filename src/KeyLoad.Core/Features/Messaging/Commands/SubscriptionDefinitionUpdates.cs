using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string SubscriptionUpdateShapeDetail = "A subscription definition update requires a positive generation without a seek offset.";
    private const string SubscriptionUpdatePauseDetail = "The subscription must be paused before its definition is updated.";
    private const string SubscriptionUpdateWindowDetail = "The subscription delivery window is inconsistent.";

    private SubscriptionInfo UpdateSubscriptionDefinition(IAtomicTransaction tx, ConfigureSubscriptionRequest request,
        SubscriptionDefinition definition, byte[] key)
    {
        if (request.ExpectedGeneration is not > SubscriptionGroupsInitialSequence
            || request.Start != SubscriptionStart.FromBeginning || request.Cursor is not null)
        { throw Errors.Fail(ErrorCode.Validation, SubscriptionUpdateShapeDetail); }
        var state = Group(tx, request.Subscription);
        if (state.Generation != request.ExpectedGeneration)
        { throw Errors.Fail(ErrorCode.RevisionConflict, SubscriptionGroupsSubscriptionGenerationChangedDetail); }
        if (!state.Paused)
        { throw Errors.Fail(ErrorCode.Conflict, SubscriptionUpdatePauseDetail); }
        var head = SourceHead(tx, request.Subscription.Source);
        if (state.Checkpoint < head.FirstAvailablePosition - SubscriptionGroupsAdjacentElementOffset
            || state.Checkpoint > head.TailPosition)
        { throw Errors.Fail(ErrorCode.HistoryUnavailable, SubscriptionGroupsSubscriptionHistoryIsUnavailableDetail); }
        if (state.IssuedPosition < state.Checkpoint || state.IssuedPosition > head.TailPosition)
        { throw Errors.Fail(ErrorCode.Corruption, SubscriptionUpdateWindowDetail); }
        if (JsonData.Fingerprint(state.Definition) == JsonData.Fingerprint(definition))
        { return GroupInfo(tx, request.Subscription, state); }
        var next = state with
        {
            Definition = definition,
            Generation = checked(state.Generation + SubscriptionGroupsAdjacentElementOffset),
            OwnershipEpoch = checked(state.OwnershipEpoch + SubscriptionGroupsAdjacentElementOffset),
            IssuedPosition = state.Checkpoint,
            Paused = true,
            SafeFailureCode = null
        };
        ClearSubscriptionDefinitionWindow(tx, request.Subscription, state);
        tx.PutRecord(key, next);
        return GroupInfo(tx, request.Subscription, next);
    }

    private static void ClearSubscriptionDefinitionWindow(IAtomicTransaction tx, SubscriptionRef subscription,
        GroupState state)
    {
        if (state.Definition.Policy.MaxWindow is < SubscriptionGroupsMinimumPositiveCount or > SubscriptionGroupsMaximumSubscriptionWindow
            || state.IssuedPosition < state.Checkpoint)
        { throw Errors.Fail(ErrorCode.Corruption, SubscriptionUpdateWindowDetail); }
        var page = tx.Scan(GroupKey(SubscriptionGroupsSubscriptionWindowKeySpace, subscription, state.Generation),
            checked(state.Definition.Policy.MaxWindow + SubscriptionGroupsAdjacentElementOffset));
        var rows = page.Records;
        if (page.HasMore || rows.Length > state.Definition.Policy.MaxWindow)
        { throw Errors.Fail(ErrorCode.Corruption, SubscriptionUpdateWindowDetail); }
        foreach (var row in rows)
        {
            var delivery = NativeSerialization.Deserialize<GroupDelivery>(row.Value.Span)
                ?? throw Errors.Fail(ErrorCode.Corruption, SubscriptionUpdateWindowDetail);
            if (delivery.Position <= state.Checkpoint || delivery.Position > state.IssuedPosition
                || !Enum.IsDefined(delivery.State)
                || !row.Key.Span.SequenceEqual(GroupKey(SubscriptionGroupsSubscriptionWindowKeySpace,
                    subscription, state.Generation, delivery.Position)))
            { throw Errors.Fail(ErrorCode.Corruption, SubscriptionUpdateWindowDetail); }
        }
        foreach (var row in rows)
        { tx.Delete(row.Key.ToArray()); }
    }
}
