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
    public static Result<object> /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */ ResolveOutcome(this DatabaseEngine engine, Command<DatabaseCommand> operation)
    {
        ArgumentNullException.ThrowIfNull(engine) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(operation) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        return engine.ResolveOutcomeCore(engine.OwnCommand(operation)) /* KLD0041: This step reads the existing native persisted outcome after identity and authority checks while retaining its original heterogeneous payload. */;
    }

    internal static Result<object> /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */ ResolveOutcomeCore(this DatabaseEngine engine, Command<DatabaseCommand> operation) => engine.Store.Read(view =>
    {
        try
        {
            return engine.ReadValidatedSubmissionOutcome(view, operation) /* KLD0041: This step reads the existing native persisted outcome after identity and authority checks while retaining its original heterogeneous payload. */;
        }
        catch (KeyLoadException exception)
        {
            return ManagedCode.Communication.Result<object>.Fail(Errors.Problem(exception.Code, exception.Message)) /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */;
        }
    }) /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */;
    /// <summary>Returns only the actual validated stored outcome; observation failures retain their cause.</summary>
    /// <param name = "operation">Original execution operation with its stable command identity.</param>
    /// <returns>The canonical stored result after all current authority and identity checks.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    internal static Result<object> /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */ ResolveSubmissionOutcome(this DatabaseEngine engine, Command<DatabaseCommand> operation)
    {
        ArgumentNullException.ThrowIfNull(operation) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        var normalized = engine.OwnCommand(operation);
        return engine.Store.Read(view => (engine.ReadValidatedSubmissionOutcome(view, normalized) /* KLD0041: This step reads the existing native persisted outcome after identity and authority checks while retaining its original heterogeneous payload. */)) /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */;
    }

    internal static Result<object> /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */ ReadValidatedSubmissionOutcome(this DatabaseEngine engine, IKeyValueView view, Command<DatabaseCommand> operation, CommandOutcomeSelection? verifiedSelection = null, PrincipalRecord? verifiedPrincipal = null)
    {
        var principal = verifiedPrincipal ?? engine.Principal(view, operation.UserId!, engine.Clock.GetUtcNow());
        if (verifiedPrincipal is null)
        {
            _ = (engine.AuthorizeOperation(view, principal, operation)).Value;
        }
        var scope = CommandOutcomePartitionIdentity.Resolve(operation);
        var selection = verifiedSelection ?? CommandOutcomeKeyResolver.Select(view, operation.UserId!, operation.CommandId, scope);
        var outcome = selection.Outcome ?? throw Errors.Fail(ErrorCode.RecoveryRequired, MissingDurableOutcomeMessage);
        engine.ValidateSubmissionOutcomeIdentity(view, principal, operation, selection, outcome, verifiedSelection.HasValue);
        engine.ValidateCachedResult(view, principal, engine.EvaluateCommand(operation, engine.Clock.GetUtcNow()), outcome);
        return outcome.Result /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */;
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
        if (operation.Value!.Kind == OperationKind.Receive && Errors.Code(previous.Result /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */.Problem).Value is null)
        {
            engine.ValidateCachedQueueReceive(view, principal, operation, previous.Result /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */) /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */;
        }

        if (operation.Value!.Kind == OperationKind.ReceiveSubscription && Errors.Code(previous.Result /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */.Problem).Value is null)
        {
            engine.ValidateCachedSubscriptionReceive(view, principal, operation, previous.Result /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */) /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */;
        }

        if (operation.Value!.Kind == OperationKind.SubscriptionProcessing && Errors.Code(previous.Result /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */.Problem).Value is null)
        {
            var processing = OperationProtocol.Payload<SubscriptionProcessingRequest>(operation);
            engine.ValidateGroupClaims(view, principal, processing.Subscription, processing.Token, operation.Timestamp);
            engine.ReauthorizeEffects(view, principal, processing.Subscription.Source.Partition, processing.Effects);
        }

        if (operation.Value!.Kind == OperationKind.CommitProjectionBatch && Errors.Code(previous.Result /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */.Problem).Value is null)
        {
            var projection = OperationProtocol.Payload<CommitProjectionBatchRequest>(operation);
            engine.ProjectionClaims(view, projection, operation.Timestamp, allowExpiredReceipt: true);
            engine.ReauthorizeEffects(view, principal, projection.Consumer.Partition, projection.Effects);
        }
    }

    internal static void ReauthorizeExtendedOutcome(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, Command<DatabaseCommand> operation, StoredOutcome previous)
    {
        if (Errors.Code(previous.Result /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */.Problem).Value is not null)
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

    internal static void ValidateCachedQueueReceive(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, Command<DatabaseCommand> operation, Result<object> /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */ result)
    {
        var deliveries = result.Get<ReceiveResult>() /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */.Deliveries;
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

    internal static void ValidateCachedSubscriptionReceive(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, Command<DatabaseCommand> operation, Result<object> /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */ result)
    {
        var cached = result.Get<ReceiveSubscriptionResult>() /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */;
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
