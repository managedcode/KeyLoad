using KeyLoad.Core.CommandOutcomes.Execution;
using KeyLoad.Core.CommandOutcomes.Identity;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes CommandOutcomeOperations against the caller's database context.</summary>
public static class CommandOutcomeOperations
{
    internal const string MissingDurableOutcomeMessage = "The applied command has no durable outcome.";
    internal const string EarlierOutcomeIncarnationMessage = "The command belongs to an earlier incarnation.";
    internal const string ChangedOutcomePrincipalPolicyMessage = "The principal policy changed since this command was evaluated.";
    internal const string EarlierSubscriptionGenerationMessage = "The cached receive belongs to an earlier subscription generation.";
    /// <summary>Resolves a replicated outcome and rechecks current authorization and content identity.</summary>
    /// <param name = "operation">Operation whose persisted result is requested.</param>
    /// <returns>The reauthorized stored result or a safe domain failure.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static Result<object> ResolveOutcome(this DatabaseEngine engine, Command<DatabaseCommand> operation)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(operation);
        return engine.ResolveOutcomeCore(engine.OwnCommand(operation));
    }

    internal static Result<object> ResolveOutcomeCore(this DatabaseEngine engine, Command<DatabaseCommand> operation) => engine.Store.Read(view =>
    {
        try
        {
            return engine.ReadValidatedSubmissionOutcome(view, operation);
        }
        catch (KeyLoadException exception)
        {
            return ManagedCode.Communication.Result<object>.Fail(Errors.Problem(exception.Code, exception.Message));
        }
    });
    /// <summary>Returns only the actual validated stored outcome; observation failures retain their cause.</summary>
    /// <param name = "operation">Original execution operation with its stable command identity.</param>
    /// <returns>The canonical stored result after all current authority and identity checks.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    internal static Result<object> ResolveSubmissionOutcome(this DatabaseEngine engine, Command<DatabaseCommand> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var normalized = engine.OwnCommand(operation);
        return engine.Store.Read(view => engine.ReadValidatedSubmissionOutcome(view, normalized));
    }

    internal static Result<object> ReadValidatedSubmissionOutcome(this DatabaseEngine engine, IKeyValueView view, Command<DatabaseCommand> operation, CommandOutcomeSelection? verifiedSelection = null)
    {
        var principal = engine.Principal(view, operation.UserId!, engine.Clock.GetUtcNow());
        _ = (engine.AuthorizeOperation(view, principal, operation)).Value;
        var scope = CommandOutcomePartitionIdentity.Resolve(operation);
        var selection = verifiedSelection ?? CommandOutcomeKeyResolver.Select(view, operation.UserId!, operation.CommandId, scope);
        var outcome = selection.Outcome ?? throw Errors.Fail(ErrorCode.RecoveryRequired, MissingDurableOutcomeMessage);
        engine.ValidateSubmissionOutcomeIdentity(view, principal, operation, selection, outcome, verifiedSelection.HasValue);
        engine.ValidateCachedResult(view, principal, engine.EvaluateCommand(operation, engine.Clock.GetUtcNow()), outcome);
        return outcome.Result;
    }

    internal static void ValidateSubmissionOutcomeIdentity(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, Command<DatabaseCommand> operation, CommandOutcomeSelection selection, StoredOutcome outcome, bool originalIdentityVerified = false)
    {
        if (outcome.PolicyEpoch != principal.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, ChangedOutcomePrincipalPolicyMessage);
        }

        if (!originalIdentityVerified)
        {
            engine.ValidateStoredSubmissionIdentity(view, operation, selection, outcome);
        }
    }

    internal static void ValidateCachedResult(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, Command<DatabaseCommand> operation, StoredOutcome previous)
    {
        if (previous.PolicyEpoch != principal.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, ChangedOutcomePrincipalPolicyMessage);
        }

        FileAuthorization.ValidateFileCachedOutcome(view, principal, operation);
        engine.ValidateBm25CachedOutcome(view, principal, operation, previous);
        engine.ValidateOnlineTextCachedOutcome(view, principal, operation, previous);
        engine.ValidateCompositionOutcome(view, principal, operation, previous);
        engine.ReauthorizeExtendedOutcome(view, principal, operation, previous);
        if (operation.Value!.Kind == OperationKind.Receive && Errors.Code(previous.Result.Problem).Value is null)
        {
            engine.ValidateCachedQueueReceive(view, principal, operation, previous.Result);
        }

        if (operation.Value!.Kind == OperationKind.ReceiveSubscription && Errors.Code(previous.Result.Problem).Value is null)
        {
            engine.ValidateCachedSubscriptionReceive(view, principal, operation, previous.Result);
        }

        if (operation.Value!.Kind == OperationKind.SubscriptionProcessing && Errors.Code(previous.Result.Problem).Value is null)
        {
            var processing = OperationProtocol.Payload<SubscriptionProcessingRequest>(operation);
            engine.ValidateGroupClaims(view, principal, processing.Subscription, processing.Token, operation.Timestamp);
            engine.ReauthorizeEffects(view, principal, processing.Subscription.Source.Partition, processing.Effects);
        }

        if (operation.Value!.Kind == OperationKind.CommitProjectionBatch && Errors.Code(previous.Result.Problem).Value is null)
        {
            var projection = OperationProtocol.Payload<CommitProjectionBatchRequest>(operation);
            engine.ProjectionClaims(view, projection, operation.Timestamp, allowExpiredReceipt: true);
            engine.ReauthorizeEffects(view, principal, projection.Consumer.Partition, projection.Effects);
        }
    }

    internal static void ReauthorizeExtendedOutcome(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, Command<DatabaseCommand> operation, StoredOutcome previous)
    {
        if (Errors.Code(previous.Result.Problem).Value is not null)
        {
            return;
        }

        if (operation.Value!.Kind == OperationKind.Batch)
        {
            var command = OperationProtocol.Payload<CommandRequest>(operation);
            engine.ReauthorizeExtendedEffects(view, principal, command.Partition, command.Mutations);
        }
        else if (operation.Value!.Kind == OperationKind.CommitInbox)
        {
            var inbox = OperationProtocol.Payload<CommitInboxRequest>(operation);
            engine.ReauthorizeEffects(view, principal, inbox.Target.Partition, inbox.Effects);
        }
        else if (operation.Value!.Kind == OperationKind.Processing)
        {
            var processing = OperationProtocol.Payload<ProcessingRequest>(operation);
            engine.ReauthorizeExtendedEffects(view, principal, processing.Lane.Partition, processing.Effects);
        }
    }

    internal static void ReauthorizeExtendedEffects(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, System.Collections.Immutable.ImmutableArray<Mutation> effects)
    {
        foreach (var effect in effects)
        {
            if (effect is InsertTypedRow or RedriveQueueMessage or CancelQueueMessage or ParkPendingQueueMessage or global::KeyLoad.ApplyVectorProjection or CreateQueueTransfer or AcceptQueueTransfer or CompleteQueueTransfer or AdvanceQueueTransferAttempt or AdvanceQueueTransferRepair or ConfigureRecurringSchedule or EmitRecurringOccurrences or CancelRecurringSchedule or CompareExchangeSaga or ExpireSaga)
            {
                engine.ReauthorizeEffect(view, principal, partition, effect);
            }
        }
    }

    internal static void ValidateCachedQueueReceive(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, Command<DatabaseCommand> operation, Result<object> result)
    {
        var deliveries = result.Get<ReceiveResult>().Deliveries;
        if (deliveries.IsEmpty)
        {
            return;
        }

        var lane = OperationProtocol.Payload<ReceiveRequest>(operation).Lane;
        foreach (var delivery in deliveries)
        {
            engine.Lease(view, principal, lane, delivery.Token, operation.Timestamp);
            var resource = engine.Resource(view, lane.Partition, lane.Queue, ResourceKind.WorkQueue);
            engine.RequireCachedDeliveryProjection(principal, resource, delivery.PayloadJson, delivery.HeadersJson);
        }
    }

    internal static void ValidateCachedSubscriptionReceive(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, Command<DatabaseCommand> operation, Result<object> result)
    {
        var cached = result.Get<ReceiveSubscriptionResult>();
        var subscription = OperationProtocol.Payload<ReceiveSubscriptionRequest>(operation).Subscription;
        var state = SubscriptionGroups.Group(view, subscription);
        if (cached.Status.Generation != state.Generation || cached.Status.OwnershipEpoch != state.OwnershipEpoch)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, EarlierSubscriptionGenerationMessage);
        }

        foreach (var delivery in cached.Deliveries)
        {
            var lease = engine.ReadSubscriptionLease(view, principal, subscription, delivery.Token, operation.Timestamp);
            var resource = engine.SourceResource(view, subscription.Source);
            var dataPrincipal = engine.Principal(view, lease.State.Definition.DataPrincipalId, operation.Timestamp);
            engine.RequireCachedDeliveryProjection(dataPrincipal, resource, delivery.Event.Data.PayloadJson, delivery.Event.Data.HeadersJson);
            engine.RequireCachedDeliveryProjection(principal, resource, delivery.Event.Data.PayloadJson, delivery.Event.Data.HeadersJson);
        }

        SubscriptionDeliveryAuthority.ValidateSubscriptionGroupProgress(view, subscription, state, engine.SourceHead(view, subscription.Source));
    }
}
