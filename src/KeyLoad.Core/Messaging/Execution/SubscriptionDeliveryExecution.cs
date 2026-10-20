using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Owns the SubscriptionDeliveryExecution operation contract.</summary>
public static class SubscriptionDeliveryExecution
{
    internal static ReceiveSubscriptionResult ReceiveSubscription(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, ReceiveSubscriptionRequest request, DateTimeOffset now, long position)
    {
        var subscription = request.Subscription;
        var state = MessagingExecutionSubscriptionGroups.Group(tx, subscription);
        var policy = state.Definition.Policy;
        var resource = engine.SourceResource(tx, subscription.Source);
        var head = engine.SourceHead(tx, subscription.Source);
        var dataPrincipal = engine.Principal(tx, state.Definition.DataPrincipalId, now);
        if (request.MaxEvents < MessagingExecutionSubscriptionGroups.SubscriptionGroupsMinimumPositiveCount || request.MaxEvents > engine.MessagingExecution.MaximumReceiveEvents || request.MaxBytes < MessagingExecutionSubscriptionGroups.SubscriptionGroupsMinimumPositiveCount || request.MaxBytes > engine.Limits.MaxBatchBytes || request.LeaseSeconds < MessagingExecutionSubscriptionGroups.SubscriptionGroupsMinimumPositiveCount || request.LeaseSeconds > policy.MaxLeaseSeconds)
        {
            throw Errors.Fail(ErrorCode.Validation, MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionReceiveBudgetOrLeaseIsInvalidDetail);
        }

        if (state.Paused || resource.Paused || engine.DispatchPaused(tx))
        {
            throw Errors.Fail(ErrorCode.DispatchPaused, MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionDeliveryIsPausedDetail);
        }

        MessagingValidationQueueLifecycleSubscriptionDeliveryAuthority.ValidateSubscriptionGroupLowerBounds(state);
        if (state.Checkpoint < head.FirstAvailablePosition - MessagingExecutionSubscriptionGroups.SubscriptionGroupsAdjacentElementOffset)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionHistoryWasRetainedAwayDetail);
        }

        var windowBudget = new SubscriptionWindowAuthorityReadBudget(engine.OperationLimitsOptions);
        engine.ValidateSubscriptionGroupProgress(tx, subscription, state, head, windowBudget);
        var result = new List<SubscriptionDelivery>();
        state = engine.FillSubscriptionWindow(tx, principal, request, now, state, policy, resource, head, dataPrincipal, result, windowBudget);
        state = MessagingExecutionSubscriptionGroups.AdvanceCheckpoint(tx, subscription, state, windowBudget);
        tx.PutRecord(MessagingSerialization.GroupKey(MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionKeySpace, subscription), state);
        return new(request.RequestId, result.ToImmutableArray(), MessagingExecutionSubscriptionGroups.ValidatedGroupInfo(subscription, state, head), engine.Token(tx, subscription.Source.Partition, position));
    }

    internal static bool TryLeaseSubscriptionEvent(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, ReceiveSubscriptionRequest request, DateTimeOffset now, GroupState state, ResourceDefinition resource, PrincipalRecord dataPrincipal, List<SubscriptionDelivery> result, SourceEventRecord record, byte[] key, GroupDelivery delivery, long sequence, ref long bytes, out GroupState issuedState)
    {
        issuedState = state;
        var projected = engine.ProjectEvent(principal, resource, engine.ProjectEvent(dataPrincipal, resource, record));
        var size = JsonDefaults.Serialize(projected).LongLength;
        if (size > request.MaxBytes && result.Count == MessagingExecutionSubscriptionGroups.SubscriptionGroupsNoRetainedBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, MessagingExecutionSubscriptionGroups.SubscriptionGroupsNextSubscriptionEventExceedsTheReceiveByteBudgetDetail);
        }

        if (bytes + size > request.MaxBytes)
        {
            return false;
        }

        if (delivery.LeaseVersion == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, MessagingValidationQueueLifecycleSubscriptionDeliveryAuthority.SubscriptionGroupsDeliveryLeaseVersionExhaustedDetail);
        }

        bytes += size;
        delivery = delivery with
        {
            State = GroupDeliveryState.Leased,
            Attempts = delivery.Attempts + MessagingExecutionSubscriptionGroups.SubscriptionGroupsAdjacentElementOffset,
            LeaseVersion = checked(delivery.LeaseVersion + MessagingExecutionSubscriptionGroups.SubscriptionGroupsVersionOne),
            LeaseUntil = now.AddSeconds(request.LeaseSeconds),
            PrincipalId = principal.Id,
            AvailableAt = null
        };
        tx.PutRecord(key, delivery);
        issuedState = state with
        {
            IssuedPosition = long.Max(state.IssuedPosition, sequence)
        };
        var subscription = request.Subscription;
        var token = engine.Sign(new GroupClaims(MessagingExecutionSubscriptionGroups.SubscriptionDeliveryTokenPurpose, engine.Store.Identity.Incarnation, subscription, state.Generation, state.OwnershipEpoch, sequence, delivery.LeaseVersion, principal.Id, principal.PolicyEpoch, dataPrincipal.PolicyEpoch));
        result.Add(new(projected, token, delivery.LeaseVersion, delivery.LeaseUntil.Value, delivery.Attempts));
        return true;
    }

    /// <summary>Reads an authorized subscription and its current checkpoint state.</summary>
    /// <param name = "principalId">Persisted principal identifier.</param>
    /// <param name = "subscription">Subscription identity.</param>
    /// <returns>The persisted subscription state.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static SubscriptionInfo GetSubscription(this DatabaseEngine engine, string principalId, SubscriptionRef subscription)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.Store.Read(view => engine.GetSubscriptionRead(view, principalId, subscription));
    }

    internal static SubscriptionInfo GetSubscriptionRead(this DatabaseEngine engine, IKeyValueView view, string principalId, SubscriptionRef subscription)
    {
        var now = engine.Clock.GetUtcNow();
        var principal = engine.Principal(view, principalId, now);
        try
        {
            engine.AuthorizeSubscription(view, principal, subscription, Capability.SubscriptionsManage, now, authorizedRead: true);
        }
        catch (KeyLoadException exception) when (exception.Code == ErrorCode.PermissionDenied)
        {
            engine.AuthorizeSubscription(view, principal, subscription, Capability.SubscriptionsConsume, now, authorizedRead: true);
        }

        return engine.GroupInfo(view, subscription, MessagingExecutionSubscriptionGroups.Group(view, subscription));
    }
}
