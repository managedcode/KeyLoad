using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string MissingDurableOutcomeMessage = "The applied command has no durable outcome.";
    private const string EarlierOutcomeIncarnationMessage = "The command belongs to an earlier incarnation.";
    private const string ChangedOutcomePrincipalPolicyMessage = "The principal policy changed since this command was evaluated.";
    private const string EarlierSubscriptionGenerationMessage = "The cached receive belongs to an earlier subscription generation.";

    /// <summary>Reads a stored operation outcome scoped to its verified principal.</summary>
    /// <param name="principal">Verified principal identifier.</param>
    /// <param name="id">Stable operation identifier.</param>
    /// <returns>The stored result, or null when no outcome is present.</returns>
    public OperationResult? Outcome(string principal, Guid id) => Store.Read(v => v.GetRecord<StoredOutcome>(KeySpace.Outcome(principal, id))?.Result);
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
            var principal = Principal(view, operation.PrincipalId, Clock.GetUtcNow());
            AuthorizeOperation(view, principal, operation);
            var outcome = view.GetRecord<StoredOutcome>(KeySpace.Outcome(operation.PrincipalId, operation.Id))
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, MissingDurableOutcomeMessage);
            if (outcome.Incarnation != Store.Identity.Incarnation)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, EarlierOutcomeIncarnationMessage);
            }

            if (outcome.Fingerprint != CommandFingerprint(operation))
            {
                throw Errors.Fail(ErrorCode.Conflict, CommandContentConflictMessage);
            }

            ValidateCachedResult(view, principal, operation with { EvaluatedAt = Clock.GetUtcNow() }, outcome);
            return outcome.Result;
        }
        catch (KeyLoadException exception) { return new OperationResult(null, exception.Code, exception.Message); }
    });
    private void ValidateCachedResult(IKeyValueView view, PrincipalRecord principal, ReplicatedOperation operation, StoredOutcome previous)
    {
        if (previous.PolicyEpoch != principal.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, ChangedOutcomePrincipalPolicyMessage);
        }

        if (BlobStorageOperations.Handles(operation.Kind))
        {
            new BlobStorageOperations(this).ValidateOutcomeAuthority(view, operation, previous.Result, previous.BlobAuthority);
        }
        ValidateCompositionOutcome(view, principal, operation, previous);
        ReauthorizeExtendedOutcome(view, principal, operation, previous);

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
        StoredOutcome previous)
    {
        if (previous.Result.Error is not null)
        {
            return;
        }
        if (operation.Kind == OperationKind.Batch)
        {
            var command = Payload<CommandRequest>(operation);
            ReauthorizeExtendedEffects(view, principal, command.Partition, command.Mutations);
        }
        else if (operation.Kind == OperationKind.Processing)
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
            if (effect is global::KeyLoad.ApplyVectorProjection or CreateQueueTransfer or AcceptQueueTransfer or CompleteQueueTransfer
                or ConfigureRecurringSchedule or EmitRecurringOccurrences or CancelRecurringSchedule or CompareExchangeSaga or ExpireSaga)
            {
                ReauthorizeEffect(view, principal, partition, effect);
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

        foreach (var delivery in cached.Deliveries)
        {
            SubscriptionLease(view, principal, subscription, delivery.Token, operation.EvaluatedAt);
        }
    }
}
