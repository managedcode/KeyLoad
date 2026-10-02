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
        public QueueCounters? Counters { get; set; }
        public long ReceivedBytes { get; set; }
        public bool CountersChanged { get; set; }
    }

    private ReceiveResult ClaimReadyMessages(IAtomicTransaction tx, PrincipalRecord principal,
        ReceiveRequest request, ResourceDefinition resource, DateTimeOffset now, long position)
    {
        var state = new ReadyClaimState();
        foreach (var item in tx.Scan(QueueKey(ReadyQueueSpace, request.Lane), QueueScanPageSize).Records)
        {
            if (state.Deliveries.Count == request.MaxMessages
                || !ClaimReadyItem(tx, principal, request, resource, now, item, state))
            {
                break;
            }
        }
        if (state.CountersChanged)
        {
            tx.PutRecord(QueueKey(QueueCountersSpace, request.Lane), state.Counters!);
        }
        return new(request.RequestId, state.Deliveries.ToImmutableArray(), Token(request.Lane.Partition, position));
    }

    private bool ClaimReadyItem(IAtomicTransaction tx, PrincipalRecord principal, ReceiveRequest request,
        ResourceDefinition resource, DateTimeOffset now, KeyValueRecord item, ReadyClaimState state)
    {
        var id = JsonDefaults.Deserialize<string>(item.Value.Span);
        var metadataKey = QueueKey(MessageMetadataSpace, request.Lane, id);
        var metadata = tx.GetRecord<MessageMetadata>(metadataKey)
            ?? throw Errors.Fail(ErrorCode.Corruption, MissingReadyMetadata);
        var body = StoredMessageBody.Read(tx, QueueKey(MessageBodySpace, request.Lane, id))
            ?? throw Errors.Fail(ErrorCode.Corruption, MissingReadyBody);
        state.Counters ??= Counters(tx, request.Lane);
        if (metadata.ExpiresAt <= now)
        {
            ExpireReadyItem(tx, request.Lane, item, metadataKey, metadata, body.Bytes, state);
            return true;
        }
        if (state.ReceivedBytes + body.Bytes > request.MaxBytes
            || state.Counters.InFlightMessages >= resource.QueuePolicy.MaxInFlightMessages
            || state.Counters.InFlightBytes + body.Bytes > resource.QueuePolicy.MaxInFlightBytes)
        {
            return false;
        }
        LeaseReadyItem(tx, principal, request, resource, now, item, metadataKey, metadata, body, state);
        return true;
    }

    private static void ExpireReadyItem(IAtomicTransaction tx, QueueLaneRef lane, KeyValueRecord item,
        byte[] metadataKey, MessageMetadata metadata, long bodyBytes, ReadyClaimState state)
    {
        tx.Delete(item.Key.ToArray());
        tx.Delete(QueueKey(MessageBodySpace, lane, metadata.Id));
        tx.PutRecord(metadataKey, metadata with { State = MessageState.Expired, StateVersion = metadata.StateVersion + 1 });
        var counters = state.Counters!;
        state.Counters = counters with
        {
            StoredMessages = counters.StoredMessages - 1,
            StoredBytes = counters.StoredBytes - bodyBytes
        };
        state.CountersChanged = true;
    }

    private void LeaseReadyItem(IAtomicTransaction tx, PrincipalRecord principal, ReceiveRequest request,
        ResourceDefinition resource, DateTimeOffset now, KeyValueRecord item, byte[] metadataKey,
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
        tx.Delete(item.Key.ToArray());
        tx.PutRecord(metadataKey, updated);
        tx.PutRecord(QueueKey(LeasedQueueSpace, request.Lane, deadline, metadata.Id), metadata.Id);
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
}
