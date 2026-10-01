using System.Security.Cryptography;
using System.Text;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static byte[] QueueKey(string space, QueueLaneRef lane, params object?[] tail)
        => KeySpace.Partition(space, lane.Partition, new object?[] { lane.Queue }.Concat(tail).ToArray());
    private static QueueCounters Counters(IKeyValueView view, QueueLaneRef lane)
        => view.GetRecord<QueueCounters>(QueueKey("queue-counters", lane)) ?? new(0, 0, 0, 0, 0);
    private static long BodyBytes(MessageBody body) => JsonDefaults.Serialize(body).LongLength;
    private bool DispatchPaused(IKeyValueView view) => view.Get(KeyCodec.Encode("system", "dispatch-paused")) is { } value
        ? JsonDefaults.Deserialize<bool>(value) : Store.Identity.DispatchPaused;
    private MutationReceipt Enqueue(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, EnqueueMessage message, DateTimeOffset now)
    {
        JsonData.Identifier(message.MessageId);
        var resource = Resource(tx, partition, message.Queue, ResourceKind.WorkQueue);
        if (resource.Paused) throw Errors.Fail(ErrorCode.DispatchPaused, "This queue is paused.");
        var lane = new QueueLaneRef(partition, message.Queue);
        if (tx.Get(QueueKey("message-meta", lane, message.MessageId)) is not null)
            throw Errors.Fail(ErrorCode.Conflict, "A message ID is already present in this lane.");
        if (message.ExpiresAt <= now || message.ExpiresAt <= message.NotBefore)
            throw Errors.Fail(ErrorCode.Validation, "The message expiry precedes its availability.");
        foreach (var policy in resource.FieldPolicies) Authorization.RequireFieldWrite(principal, resource, policy.Path);
        foreach (var policy in resource.HeaderPolicies) Authorization.RequireFieldWrite(principal, resource with { FieldPolicies = resource.HeaderPolicies }, policy.Path);
        var body = new MessageBody(message.MessageId, JsonData.Validate(message.PayloadJson, Limits),
            JsonData.Validate(message.HeadersJson, Limits), message.OrderingKey, JsonData.Fingerprint(message));
        var counters = Counters(tx, lane);
        var size = BodyBytes(body);
        if (counters.StoredMessages >= resource.QueuePolicy.MaxStoredMessages || counters.StoredBytes + size > resource.QueuePolicy.MaxStoredBytes)
            throw Errors.Fail(ErrorCode.ResourceExhausted, "The queue stored-message quota is exhausted.");
        var scheduled = message.NotBefore > now;
        var readySequence = scheduled ? 0 : checked(counters.NextReadySequence + 1);
        var metadata = new MessageMetadata(message.MessageId, scheduled ? MessageState.Scheduled : MessageState.Ready,
            0, 1, readySequence, message.NotBefore, message.ExpiresAt);
        tx.PutRecord(QueueKey("message-body", lane, message.MessageId), body);
        tx.PutRecord(QueueKey("message-meta", lane, message.MessageId), metadata);
        tx.PutRecord(scheduled ? QueueKey("scheduled", lane, message.NotBefore!.Value, message.MessageId)
            : QueueKey("ready", lane, readySequence, message.MessageId), message.MessageId);
        tx.PutRecord(QueueKey("queue-counters", lane), counters with
        {
            StoredMessages = counters.StoredMessages + 1, StoredBytes = counters.StoredBytes + size,
            NextReadySequence = scheduled ? counters.NextReadySequence : readySequence
        });
        return new("enqueue", message.Queue, message.MessageId, 1);
    }

    private void Sweep(IAtomicTransaction tx, QueueLaneRef lane, QueuePolicy policy, DateTimeOffset now)
    {
        // One bounded lane sweep replaces per-message timers. Time and resulting transitions are replicated.
        foreach (var space in new[] { "scheduled", "lease" })
        {
            var page = tx.Scan(QueueKey(space, lane), 256);
            foreach (var item in page.Records)
            {
                var components = KeyCodec.Decode(item.Key);
                if ((DateTimeOffset)components[^2]! > now) break;
                var id = JsonDefaults.Deserialize<string>(item.Value);
                var metaKey = QueueKey("message-meta", lane, id);
                var metadata = tx.GetRecord<MessageMetadata>(metaKey) ?? throw Errors.Fail(ErrorCode.Corruption, "A queue index points to absent metadata.");
                var body = tx.GetRecord<MessageBody>(QueueKey("message-body", lane, id)) ?? throw Errors.Fail(ErrorCode.Corruption, "A queue message body is absent.");
                var counters = Counters(tx, lane);
                tx.Delete(item.Key);
                if (space == "lease") counters = counters with
                {
                    InFlightMessages = counters.InFlightMessages - 1, InFlightBytes = counters.InFlightBytes - BodyBytes(body)
                };
                MessageMetadata updated;
                if (metadata.ExpiresAt <= now)
                {
                    updated = metadata with { State = MessageState.Expired, StateVersion = metadata.StateVersion + 1, LeaseOwner = null, LeaseUntil = null };
                    counters = counters with { StoredMessages = counters.StoredMessages - 1, StoredBytes = counters.StoredBytes - BodyBytes(body) };
                    tx.Delete(QueueKey("message-body", lane, id));
                }
                else if (metadata.Attempts >= policy.MaxAttempts)
                {
                    updated = metadata with { State = MessageState.DeadLettered, StateVersion = metadata.StateVersion + 1,
                        LeaseOwner = null, LeaseUntil = null, SafeFailureCode = "AttemptsExhausted" };
                    tx.PutRecord(QueueKey("dead-letter", lane, id), id);
                }
                else
                {
                    var sequence = checked(counters.NextReadySequence + 1);
                    counters = counters with { NextReadySequence = sequence };
                    updated = metadata with { State = MessageState.Ready, ReadySequence = sequence, StateVersion = metadata.StateVersion + 1,
                        LeaseOwner = null, LeaseUntil = null, NotBefore = null };
                    tx.PutRecord(QueueKey("ready", lane, sequence, id), id);
                }
                tx.PutRecord(metaKey, updated);
                tx.PutRecord(QueueKey("queue-counters", lane), counters);
            }
        }
    }

    private ReceiveResult Receive(IAtomicTransaction tx, PrincipalRecord principal, ReceiveRequest request, DateTimeOffset now, long position)
    {
        var resource = Resource(tx, request.Lane.Partition, request.Lane.Queue, ResourceKind.WorkQueue);
        var policy = resource.QueuePolicy;
        if (DispatchPaused(tx) || resource.Paused) throw Errors.Fail(ErrorCode.DispatchPaused, "Queue dispatch is paused.");
        if (request.MaxMessages is < 1 or > 100 || request.MaxBytes is < 1 || request.MaxBytes > Limits.MaxBatchBytes
            || request.LeaseSeconds < 1 || request.LeaseSeconds > policy.MaxLeaseSeconds)
            throw Errors.Fail(ErrorCode.Validation, "The receive budget or lease duration is invalid.");
        Sweep(tx, request.Lane, policy, now);
        var deliveries = new List<Delivery>();
        long receivedBytes = 0;
        foreach (var item in tx.Scan(QueueKey("ready", request.Lane), 256).Records)
        {
            if (deliveries.Count == request.MaxMessages) break;
            var id = JsonDefaults.Deserialize<string>(item.Value);
            var metadataKey = QueueKey("message-meta", request.Lane, id);
            var metadata = tx.GetRecord<MessageMetadata>(metadataKey) ?? throw Errors.Fail(ErrorCode.Corruption, "Queue metadata is absent.");
            var body = tx.GetRecord<MessageBody>(QueueKey("message-body", request.Lane, id)) ?? throw Errors.Fail(ErrorCode.Corruption, "Queue body is absent.");
            var counters = Counters(tx, request.Lane);
            var size = BodyBytes(body);
            if (metadata.ExpiresAt <= now)
            {
                tx.Delete(item.Key); tx.Delete(QueueKey("message-body", request.Lane, id));
                tx.PutRecord(metadataKey, metadata with { State = MessageState.Expired, StateVersion = metadata.StateVersion + 1 });
                tx.PutRecord(QueueKey("queue-counters", request.Lane), counters with { StoredMessages = counters.StoredMessages - 1, StoredBytes = counters.StoredBytes - size });
                continue;
            }
            if (receivedBytes + size > request.MaxBytes || counters.InFlightMessages >= policy.MaxInFlightMessages
                || counters.InFlightBytes + size > policy.MaxInFlightBytes) break;
            var deadline = now.AddSeconds(request.LeaseSeconds);
            var version = checked(metadata.LeaseVersion + 1);
            var updated = metadata with { State = MessageState.Leased, Attempts = metadata.Attempts + 1,
                StateVersion = metadata.StateVersion + 1, LeaseVersion = version, LeaseOwner = principal.Id, LeaseUntil = deadline };
            tx.Delete(item.Key);
            tx.PutRecord(metadataKey, updated);
            tx.PutRecord(QueueKey("lease", request.Lane, deadline, id), id);
            tx.PutRecord(QueueKey("queue-counters", request.Lane), counters with { InFlightMessages = counters.InFlightMessages + 1, InFlightBytes = counters.InFlightBytes + size });
            var token = Sign(new DeliveryClaims(request.Lane, id, principal.Id, version, metadata.DeliveryGeneration, Store.Identity.Incarnation));
            deliveries.Add(new(id, Authorization.Project(principal, resource.FieldPolicies, body.PayloadJson, out _),
                Authorization.Project(principal, resource.HeaderPolicies, body.HeadersJson, out _), token, version, deadline, updated.Attempts, updated.DeliveryGeneration));
            receivedBytes += size;
        }
        return new(request.RequestId, deliveries.ToArray(), Token(request.Lane.Partition, position));
    }

    public string Sign<T>(T claims)
    {
        var bytes = JsonDefaults.Serialize(claims);
        return Base64Url(bytes) + "." + Base64Url(HMACSHA256.HashData(Store.Identity.SigningKey, bytes));
    }
    public T Verify<T>(string token)
    {
        try
        {
            if (token.Length > 8_192) throw new FormatException();
            var parts = token.Split('.');
            if (parts.Length != 2) throw new FormatException();
            var bytes = Convert.FromBase64String(Pad(parts[0]));
            var mac = Convert.FromBase64String(Pad(parts[1]));
            if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(Store.Identity.SigningKey, bytes), mac)) throw new FormatException();
            return JsonDefaults.Deserialize<T>(bytes);
        }
        catch (Exception e) when (e is FormatException or System.Text.Json.JsonException)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, "The signed token is invalid."); }
    }
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Pad(string value) { var padded = value.Replace('-', '+').Replace('_', '/'); return padded.PadRight((padded.Length + 3) / 4 * 4, '='); }
    private (DeliveryClaims Claims, MessageMetadata Metadata, MessageBody Body) Lease(IKeyValueView view, PrincipalRecord principal,
        QueueLaneRef lane, string token, DateTimeOffset now)
    {
        var claims = Verify<DeliveryClaims>(token);
        if (claims.Incarnation != Store.Identity.Incarnation || claims.Lane != lane || claims.PrincipalId != principal.Id)
            throw Errors.Fail(ErrorCode.TokenInvalidated, "The delivery token scope is invalid.");
        var metadata = view.GetRecord<MessageMetadata>(QueueKey("message-meta", lane, claims.MessageId))
            ?? throw Errors.Fail(ErrorCode.NotFound, "The message is unavailable.");
        if (metadata.State != MessageState.Leased || metadata.LeaseOwner != principal.Id || metadata.LeaseVersion != claims.LeaseVersion
            || metadata.DeliveryGeneration != claims.DeliveryGeneration)
            throw Errors.Fail(ErrorCode.StaleLease, "The delivery lease is stale.");
        if (metadata.LeaseUntil <= now) throw Errors.Fail(ErrorCode.LeaseExpired, "The delivery lease has expired.");
        var body = view.GetRecord<MessageBody>(QueueKey("message-body", lane, claims.MessageId))
            ?? throw Errors.Fail(ErrorCode.Corruption, "A leased message body is absent.");
        return (claims, metadata, body);
    }
    private CommitReceipt CompleteDelivery(IAtomicTransaction tx, PrincipalRecord principal, DeliveryCommand command, DateTimeOffset now, long position)
    {
        var resource = Resource(tx, command.Lane.Partition, command.Lane.Queue, ResourceKind.WorkQueue);
        var (_, metadata, body) = Lease(tx, principal, command.Lane, command.Token, now);
        var counters = Counters(tx, command.Lane);
        tx.Delete(QueueKey("lease", command.Lane, metadata.LeaseUntil!.Value, metadata.Id));
        MessageMetadata updated;
        if (command.Action == DeliveryAction.Renew)
        {
            var seconds = command.LeaseSeconds ?? 30;
            if (seconds < 1 || seconds > resource.QueuePolicy.MaxLeaseSeconds) throw Errors.Fail(ErrorCode.Validation, "The renewal duration is invalid.");
            var deadline = now.AddSeconds(seconds);
            updated = metadata with { LeaseUntil = deadline, StateVersion = metadata.StateVersion + 1 };
            tx.PutRecord(QueueKey("lease", command.Lane, deadline, metadata.Id), metadata.Id);
        }
        else
        {
            counters = counters with { InFlightMessages = counters.InFlightMessages - 1, InFlightBytes = counters.InFlightBytes - BodyBytes(body) };
            if (command.Action == DeliveryAction.Ack)
            {
                updated = metadata with { State = MessageState.Acked, StateVersion = metadata.StateVersion + 1, LeaseOwner = null, LeaseUntil = null };
                counters = counters with { StoredMessages = counters.StoredMessages - 1, StoredBytes = counters.StoredBytes - BodyBytes(body) };
                tx.Delete(QueueKey("message-body", command.Lane, metadata.Id));
            }
            else if (metadata.Attempts >= resource.QueuePolicy.MaxAttempts)
            {
                updated = metadata with { State = MessageState.DeadLettered, StateVersion = metadata.StateVersion + 1,
                    LeaseOwner = null, LeaseUntil = null, SafeFailureCode = "AttemptsExhausted" };
                tx.PutRecord(QueueKey("dead-letter", command.Lane, metadata.Id), metadata.Id);
            }
            else
            {
                var delay = Math.Min(resource.QueuePolicy.RetryMaxMilliseconds,
                    resource.QueuePolicy.RetryBaseMilliseconds * Math.Pow(2, Math.Min(metadata.Attempts - 1, 20)));
                var available = now.AddMilliseconds(delay);
                updated = metadata with { State = MessageState.Scheduled, StateVersion = metadata.StateVersion + 1,
                    LeaseOwner = null, LeaseUntil = null, NotBefore = available, SafeFailureCode = "RetryRequested" };
                tx.PutRecord(QueueKey("scheduled", command.Lane, available, metadata.Id), metadata.Id);
            }
        }
        tx.PutRecord(QueueKey("message-meta", command.Lane, metadata.Id), updated);
        tx.PutRecord(QueueKey("queue-counters", command.Lane), counters);
        return new(command.CommandId, Token(command.Lane.Partition, position), [new(command.Action.ToString(), command.Lane.Queue, metadata.Id, updated.StateVersion)], Durability);
    }
    private CommitReceipt CompleteProcessing(IAtomicTransaction tx, PrincipalRecord principal, ProcessingRequest request, DateTimeOffset now, long position)
    {
        JsonData.Identifier(request.HandlerScope);
        if (request.ExecutionGeneration < 1) throw Errors.Fail(ErrorCode.Validation, "The handler generation is invalid.");
        var claims = Verify<DeliveryClaims>(request.Token);
        if (claims.Incarnation != Store.Identity.Incarnation || claims.Lane != request.Lane || claims.PrincipalId != principal.Id)
            throw Errors.Fail(ErrorCode.TokenInvalidated, "The delivery token scope is invalid.");
        var inboxKey = QueueKey("inbox", request.Lane, claims.MessageId, request.HandlerScope, request.ExecutionGeneration);
        var fingerprint = JsonData.Fingerprint(new { request.Lane, claims.MessageId, request.HandlerScope, request.ExecutionGeneration, request.Effects, principal.Id });
        if (tx.GetRecord<InboxRecord>(inboxKey) is { } completed)
        {
            if (completed.Fingerprint != fingerprint) throw Errors.Fail(ErrorCode.Conflict, "The inbox input was already completed with different effects.");
            return completed.Receipt;
        }
        Lease(tx, principal, request.Lane, request.Token, now);
        var ack = CompleteDelivery(tx, principal, new(request.CommandId, request.Lane, request.Token, DeliveryAction.Ack), now, position);
        var effects = ApplyMutations(tx, principal, request.Lane.Partition, request.Effects, now);
        var receipt = ack with { Mutations = effects.Concat(ack.Mutations).ToArray() };
        tx.PutRecord(inboxKey, new InboxRecord(fingerprint, receipt));
        return receipt;
    }
    private sealed record InboxRecord(string Fingerprint, CommitReceipt Receipt);
    public MessageInspection? InspectMessage(string principalId, QueueLaneRef lane, string id) => Store.Read(view =>
    {
        var principal = Principal(view, principalId, DateTimeOffset.UtcNow);
        Authorization.Require(principal, lane.Partition, lane.Queue, Capability.QueueInspect);
        var resource = Resource(view, lane.Partition, lane.Queue, ResourceKind.WorkQueue);
        var metadata = view.GetRecord<MessageMetadata>(QueueKey("message-meta", lane, id));
        if (metadata is null) return null;
        if (metadata.State == MessageState.DeadLettered) Authorization.Require(principal, lane.Partition, lane.Queue, Capability.DeadLettersRead);
        var body = view.GetRecord<MessageBody>(QueueKey("message-body", lane, id));
        return new MessageInspection(metadata, body is null ? null : Authorization.Project(principal, resource.FieldPolicies, body.PayloadJson, out _),
            body is null ? null : Authorization.Project(principal, resource.HeaderPolicies, body.HeadersJson, out _));
    });
}
