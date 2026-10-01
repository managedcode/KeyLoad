using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine(IAtomicStore store, IAuthorizationPolicy authorization, DatabaseLimits? limits = null)
{
    public IAtomicStore Store { get; } = store;
    public IAuthorizationPolicy Authorization { get; } = authorization;
    public DatabaseLimits Limits { get; } = limits ?? new();
    public DurabilityProfile Durability { get; set; } = store.Identity.Durability;
    public long LastApplied => Store.Read(view => view.Get(KeySpace.Applied) is { } bytes ? JsonDefaults.Deserialize<long>(bytes) : 0);

    public void Bootstrap(PrincipalRecord administrator, ApiKeyRecord apiKey)
    {
        if (!administrator.ClusterAdministrator || administrator.Id != apiKey.PrincipalId)
            throw Errors.Fail(ErrorCode.Validation, "Bootstrap requires a cluster administrator.");
        Store.Commit((tx, _) =>
        {
            if (tx.Get(KeySpace.Principal(administrator.Id)) is null)
            {
                tx.PutRecord(KeySpace.Principal(administrator.Id), administrator);
                tx.PutRecord(KeySpace.ApiKey(apiKey.Id), apiKey);
            }
            return true;
        });
    }
    public PrincipalRecord Principal(IKeyValueView view, string id, DateTimeOffset now)
    {
        var principal = view.GetRecord<PrincipalRecord>(KeySpace.Principal(id));
        if (principal is null || principal.Revoked || principal.ExpiresAt <= now)
            throw Errors.Fail(ErrorCode.Unauthenticated, "The credential is unavailable or expired.");
        return principal;
    }
    public string Authenticate(string secret, DateTimeOffset now)
    {
        var separator = secret.IndexOf('.');
        if (secret.Length is < 20 or > 256 || separator <= 0)
            throw Errors.Fail(ErrorCode.Unauthenticated, "An API key is required.");
        return Store.Read(view =>
        {
            var key = view.GetRecord<ApiKeyRecord>(KeySpace.ApiKey(secret[..separator]));
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
            byte[] verifier;
            try { verifier = key is null ? new byte[32] : Convert.FromHexString(key.Verifier); }
            catch (FormatException) { throw Errors.Fail(ErrorCode.Corruption, "A credential verifier is invalid."); }
            if (key is null || !CryptographicOperations.FixedTimeEquals(hash, verifier) || key.Revoked || key.ExpiresAt <= now)
                throw Errors.Fail(ErrorCode.Unauthenticated, "The credential is unavailable or expired.");
            return Principal(view, key.PrincipalId, now).Id;
        });
    }
    public static ApiKeyRecord Credential(string id, string principal, string secret, DateTimeOffset? expiresAt = null)
        => new(id, principal, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))), expiresAt);

    public ResourceDefinition Resource(IKeyValueView view, PartitionRef partition, string name, ResourceKind? kind = null)
    {
        var resource = view.GetRecord<ResourceDefinition>(KeySpace.Resource(partition.TenantId, partition.DatabaseId, name))
            ?? throw Errors.Fail(ErrorCode.NotFound, "The resource is not configured.");
        if (resource.TransactionDomainId != partition.TransactionDomainId)
            throw Errors.Fail(ErrorCode.Conflict, "The resource belongs to a different transaction domain.");
        if (kind is { } required && resource.Kind != required)
            throw Errors.Fail(ErrorCode.Validation, "The resource has a different kind.");
        return resource;
    }

    public OperationResult Apply(ReplicatedOperation operation, long replicationIndex = 0)
    {
        if (operation.Id == Guid.Empty || Encoding.UTF8.GetByteCount(operation.PayloadJson) > Limits.MaxBatchBytes)
            throw Errors.Fail(ErrorCode.ResourceExhausted, "The operation ID or byte budget is invalid.");
        return Store.Commit((tx, position) =>
        {
            var resultKey = KeySpace.Outcome(operation.PrincipalId, operation.Id);
            if (replicationIndex > 0 && tx.Get(KeySpace.Applied) is { } appliedBytes && JsonDefaults.Deserialize<long>(appliedBytes) >= replicationIndex)
                return tx.GetRecord<StoredOutcome>(resultKey)?.Result ?? new("null");
            var fingerprint = JsonData.Fingerprint(new { operation.Id, operation.Kind, operation.PrincipalId, operation.PayloadJson });
            long policyEpoch = 0;
            OperationResult result;
            try
            {
                var principal = Principal(tx, operation.PrincipalId, operation.EvaluatedAt);
                policyEpoch = principal.PolicyEpoch;
                AuthorizeOperation(tx, principal, operation);
                if (tx.GetRecord<StoredOutcome>(resultKey) is { } previous)
                {
                    if (previous.Incarnation != Store.Identity.Incarnation)
                        throw Errors.Fail(ErrorCode.TokenInvalidated, "This command belongs to a previous database incarnation.");
                    if (previous.Fingerprint != fingerprint)
                        throw Errors.Fail(ErrorCode.Conflict, "The command ID was already used with different content.");
                    ValidateCachedResult(tx, principal, operation, previous);
                    if (replicationIndex > 0) tx.PutRecord(KeySpace.Applied, replicationIndex);
                    return previous.Result;
                }
                if (tx.Get(KeySpace.Clock) is { } clock && operation.EvaluatedAt < JsonDefaults.Deserialize<DateTimeOffset>(clock))
                    throw Errors.Fail(ErrorCode.ClockUncertain, "The leader clock is behind the committed clock.");
                result = Execute(tx, principal, operation, replicationIndex > 0 ? replicationIndex : position);
            }
            catch (KeyLoadException exception) when (exception.Code is not (ErrorCode.Corruption or ErrorCode.RecoveryRequired or ErrorCode.UnknownWriteOutcome))
            {
                tx.Reset();
                result = new(null, exception.Code, exception.Message);
            }
            catch (JsonException)
            {
                tx.Reset();
                result = new(null, ErrorCode.Validation, "The operation contains invalid protocol JSON.");
            }
            // The outcome and apply watermark share the same redo transaction as every domain effect.
            if (tx.Get(resultKey) is null) tx.PutRecord(resultKey, new StoredOutcome(fingerprint, Store.Identity.Incarnation, policyEpoch, result));
            if (replicationIndex > 0) tx.PutRecord(KeySpace.Applied, replicationIndex);
            if (tx.Get(KeySpace.Clock) is not { } oldClock || operation.EvaluatedAt >= JsonDefaults.Deserialize<DateTimeOffset>(oldClock))
                tx.PutRecord(KeySpace.Clock, operation.EvaluatedAt);
            return result;
        });
    }

    private sealed record StoredOutcome(string Fingerprint, Guid Incarnation, long PolicyEpoch, OperationResult Result);
    public OperationResult? Outcome(string principal, Guid id) => Store.Read(v => v.GetRecord<StoredOutcome>(KeySpace.Outcome(principal, id))?.Result);
    public OperationResult ResolveOutcome(ReplicatedOperation operation) => Store.Read(view =>
    {
        try
        {
            var principal = Principal(view, operation.PrincipalId, DateTimeOffset.UtcNow);
            AuthorizeOperation(view, principal, operation);
            var outcome = view.GetRecord<StoredOutcome>(KeySpace.Outcome(operation.PrincipalId, operation.Id))
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, "The applied command has no durable outcome.");
            if (outcome.Incarnation != Store.Identity.Incarnation) throw Errors.Fail(ErrorCode.TokenInvalidated, "The command belongs to an earlier incarnation.");
            if (outcome.Fingerprint != JsonData.Fingerprint(new { operation.Id, operation.Kind, operation.PrincipalId, operation.PayloadJson }))
                throw Errors.Fail(ErrorCode.Conflict, "The command ID was already used with different content.");
            ValidateCachedResult(view, principal, operation with { EvaluatedAt = DateTimeOffset.UtcNow }, outcome);
            return outcome.Result;
        }
        catch (KeyLoadException exception) { return new OperationResult(null, exception.Code, exception.Message); }
    });
    private void ValidateCachedResult(IKeyValueView view, PrincipalRecord principal, ReplicatedOperation operation, StoredOutcome previous)
    {
        if (previous.PolicyEpoch != principal.PolicyEpoch)
            throw Errors.Fail(ErrorCode.PermissionDenied, "The principal policy changed since this command was evaluated.");
        if (operation.Kind == OperationKind.Receive && previous.Result.Error is null)
            foreach (var delivery in previous.Result.Get<ReceiveResult>().Deliveries)
                Lease(view, principal, Payload<ReceiveRequest>(operation).Lane, delivery.Token, operation.EvaluatedAt);
        if (operation.Kind == OperationKind.ReceiveSubscription && previous.Result.Error is null)
        {
            var cached = previous.Result.Get<ReceiveSubscriptionResult>();
            var state = Group(view, Payload<ReceiveSubscriptionRequest>(operation).Subscription);
            if (cached.Status.Generation != state.Generation || cached.Status.OwnershipEpoch != state.OwnershipEpoch)
                throw Errors.Fail(ErrorCode.TokenInvalidated, "The cached receive belongs to an earlier subscription generation.");
            foreach (var delivery in cached.Deliveries)
                SubscriptionLease(view, principal, Payload<ReceiveSubscriptionRequest>(operation).Subscription, delivery.Token, operation.EvaluatedAt);
        }
        if (operation.Kind == OperationKind.SubscriptionProcessing && previous.Result.Error is null)
        {
            var processing = Payload<SubscriptionProcessingRequest>(operation);
            ValidateGroupClaims(view, principal, processing.Subscription, processing.Token, operation.EvaluatedAt);
            ReauthorizeEffects(view, principal, processing.Subscription.Source.Partition, processing.Effects);
        }
    }
    private static T Payload<T>(ReplicatedOperation operation) => JsonDefaults.Deserialize<T>(Encoding.UTF8.GetBytes(operation.PayloadJson));
    private static OperationResult Result<T>(T value) => new(JsonSerializer.Serialize(value, JsonDefaults.Options));
    public CommitToken Token(PartitionRef partition, long position) => new(Store.Identity.Incarnation, partition.AtomicPartitionId, position, 1);
    private void AuthorizeOperation(IKeyValueView view, PrincipalRecord principal, ReplicatedOperation operation)
    {
        switch (operation.Kind)
        {
            case OperationKind.Batch: AuthorizeBatch(view, principal, Payload<CommandRequest>(operation)); break;
            case OperationKind.Receive:
                var receive = Payload<ReceiveRequest>(operation);
                Authorization.Require(principal, receive.Lane.Partition, receive.Lane.Queue, Capability.QueueConsume);
                Authorization.RequireWorkerInput(principal, Resource(view, receive.Lane.Partition, receive.Lane.Queue, ResourceKind.WorkQueue));
                break;
            case OperationKind.Delivery:
                var delivery = Payload<DeliveryCommand>(operation);
                Authorization.Require(principal, delivery.Lane.Partition, delivery.Lane.Queue,
                    delivery.Action == DeliveryAction.Renew ? Capability.QueueRenew : Capability.QueueAck);
                break;
            case OperationKind.Processing:
                var processing = Payload<ProcessingRequest>(operation);
                Authorization.Require(principal, processing.Lane.Partition, processing.Lane.Queue, Capability.QueueAck);
                AuthorizeBatch(view, principal, new(processing.CommandId, processing.Lane.Partition, processing.Effects), allowEmpty: true);
                break;
            case OperationKind.ConfigureSubscription:
                AuthorizeSubscription(view, principal, Payload<ConfigureSubscriptionRequest>(operation).Subscription,
                    Capability.SubscriptionsManage, operation.EvaluatedAt); break;
            case OperationKind.SeekSubscription:
                AuthorizeSubscription(view, principal, Payload<SeekSubscriptionRequest>(operation).Subscription,
                    Capability.SubscriptionsManage, operation.EvaluatedAt); break;
            case OperationKind.SetSubscriptionPaused:
                AuthorizeSubscription(view, principal, Payload<SetSubscriptionPausedRequest>(operation).Subscription,
                    Capability.SubscriptionsManage, operation.EvaluatedAt); break;
            case OperationKind.ReceiveSubscription:
                AuthorizeSubscription(view, principal, Payload<ReceiveSubscriptionRequest>(operation).Subscription,
                    Capability.SubscriptionsConsume, operation.EvaluatedAt, requireDataPrincipal: true); break;
            case OperationKind.SubscriptionDelivery:
                AuthorizeSubscription(view, principal, Payload<SubscriptionDeliveryCommand>(operation).Subscription,
                    Capability.SubscriptionsAck, operation.EvaluatedAt, requireDataPrincipal: true); break;
            case OperationKind.SubscriptionProcessing:
                var subscriptionProcessing = Payload<SubscriptionProcessingRequest>(operation);
                AuthorizeSubscription(view, principal, subscriptionProcessing.Subscription, Capability.SubscriptionsAck,
                    operation.EvaluatedAt, requireDataPrincipal: true);
                AuthorizeBatch(view, principal, new(subscriptionProcessing.CommandId, subscriptionProcessing.Subscription.Source.Partition, subscriptionProcessing.Effects), allowEmpty: true);
                break;
            default:
                if (!principal.ClusterAdministrator) throw Errors.Fail(ErrorCode.PermissionDenied, "Cluster administration is required.");
                break;
        }
    }
    private void AuthorizeBatch(IKeyValueView view, PrincipalRecord principal, CommandRequest request, bool allowEmpty = false)
    {
        ValidatePartition(request.Partition);
        if (request.OwnershipEpoch != 1) throw Errors.Fail(ErrorCode.OwnershipLost, "The partition ownership epoch is stale.");
        if (!allowEmpty && request.Mutations.Length == 0 || request.Mutations.Length > Limits.MaxBatchMutations)
            throw Errors.Fail(ErrorCode.ResourceExhausted, "The mutation count exceeds its budget.");
        foreach (var mutation in request.Mutations)
        {
            JsonData.Identifier(mutation.Resource);
            var capability = mutation switch
            {
                PutDocument or PatchDocument or DeleteDocument => Capability.DocumentsWrite,
                AppendEvents => Capability.EventsAppend, PublishTopic => Capability.TopicsPublish, EnqueueMessage => Capability.QueuePublish,
                UpsertEdge or DeleteEdge => Capability.GraphWrite, AppendSamples => Capability.SeriesAppend,
                PutVector => Capability.DocumentsWrite, _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, "This mutation is unsupported.")
            };
            Authorization.Require(principal, request.Partition, mutation.Resource, capability);
            Resource(view, request.Partition, mutation.Resource);
        }
    }
    public static void ValidatePartition(PartitionRef partition)
    {
        JsonData.Identifier(partition.TenantId); JsonData.Identifier(partition.DatabaseId);
        JsonData.Identifier(partition.TransactionDomainId); JsonData.Identifier(partition.PartitionKey);
    }

    private OperationResult Execute(IAtomicTransaction tx, PrincipalRecord principal, ReplicatedOperation operation, long position)
    {
        switch (operation.Kind)
        {
            case OperationKind.Batch:
                var batch = Payload<CommandRequest>(operation);
                if (batch.CommandId != operation.Id) throw Errors.Fail(ErrorCode.Validation, "The envelope and command IDs differ.");
                return Result(new CommitReceipt(batch.CommandId, Token(batch.Partition, position),
                    ApplyMutations(tx, principal, batch.Partition, batch.Mutations, operation.EvaluatedAt), Durability));
            case OperationKind.Receive: return Result(Receive(tx, principal, Payload<ReceiveRequest>(operation), operation.EvaluatedAt, position));
            case OperationKind.Delivery: return Result(CompleteDelivery(tx, principal, Payload<DeliveryCommand>(operation), operation.EvaluatedAt, position));
            case OperationKind.Processing: return Result(CompleteProcessing(tx, principal, Payload<ProcessingRequest>(operation), operation.EvaluatedAt, position));
            case OperationKind.ConfigureSubscription:
                var subscription = Payload<ConfigureSubscriptionRequest>(operation); RequireEnvelopeId(operation, subscription.CommandId);
                return Result(ConfigureSubscription(tx, principal, subscription, operation.EvaluatedAt));
            case OperationKind.SeekSubscription:
                var seek = Payload<SeekSubscriptionRequest>(operation); RequireEnvelopeId(operation, seek.CommandId);
                return Result(SeekSubscription(tx, principal, seek, operation.EvaluatedAt));
            case OperationKind.SetSubscriptionPaused:
                var paused = Payload<SetSubscriptionPausedRequest>(operation); RequireEnvelopeId(operation, paused.CommandId);
                return Result(SetSubscriptionPaused(tx, paused));
            case OperationKind.ReceiveSubscription:
                var subscriptionReceive = Payload<ReceiveSubscriptionRequest>(operation); RequireEnvelopeId(operation, subscriptionReceive.RequestId);
                return Result(ReceiveSubscription(tx, principal, subscriptionReceive, operation.EvaluatedAt, position));
            case OperationKind.SubscriptionDelivery:
                var subscriptionDelivery = Payload<SubscriptionDeliveryCommand>(operation); RequireEnvelopeId(operation, subscriptionDelivery.CommandId);
                return Result(CompleteSubscriptionDelivery(tx, principal, subscriptionDelivery, operation.EvaluatedAt, position));
            case OperationKind.SubscriptionProcessing:
                var subscriptionProcess = Payload<SubscriptionProcessingRequest>(operation); RequireEnvelopeId(operation, subscriptionProcess.CommandId);
                return Result(CompleteSubscriptionProcessing(tx, principal, subscriptionProcess, operation.EvaluatedAt, position));
            case OperationKind.ConfigureResource:
                var config = Payload<ConfigureResourceRequest>(operation);
                ValidateResource(config);
                var key = KeySpace.Resource(config.TenantId, config.DatabaseId, config.Definition.Name);
                var previous = tx.GetRecord<ResourceDefinition>(key);
                if (previous is not null && JsonData.Fingerprint(previous) != JsonData.Fingerprint(config.Definition))
                    throw Errors.Fail(ErrorCode.UnsupportedCapability, "Resource migrations require an explicit migration job.");
                tx.PutRecord(key, config.Definition);
                return Result(config.Definition);
            case OperationKind.ConfigurePrincipal:
                var target = Payload<ConfigurePrincipalRequest>(operation).Principal;
                JsonData.Identifier(target.Id); JsonData.Identifier(target.TenantId);
                var old = tx.GetRecord<PrincipalRecord>(KeySpace.Principal(target.Id));
                if (old is not null && target.PolicyEpoch <= old.PolicyEpoch)
                    throw Errors.Fail(ErrorCode.RevisionConflict, "A policy update must advance its epoch.");
                tx.PutRecord(KeySpace.Principal(target.Id), target);
                return Result(target);
            case OperationKind.ConfigureApiKey:
                var apiKey = Payload<ConfigureApiKeyRequest>(operation).ApiKey;
                JsonData.Identifier(apiKey.Id); Principal(tx, apiKey.PrincipalId, operation.EvaluatedAt);
                if (apiKey.Verifier.Length != 64 || !apiKey.Verifier.All(Uri.IsHexDigit)) throw Errors.Fail(ErrorCode.Validation, "The credential verifier is invalid.");
                tx.PutRecord(KeySpace.ApiKey(apiKey.Id), apiKey);
                return Result(true);
            case OperationKind.Membership:
                var membership = Payload<MembershipMutation>(operation);
                var membershipKey = KeyCodec.Encode("membership", membership.Key);
                var row = tx.GetRecord<MembershipRecord>(membershipKey);
                if (membership.ExpectedVersion != (row?.Version ?? 0)) return Result(false);
                tx.PutRecord(membershipKey, new MembershipRecord((row?.Version ?? 0) + 1, membership.Json));
                return Result(true);
            case OperationKind.SetDispatch:
                tx.PutRecord(KeyCodec.Encode("system", "dispatch-paused"), Payload<bool>(operation));
                return Result(true);
            default: throw Errors.Fail(ErrorCode.UnsupportedCapability, "The operation is unsupported.");
        }
    }
    private void ValidateResource(ConfigureResourceRequest request)
    {
        JsonData.Identifier(request.TenantId); JsonData.Identifier(request.DatabaseId);
        JsonData.Identifier(request.Definition.Name); JsonData.Identifier(request.Definition.TransactionDomainId);
        var definition = request.Definition;
        if (definition.Indexes.Length > 32 || definition.FieldPolicies.Length > 256 || definition.HeaderPolicies.Length > 256)
            throw Errors.Fail(ErrorCode.ResourceExhausted, "The resource schema exceeds its budget.");
        if (definition.Indexes.Select(i => i.Name).Distinct(StringComparer.Ordinal).Count() != definition.Indexes.Length)
            throw Errors.Fail(ErrorCode.Validation, "Index names must be unique.");
        foreach (var index in definition.Indexes)
        {
            JsonData.Identifier(index.Name);
            if (index.Fields.Length is < 1 or > 8) throw Errors.Fail(ErrorCode.Validation, "An index requires one to eight fields.");
            foreach (var field in index.Fields) JsonData.PathSegments(field);
        }
        foreach (var policy in definition.FieldPolicies.Concat(definition.HeaderPolicies)) JsonData.PathSegments(policy.Path);
        var q = definition.QueuePolicy;
        if (definition.EventRetention.MaxEvents < 1 || definition.EventRetention.MaxBytes < 1)
            throw Errors.Fail(ErrorCode.Validation, "The retained event quota is invalid.");
        if (q.MaxAttempts < 1 || q.MaxLeaseSeconds is < 1 or > 3_600 || q.MaxStoredMessages < 1 || q.MaxStoredBytes < 1
            || q.MaxInFlightMessages < 1 || q.MaxInFlightBytes < 1 || q.RetryBaseMilliseconds < 1 || q.RetryMaxMilliseconds < q.RetryBaseMilliseconds)
            throw Errors.Fail(ErrorCode.Validation, "The queue policy is invalid.");
    }

    private MutationReceipt[] ApplyMutations(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, Mutation[] mutations, DateTimeOffset now)
    {
        var receipts = new List<MutationReceipt>();
        foreach (var mutation in mutations)
        {
            receipts.Add(mutation switch
            {
                PutDocument put => Put(tx, principal, partition, put, now),
                PatchDocument patch => Patch(tx, principal, partition, patch, now),
                DeleteDocument delete => Delete(tx, principal, partition, delete, now),
                AppendEvents events => Append(tx, principal, partition, events, now),
                PublishTopic topic => Publish(tx, principal, partition, topic, now),
                EnqueueMessage message => Enqueue(tx, principal, partition, message, now),
                UpsertEdge edge => Upsert(tx, principal, partition, edge),
                DeleteEdge edge => RemoveEdge(tx, principal, partition, edge),
                AppendSamples samples => Append(tx, principal, partition, samples),
                PutVector vector => Upsert(tx, principal, partition, vector),
                _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, "The mutation is unsupported.")
            });
        }
        foreach (var collection in mutations.Where(mutation => mutation is PutDocument or PatchDocument or DeleteDocument)
            .Select(mutation => mutation.Resource).Distinct(StringComparer.Ordinal))
            tx.PutRecord(KeySpace.Partition("document-epoch", partition, collection), checked(DocumentEpoch(tx, partition, collection) + 1));
        return receipts.ToArray();
    }
    private static void RequireEnvelopeId(ReplicatedOperation operation, Guid id)
    {
        if (id != operation.Id) throw Errors.Fail(ErrorCode.Validation, "The envelope and request IDs differ.");
    }
}
public sealed record MembershipMutation(string Key, long ExpectedVersion, string Json);
public sealed record MembershipRecord(long Version, string Json);
