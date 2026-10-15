using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Storage;

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
    public OperationResult ResolveOutcome(ReplicatedOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ResolveOutcomeCore(NormalizeOperation(operation));
    }
    private OperationResult ResolveOutcomeCore(ReplicatedOperation operation) => Store.Read(view =>
    {
        try
        {
            return ReadValidatedSubmissionOutcome(view, operation);
        }
        catch (KeyLoadException exception) { return new OperationResult(null, exception.Code, exception.Message); }
    });

    /// <summary>Returns only the actual validated stored outcome; observation failures retain their cause.</summary>
    /// <param name="operation">Original native operation with its stable command identity.</param>
    /// <returns>The canonical stored result after all current authority and identity checks.</returns>
    internal OperationResult ResolveSubmissionOutcome(ReplicatedOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var normalized = NormalizeOperation(operation);
        return Store.Read(view => ReadValidatedSubmissionOutcome(view, normalized, allowStoredDocumentDenial: true));
    }

    private OperationResult ReadValidatedSubmissionOutcome(IKeyValueView view, ReplicatedOperation operation,
        bool allowStoredDocumentDenial = false, CommandOutcomeSelection? verifiedSelection = null)
    {
        var principal = Principal(view, operation.PrincipalId, Clock.GetUtcNow());
        RetiredOriginalOutcomeReadScope? retired = null;
        KeyLoadException? denied = null;
        try
        {
            retired = CaptureRetiredOriginalOutcomeReadScope(view, principal, operation);
            if (retired is null)
            {
                AuthorizeOperation(view, principal, operation);
            }
        }
        catch (KeyLoadException refusal) when (allowStoredDocumentDenial && refusal.Code == ErrorCode.PermissionDenied)
        {
            if (!IsOrdinaryDocumentSubmission(view, operation))
            {
                throw;
            }
            denied = refusal;
        }
        var scope = CommandOutcomePartitionIdentity.Resolve(operation);
        var selection = verifiedSelection ?? CommandOutcomeKeyResolver.Select(view, operation.PrincipalId, operation.Id, scope);
        var outcome = selection.Outcome;
        if (outcome is null)
        {
            if (denied is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(denied).Throw();
            }
            if (retired is not null)
            {
                AuthorizeOperation(view, principal, operation);
            }
            throw Errors.Fail(ErrorCode.RecoveryRequired, MissingDurableOutcomeMessage);
        }
        ValidateSubmissionOutcomeIdentity(view, principal, operation, selection, outcome, verifiedSelection.HasValue);
        if (denied is not null && (outcome.Result.Error != denied.Code
            || !string.Equals(outcome.Result.SafeDetail, denied.Message, StringComparison.Ordinal)
            || outcome.Result.Json is not null || outcome.Result.NativeValue is not null
            || outcome.BlobAuthority is not null || outcome.CompositionAuthority is not null
            || outcome.OnlineTextAuthority is not null || outcome.RemoteTransferFailureAuthority is not null
            || outcome.RemoteTransferAuthority is not null))
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(denied).Throw();
        }
        if (retired is { } retained)
        {
            if (outcome.Result.Error is not null)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch);
            }
            RequireRetiredOriginalOutcomeIdentity(retained.Command, operation.Id, outcome.Result.Get<CommitReceipt>(),
                retained.Fence, retained.Control, retained.Placement, retained.ControlOwner, retained.LocalOwner);
        }
        ValidateCachedResult(view, principal, operation with
        {
            EvaluatedAt = Clock.GetUtcNow()
        }, outcome,
            retired is null ? BatchResourceAdmission.CurrentPhysicalOwner : BatchResourceAdmission.RetiredOutcomeMetadata);
        return outcome.Result;
    }
    private void ValidateSubmissionOutcomeIdentity(IKeyValueView view, PrincipalRecord principal,
        ReplicatedOperation operation, CommandOutcomeSelection selection, StoredOutcome outcome, bool originalIdentityVerified = false)
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

    private void ValidateCachedResult(IKeyValueView view, PrincipalRecord principal, ReplicatedOperation operation, StoredOutcome previous,
        BatchResourceAdmission admission = BatchResourceAdmission.CurrentPhysicalOwner)
    {
        if (previous.PolicyEpoch != principal.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, ChangedOutcomePrincipalPolicyMessage);
        }

        if (BlobStorageOperations.Handles(operation.Kind))
        {
            new BlobStorageOperations(this).ValidateOutcomeAuthority(view, operation, previous.Result, previous.BlobAuthority);
        }
        if (operation.Kind is OperationKind.RemoteGraphSource or OperationKind.RemoteGraphReceive or OperationKind.RemoteGraphComplete)
        {
            ReauthorizeRemoteGraphOutcome(view, principal, operation, previous);
        }
        ValidateBm25CachedOutcome(view, principal, operation, previous);
        ValidateOnlineTextCachedOutcome(view, principal, operation, previous);
        ValidateCompositionOutcome(view, principal, operation, previous);
        if (!ValidateRemoteTransferCachedOutcome(view, principal, operation, previous))
        {
            ReauthorizeExtendedOutcome(view, principal, operation, previous, admission);
        }

        if (operation.Kind == OperationKind.Receive && previous.Result.Error is null)
        {
            ValidateCachedQueueReceive(view, principal, operation, previous.Result);
        }

        if (operation.Kind == OperationKind.ReceiveSubscription && previous.Result.Error is null)
        {
            ValidateCachedSubscriptionReceive(view, principal, operation, previous.Result);
        }
        if (operation.Kind == OperationKind.SubscriptionProcessing && previous.Result.Error is null)
        {
            var processing = Payload<SubscriptionProcessingRequest>(operation);
            ValidateGroupClaims(view, principal, processing.Subscription, processing.Token, operation.EvaluatedAt);
            ReauthorizeEffects(view, principal, processing.Subscription.Source.Partition, processing.Effects);
        }
        if (operation.Kind == OperationKind.CommitProjectionBatch && previous.Result.Error is null)
        {
            var projection = Payload<CommitProjectionBatchRequest>(operation);
            ProjectionClaims(view, projection, operation.EvaluatedAt, allowExpiredReceipt: true);
            ReauthorizeEffects(view, principal, projection.Consumer.Partition, projection.Effects);
        }
    }

    private void ReauthorizeExtendedOutcome(IKeyValueView view, PrincipalRecord principal, ReplicatedOperation operation,
        StoredOutcome previous, BatchResourceAdmission admission)
    {
        if (previous.Result.Error is not null)
        {
            return;
        }
        if (operation.Kind == OperationKind.Batch)
        {
            var command = Payload<CommandRequest>(operation);
            ReauthorizeExtendedEffects(view, principal, command.Partition, command.Mutations, admission);
        }
        else if (operation.Kind == OperationKind.CommitInbox)
        {
            var inbox = Payload<CommitInboxRequest>(operation);
            ReauthorizeEffects(view, principal, inbox.Target.Partition, inbox.Effects);
        }
        else if (operation.Kind == OperationKind.Processing)
        {
            var processing = Payload<ProcessingRequest>(operation);
            ReauthorizeExtendedEffects(view, principal, processing.Lane.Partition, processing.Effects);
        }
    }

    private void ReauthorizeExtendedEffects(IKeyValueView view, PrincipalRecord principal, PartitionRef partition,
        System.Collections.Immutable.ImmutableArray<Mutation> effects,
        BatchResourceAdmission admission = BatchResourceAdmission.CurrentPhysicalOwner)
    {
        foreach (var effect in effects)
        {
            if (effect is InsertTypedRow or RedriveQueueMessage or CancelQueueMessage or ParkPendingQueueMessage
                or global::KeyLoad.ApplyVectorProjection or CreateQueueTransfer or AcceptQueueTransfer or CompleteQueueTransfer or AdvanceQueueTransferAttempt or AdvanceQueueTransferRepair
                or ConfigureRecurringSchedule or EmitRecurringOccurrences or CancelRecurringSchedule or CompareExchangeSaga or ExpireSaga)
            {
                ReauthorizeEffect(view, principal, partition, effect, admission);
            }
        }
    }

    private void ValidateCachedQueueReceive(IKeyValueView view, PrincipalRecord principal,
        ReplicatedOperation operation, OperationResult result)
    {
        var deliveries = result.Get<ReceiveResult>().Deliveries;
        if (deliveries.IsEmpty)
        {
            return;
        }

        var lane = Payload<ReceiveRequest>(operation).Lane;
        foreach (var delivery in deliveries)
        {
            Lease(view, principal, lane, delivery.Token, operation.EvaluatedAt);
            var resource = Resource(view, lane.Partition, lane.Queue, ResourceKind.WorkQueue);
            RequireCachedDeliveryProjection(principal, resource, delivery.PayloadJson, delivery.HeadersJson);
        }
    }

    private void ValidateCachedSubscriptionReceive(IKeyValueView view, PrincipalRecord principal,
        ReplicatedOperation operation, OperationResult result)
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
            var lease = ReadSubscriptionLease(view, principal, subscription, delivery.Token, operation.EvaluatedAt, budget);
            var resource = SourceResource(view, subscription.Source);
            var dataPrincipal = Principal(view, lease.State.Definition.DataPrincipalId, operation.EvaluatedAt);
            RequireCachedDeliveryProjection(dataPrincipal, resource, delivery.Event.Data.PayloadJson, delivery.Event.Data.HeadersJson);
            RequireCachedDeliveryProjection(principal, resource, delivery.Event.Data.PayloadJson, delivery.Event.Data.HeadersJson);
        }
        ValidateSubscriptionGroupProgress(view, subscription, state, SourceHead(view, subscription.Source), budget);
    }
}
