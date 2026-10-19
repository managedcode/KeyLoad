using KeyLoad.Core.Features.CommandOutcomes.Execution;
using KeyLoad.Core.Features.CommandOutcomes.Identity;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string MissingDurableOutcomeMessage = "The applied command has no durable outcome.";
    private const string EarlierOutcomeIncarnationMessage = "The command belongs to an earlier incarnation.";
    private const string ChangedOutcomePrincipalPolicyMessage = "The principal policy changed since this command was evaluated.";
    private const string EarlierSubscriptionGenerationMessage = "The cached receive belongs to an earlier subscription generation.";

    /// <summary>Resolves a replicated outcome and rechecks current authorization and content identity.</summary>
    /// <param name="operation">Operation whose persisted result is requested.</param>
    /// <returns>The reauthorized stored result or a safe domain failure.</returns>
    public Result<object> ResolveOutcome(Command<DatabaseCommand> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ResolveOutcomeCore(OwnCommand(operation));
    }
    private Result<object> ResolveOutcomeCore(Command<DatabaseCommand> operation) => Store.Read(view =>
    {
        try
        {
            return ReadValidatedSubmissionOutcome(view, operation);
        }
        catch (KeyLoadException exception) { return ManagedCode.Communication.Result<object>.Fail(Errors.Problem(exception.Code, exception.Message)); }
    });

    /// <summary>Returns only the actual validated stored outcome; observation failures retain their cause.</summary>
    /// <param name="operation">Original execution operation with its stable command identity.</param>
    /// <returns>The canonical stored result after all current authority and identity checks.</returns>
    internal Result<object> ResolveSubmissionOutcome(Command<DatabaseCommand> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var normalized = OwnCommand(operation);
        return Store.Read(view => ReadValidatedSubmissionOutcome(view, normalized));
    }

    private Result<object> ReadValidatedSubmissionOutcome(IKeyValueView view, Command<DatabaseCommand> operation,
        CommandOutcomeSelection? verifiedSelection = null)
    {
        var principal = Principal(view, operation.UserId!, Clock.GetUtcNow());
        AuthorizeOperation(view, principal, operation);
        var scope = CommandOutcomePartitionIdentity.Resolve(operation);
        var selection = verifiedSelection ?? CommandOutcomeKeyResolver.Select(view, operation.UserId!, operation.CommandId, scope);
        var outcome = selection.Outcome
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, MissingDurableOutcomeMessage);
        ValidateSubmissionOutcomeIdentity(view, principal, operation, selection, outcome, verifiedSelection.HasValue);
        ValidateCachedResult(view, principal, EvaluateCommand(operation, Clock.GetUtcNow()), outcome);
        return outcome.Result;
    }
    private void ValidateSubmissionOutcomeIdentity(IKeyValueView view, PrincipalRecord principal,
        Command<DatabaseCommand> operation, CommandOutcomeSelection selection, StoredOutcome outcome, bool originalIdentityVerified = false)
    {
        if (outcome.PolicyEpoch != principal.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, ChangedOutcomePrincipalPolicyMessage);
        }
        if (!originalIdentityVerified)
        {
            ValidateStoredSubmissionIdentity(view, operation, selection, outcome);
        }
    }

    private void ValidateCachedResult(IKeyValueView view, PrincipalRecord principal, Command<DatabaseCommand> operation, StoredOutcome previous)
    {
        if (previous.PolicyEpoch != principal.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, ChangedOutcomePrincipalPolicyMessage);
        }

        ValidateFileCachedOutcome(view, principal, operation);
        ValidateBm25CachedOutcome(view, principal, operation, previous);
        ValidateOnlineTextCachedOutcome(view, principal, operation, previous);
        ValidateCompositionOutcome(view, principal, operation, previous);
        ReauthorizeExtendedOutcome(view, principal, operation, previous);

        if (operation.Value!.Kind == OperationKind.Receive && Errors.Code(previous.Result.Problem) is null)
        {
            ValidateCachedQueueReceive(view, principal, operation, previous.Result);
        }

        if (operation.Value!.Kind == OperationKind.ReceiveSubscription && Errors.Code(previous.Result.Problem) is null)
        {
            ValidateCachedSubscriptionReceive(view, principal, operation, previous.Result);
        }
        if (operation.Value!.Kind == OperationKind.SubscriptionProcessing && Errors.Code(previous.Result.Problem) is null)
        {
            var processing = Payload<SubscriptionProcessingRequest>(operation);
            ValidateGroupClaims(view, principal, processing.Subscription, processing.Token, operation.Timestamp);
            ReauthorizeEffects(view, principal, processing.Subscription.Source.Partition, processing.Effects);
        }
        if (operation.Value!.Kind == OperationKind.CommitProjectionBatch && Errors.Code(previous.Result.Problem) is null)
        {
            var projection = Payload<CommitProjectionBatchRequest>(operation);
            ProjectionClaims(view, projection, operation.Timestamp, allowExpiredReceipt: true);
            ReauthorizeEffects(view, principal, projection.Consumer.Partition, projection.Effects);
        }
    }

    private void ReauthorizeExtendedOutcome(IKeyValueView view, PrincipalRecord principal, Command<DatabaseCommand> operation,
        StoredOutcome previous)
    {
        if (Errors.Code(previous.Result.Problem) is not null)
        {
            return;
        }
        if (operation.Value!.Kind == OperationKind.Batch)
        {
            var command = Payload<CommandRequest>(operation);
            ReauthorizeExtendedEffects(view, principal, command.Partition, command.Mutations);
        }
        else if (operation.Value!.Kind == OperationKind.CommitInbox)
        {
            var inbox = Payload<CommitInboxRequest>(operation);
            ReauthorizeEffects(view, principal, inbox.Target.Partition, inbox.Effects);
        }
        else if (operation.Value!.Kind == OperationKind.Processing)
        {
            var processing = Payload<ProcessingRequest>(operation);
            ReauthorizeExtendedEffects(view, principal, processing.Lane.Partition, processing.Effects);
        }
    }

    private void ReauthorizeExtendedEffects(IKeyValueView view, PrincipalRecord principal, PartitionRef partition,
        System.Collections.Immutable.ImmutableArray<Mutation> effects)
    {
        foreach (var effect in effects)
        {
            if (effect is InsertTypedRow or RedriveQueueMessage or CancelQueueMessage or ParkPendingQueueMessage
                or global::KeyLoad.ApplyVectorProjection or CreateQueueTransfer or AcceptQueueTransfer or CompleteQueueTransfer or AdvanceQueueTransferAttempt or AdvanceQueueTransferRepair
                or ConfigureRecurringSchedule or EmitRecurringOccurrences or CancelRecurringSchedule or CompareExchangeSaga or ExpireSaga)
            {
                ReauthorizeEffect(view, principal, partition, effect);
            }
        }
    }

    private void ValidateCachedQueueReceive(IKeyValueView view, PrincipalRecord principal,
        Command<DatabaseCommand> operation, Result<object> result)
    {
        var deliveries = result.Get<ReceiveResult>().Deliveries;
        if (deliveries.IsEmpty)
        {
            return;
        }

        var lane = Payload<ReceiveRequest>(operation).Lane;
        foreach (var delivery in deliveries)
        {
            Lease(view, principal, lane, delivery.Token, operation.Timestamp);
            var resource = Resource(view, lane.Partition, lane.Queue, ResourceKind.WorkQueue);
            RequireCachedDeliveryProjection(principal, resource, delivery.PayloadJson, delivery.HeadersJson);
        }
    }

    private void ValidateCachedSubscriptionReceive(IKeyValueView view, PrincipalRecord principal,
        Command<DatabaseCommand> operation, Result<object> result)
    {
        var cached = result.Get<ReceiveSubscriptionResult>();
        var subscription = Payload<ReceiveSubscriptionRequest>(operation).Subscription;
        var state = Group(view, subscription);
        if (cached.Status.Generation != state.Generation || cached.Status.OwnershipEpoch != state.OwnershipEpoch)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, EarlierSubscriptionGenerationMessage);
        }

        var budget = new SubscriptionWindowAuthorityReadBudget(OperationLimitsOptions);
        foreach (var delivery in cached.Deliveries)
        {
            var lease = ReadSubscriptionLease(view, principal, subscription, delivery.Token, operation.Timestamp, budget);
            var resource = SourceResource(view, subscription.Source);
            var dataPrincipal = Principal(view, lease.State.Definition.DataPrincipalId, operation.Timestamp);
            RequireCachedDeliveryProjection(dataPrincipal, resource, delivery.Event.Data.PayloadJson, delivery.Event.Data.HeadersJson);
            RequireCachedDeliveryProjection(principal, resource, delivery.Event.Data.PayloadJson, delivery.Event.Data.HeadersJson);
        }
        ValidateSubscriptionGroupProgress(view, subscription, state, SourceHead(view, subscription.Source), budget);
    }
}
