using System.Collections.Immutable;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string MissingReadyMetadata = "Queue metadata is absent.";
    private const string MissingReadyBody = "Queue body is absent.";

    private sealed class ReadyClaimState
    {
        public List<Delivery> Deliveries { get; } = [];
        public List<ReadyClaimInput> TransitionInputs { get; } = [];
        public QueueCounters? Counters { get; set; }
        public long ReceivedBytes { get; set; }
        public bool CountersChanged { get; set; }
    }

    private ReceiveResult ClaimReadyMessages(IAtomicTransaction tx, PrincipalRecord principal,
        ReceiveRequest request, ResourceDefinition resource, DateTimeOffset now, long position)
    {
        var state = new ReadyClaimState();
        tx.VisitRange(QueueKey(ReadyQueueSpace, request.Lane), QueueScanPageSize,
            (key, value) => CaptureReadyItem(tx, principal, request, resource, now, key, value, state));
        ApplyReadyInputs(tx, state.TransitionInputs);
        if (state.CountersChanged)
        {
            tx.PutRecord(QueueKey(QueueCountersSpace, request.Lane), state.Counters!);
        }
        return new(request.RequestId, state.Deliveries.ToImmutableArray(), Token(request.Lane.Partition, position));
    }

    private bool CaptureReadyItem(IAtomicTransaction tx, PrincipalRecord principal, ReceiveRequest request,
        ResourceDefinition resource, DateTimeOffset now, ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> value, ReadyClaimState state)
    {
        var id = NativeSerialization.Deserialize<string>(value);
        var metadataKey = QueueKey(MessageMetadataSpace, request.Lane, id);
        var metadata = tx.GetRecord<MessageMetadata>(metadataKey)
            ?? throw Errors.Fail(ErrorCode.Corruption, MissingReadyMetadata);
        var bodyKey = QueueKey(MessageBodySpace, request.Lane, id);
        var body = StoredMessageBody.Read(tx, bodyKey)
            ?? throw Errors.Fail(ErrorCode.Corruption, MissingReadyBody);
        state.Counters ??= Counters(tx, request.Lane);
        if (metadata.ExpiresAt <= now)
        {
            AddExpiredReadyInput(key, bodyKey, metadataKey, metadata, body.Bytes, state);
            return state.TransitionInputs.Count < QueueScanPageSize;
        }

        if (state.ReceivedBytes + body.Bytes > request.MaxBytes
            || state.Counters.InFlightMessages >= resource.QueuePolicy.MaxInFlightMessages
            || state.Counters.InFlightBytes + body.Bytes > resource.QueuePolicy.MaxInFlightBytes)
        {
            return false;
        }

        AddLeaseReadyInput(principal, request, resource, now, key, metadataKey, metadata, body, state);
        return state.Deliveries.Count < request.MaxMessages
            && state.TransitionInputs.Count < QueueScanPageSize;
    }

    private static void AddExpiredReadyInput(ReadOnlySpan<byte> key, byte[] bodyKey, byte[] metadataKey,
        MessageMetadata metadata, long bodyBytes, ReadyClaimState state)
    {
        var expired = metadata with { State = MessageState.Expired, StateVersion = metadata.StateVersion + 1 };
        state.TransitionInputs.Add(new(ReadyKey: key.ToArray(), MetadataKey: metadataKey, Metadata: expired,
            TransitionKey: bodyKey, DeletesBody: true));
        var counters = state.Counters!;
        state.Counters = counters with
        {
            StoredMessages = counters.StoredMessages - 1,
            StoredBytes = counters.StoredBytes - bodyBytes
        };
        state.CountersChanged = true;
    }

    private void AddLeaseReadyInput(PrincipalRecord principal, ReceiveRequest request,
        ResourceDefinition resource, DateTimeOffset now, ReadOnlySpan<byte> key, byte[] metadataKey,
        MessageMetadata metadata, StoredMessageBody body, ReadyClaimState state)
    {
        var deadline = now.AddSeconds(request.LeaseSeconds);
        var version = checked(metadata.LeaseVersion + 1);
        var updated = metadata with
        {
            State = MessageState.Leased,
            Attempts = metadata.Attempts + 1,
            StateVersion = metadata.StateVersion + 1,
            LeaseVersion = version,
            LeaseOwner = principal.Id,
            LeaseUntil = deadline
        };
        var leaseKey = QueueKey(LeasedQueueSpace, request.Lane, deadline, metadata.Id);
        state.TransitionInputs.Add(new(ReadyKey: key.ToArray(), MetadataKey: metadataKey, Metadata: updated,
            TransitionKey: leaseKey, DeletesBody: false));
        var counters = state.Counters!;
        state.Counters = counters with
        {
            InFlightMessages = counters.InFlightMessages + 1,
            InFlightBytes = counters.InFlightBytes + body.Bytes
        };
        state.CountersChanged = true;
        var token = Sign(new DeliveryClaims(request.Lane, metadata.Id, principal.Id, version,
            metadata.DeliveryGeneration, Store.Identity.Incarnation));
        state.Deliveries.Add(new(metadata.Id,
            Authorization.Project(principal, resource.FieldPolicies, body.Body.PayloadJson, out _),
            Authorization.Project(principal, resource.HeaderPolicies, body.Body.HeadersJson, out _),
            token, version, deadline, updated.Attempts, updated.DeliveryGeneration));
        state.ReceivedBytes += body.Bytes;
    }

    private static void ApplyReadyInputs(IAtomicTransaction tx, List<ReadyClaimInput> inputs)
    {
        foreach (var input in inputs)
        {
            tx.Delete(input.ReadyKey);
            if (input.DeletesBody)
            {
                tx.Delete(input.TransitionKey);
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
