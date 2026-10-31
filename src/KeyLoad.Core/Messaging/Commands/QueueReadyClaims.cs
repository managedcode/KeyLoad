using System.Collections.Immutable;
using KeyLoad.Core.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes QueueReadyClaims operations against the caller's database context.</summary>
public static class QueueReadyClaims
{
    internal const int QueueReadyClaimsVersionOne = 1;
    internal const string QueueClaimVersionExhausted = "The queue claim counters are exhausted.";
    internal const int QueueReadyClaimsAdjacentElementOffset = 1;
    internal const string MissingReadyMetadata = "Queue metadata is absent.";
    internal const string MissingReadyBody = "Queue body is absent.";
    internal sealed class ReadyClaimState
    {
        public List<Delivery> Deliveries { get; } = [];
        public List<ReadyClaimInput> TransitionInputs { get; } = [];
        public QueueCounters? Counters { get; set; }
        public long ReceivedBytes { get; set; }
        public bool CountersChanged { get; set; }
    }

    internal static ReceiveResult ClaimReadyMessages(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, ReceiveRequest request, ResourceDefinition resource, DateTimeOffset now, long position)
    {
        var state = new ReadyClaimState();
        tx.VisitRange(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.ReadyQueueSpace, request.Lane), engine.MessagingExecution.QueueScanPageSize, (key, value) => engine.CaptureReadyItem(tx, principal, request, resource, now, key, value, state));
        ApplyReadyInputs(tx, state.TransitionInputs);
        if (state.CountersChanged)
        {
            tx.PutRecord(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.QueueCountersSpace, request.Lane), state.Counters!);
        }

        return new(request.RequestId, state.Deliveries.ToImmutableArray(), engine.Token(tx, request.Lane.Partition, position));
    }

    internal static bool CaptureReadyItem(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, ReceiveRequest request, ResourceDefinition resource, DateTimeOffset now, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, ReadyClaimState state)
    {
        var id = OrleansSerialization.Deserialize<string>(value);
        var metadataKey = MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.MessageMetadataSpace, request.Lane, id);
        var metadata = tx.GetRecord<MessageMetadata>(metadataKey) ?? throw Errors.Fail(ErrorCode.Corruption, MissingReadyMetadata);
        var bodyKey = MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.MessageBodySpace, request.Lane, id);
        var body = (StoredMessageBody.Read(tx, bodyKey)).Value ?? throw Errors.Fail(ErrorCode.Corruption, MissingReadyBody);
        MessagingValidation.RequireQueueReadyAuthority(request.Lane, id, key, metadata, body.Body);
        state.Counters ??= MessagingExecutionMessaging.Counters(tx, request.Lane);
        if (metadata.ExpiresAt <= now)
        {
            AddExpiredReadyInput(tx, request.Lane, resource.QueuePolicy, body.Body, key, bodyKey, metadataKey, metadata, body.Bytes, state);
            return state.TransitionInputs.Count < engine.MessagingExecution.QueueScanPageSize;
        }

        if (!MessagingValidationQueueOrdering.RequireStrictQueueHead(tx, request.Lane, resource.QueuePolicy, body.Body, metadata))
        {
            return state.TransitionInputs.Count < engine.MessagingExecution.QueueScanPageSize;
        }

        if (state.Counters.InFlightMessages >= resource.QueuePolicy.MaxInFlightMessages || state.Counters.InFlightBytes + body.Bytes > resource.QueuePolicy.MaxInFlightBytes)
        {
            return false;
        }

        engine.AddLeaseReadyInput(principal, request, resource, now, key, metadataKey, metadata, body, state);
        return state.Deliveries.Count < request.MaxMessages && state.TransitionInputs.Count < engine.MessagingExecution.QueueScanPageSize;
    }

    internal static void AddExpiredReadyInput(IKeyValueView view, QueueLaneRef lane, QueuePolicy policy, MessageBody body, ReadOnlySpan<byte> key, byte[] bodyKey, byte[] metadataKey, MessageMetadata metadata, long bodyBytes, ReadyClaimState state)
    {
        if (metadata.StateVersion == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, QueueClaimVersionExhausted);
        }

        var orderKey = policy.OrderingProfile == QueueOrderingProfile.StrictPerKey ? MessagingValidationQueueOrdering.RequireQueueOrderReference(view, lane, MessagingCommandsQueueOrdering.RequireQueueOrderingKey(body), metadata) : null;
        var expired = metadata with
        {
            State = MessageState.Expired,
            StateVersion = metadata.StateVersion + QueueReadyClaimsVersionOne,
            ActiveOrderSequence = QueueOrderProtocol.Initial
        };
        state.TransitionInputs.Add(new(ReadyKey: key.ToArray(), MetadataKey: metadataKey, Metadata: expired, TransitionKey: bodyKey, DeletesBody: true, StrictOrderKey: orderKey));
        state.Counters = QueueLifecycleAccounting.ReleaseQueueStored(state.Counters!, bodyBytes);
        state.CountersChanged = true;
    }

    internal static void AddLeaseReadyInput(this DatabaseEngine engine, PrincipalRecord principal, ReceiveRequest request, ResourceDefinition resource, DateTimeOffset now, ReadOnlySpan<byte> key, byte[] metadataKey, MessageMetadata metadata, StoredMessageBody body, ReadyClaimState state)
    {
        RequireQueueClaimVersionSpace(metadata);
        var deadline = now.AddSeconds(request.LeaseSeconds);
        var version = checked(metadata.LeaseVersion + QueueReadyClaimsVersionOne);
        var updated = metadata with
        {
            State = MessageState.Leased,
            Attempts = metadata.Attempts + QueueReadyClaimsAdjacentElementOffset,
            StateVersion = metadata.StateVersion + QueueReadyClaimsVersionOne,
            LeaseVersion = version,
            LeaseOwner = principal.Id,
            LeaseUntil = deadline
        };
        var leaseKey = MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.LeasedQueueSpace, request.Lane, deadline, metadata.Id);
        state.TransitionInputs.Add(new(ReadyKey: key.ToArray(), MetadataKey: metadataKey, Metadata: updated, TransitionKey: leaseKey, DeletesBody: false));
        var counters = state.Counters!;
        state.Counters = counters with
        {
            InFlightMessages = counters.InFlightMessages + QueueReadyClaimsAdjacentElementOffset,
            InFlightBytes = counters.InFlightBytes + body.Bytes
        };
        state.CountersChanged = true;
        var token = engine.Sign(new DeliveryClaims(request.Lane, metadata.Id, principal.Id, version, metadata.DeliveryGeneration, engine.Store.Identity.Incarnation));
        state.Deliveries.Add(new(metadata.Id, engine.Authorization.Project(principal, resource.FieldPolicies, body.Body.PayloadJson, out _), engine.Authorization.Project(principal, resource.HeaderPolicies, body.Body.HeadersJson, out _), token, version, deadline, updated.Attempts, updated.DeliveryGeneration));
        state.ReceivedBytes += body.Bytes;
    }

    internal static void RequireQueueClaimVersionSpace(MessageMetadata metadata)
    {
        if (metadata.Attempts == int.MaxValue || metadata.StateVersion == long.MaxValue || metadata.LeaseVersion == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, QueueClaimVersionExhausted);
        }
    }

    internal static void ApplyReadyInputs(IAtomicTransaction tx, List<ReadyClaimInput> inputs)
    {
        foreach (var input in inputs)
        {
            tx.Delete(input.ReadyKey);
            if (input.DeletesBody)
            {
                tx.Delete(input.TransitionKey);
                if (input.StrictOrderKey is not null)
                {
                    tx.Delete(input.StrictOrderKey);
                }

                tx.PutRecord(input.MetadataKey, input.Metadata);
            }
            else
            {
                tx.PutRecord(input.MetadataKey, input.Metadata);
                tx.PutRecord(input.TransitionKey, input.Metadata.Id);
            }
        }
    }
}
