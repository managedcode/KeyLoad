using System.Collections.Immutable;
using KeyLoad.Core.Features.ChangeFeeds;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{

    private static byte[] OutboxKey(PartitionRef partition, long sequence) => KeySpace.Partition("outbox", partition, sequence);
    private static byte[] ConsumerKey(ProjectionConsumerRef consumer) => KeySpace.Partition("projection-consumer", consumer.Partition, consumer.Name);
    /// <summary>Reads persisted outbox head metadata from the current storage cut.</summary>
    /// <param name="view">Current gated storage view.</param>
    /// <param name="partition">Owning atomic partition.</param>
    /// <returns>The persisted head, or its initial value.</returns>
    public OutboxHead ReadOutboxHead(IKeyValueView view, PartitionRef partition)
        => view.GetRecord<OutboxHead>(KeySpace.Partition("outbox-head", partition)) ?? new(0, 1, 0, 0);
    private void AppendOutbox(IAtomicTransaction tx, PartitionRef partition, OutboxEntry entry, bool allowProgressReserve)
    {
        var head = ReadOutboxHead(tx, partition);
        entry = entry with { Sequence = checked(head.Tail + 1) };
        var payload = NativeSerialization.Serialize(entry);
        var maxRecords = checked(Limits.MaxOutboxRecords + (allowProgressReserve ? Limits.ReservedOutboxRecords : 0));
        var maxBytes = checked(Limits.MaxOutboxBytes + (allowProgressReserve ? Limits.ReservedOutboxBytes : 0));
        if (head.StoredRecords >= maxRecords || payload.Length > maxBytes - head.StoredBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, "The committed outbox quota is exhausted; advance consumers and reclaim retained entries.");
        }

        tx.Put(OutboxKey(partition, entry.Sequence), payload);
        tx.PutRecord(KeySpace.Partition("outbox-head", partition), head with
        { Tail = entry.Sequence, StoredRecords = head.StoredRecords + 1, StoredBytes = head.StoredBytes + payload.Length });
    }
    private static ProjectionConsumerInfo ProjectionConsumer(IKeyValueView view, ProjectionConsumerRef consumer)
    {
        ValidatePartition(consumer.Partition);
        JsonData.Identifier(consumer.Name);
        return view.GetRecord<ProjectionConsumerInfo>(ConsumerKey(consumer))
            ?? throw Errors.Fail(ErrorCode.NotFound, "The projection consumer is unavailable.");
    }
    private ImmutableArray<ProjectionConsumerInfo> ProjectionConsumers(IKeyValueView view, PartitionRef partition)
    {
        var page = view.Scan(KeySpace.Partition("projection-consumer", partition), Limits.MaxProjectionConsumers);
        if (page.HasMore)
        {
            throw Errors.Fail(ErrorCode.Corruption, "The projection consumer limit is inconsistent.");
        }

        return page.Records.Select(record => NativeSerialization.Deserialize<ProjectionConsumerInfo>(record.Value.Span)).ToImmutableArray();
    }
    private ProjectionConsumerInfo ConfigureProjectionConsumer(IAtomicTransaction tx, ConfigureProjectionConsumerRequest request)
    {
        ValidatePartition(request.Consumer.Partition);
        JsonData.Identifier(request.Consumer.Name);
        var definition = request.Definition;
        if (definition.Resources.IsDefault || definition.MutationKinds.IsDefault
            || definition.IndexGeneration < 1 || definition.Resources.Length > 256 || definition.MutationKinds.Length > 32)
        {
            throw Errors.Fail(ErrorCode.Validation, "The projection generation or filter is invalid.");
        }

        foreach (var resource in definition.Resources)
        { JsonData.Identifier(resource); Resource(tx, request.Consumer.Partition, resource); }
        foreach (var kind in definition.MutationKinds)
        {
            if (kind is not ("putDocument" or "patchDocument" or "deleteDocument" or "appendEvents" or "publishTopic" or "enqueue"
                or "upsertEdge" or "deleteEdge" or "appendSamples" or "putVector"))
            {
                throw Errors.Fail(ErrorCode.UnsupportedCapability, "The projection mutation kind is unsupported.");
            }
        }

        var key = ConsumerKey(request.Consumer);
        if (tx.GetRecord<ProjectionConsumerInfo>(key) is { } previous)
        {
            if (JsonData.Fingerprint(previous.Definition) != JsonData.Fingerprint(definition) || previous.Released
                || request.StartAfter is { } start && start != previous.Checkpoint)
            {
                throw Errors.Fail(ErrorCode.Conflict, "Use a new consumer identity for a different index generation or rebuild.");
            }

            return previous;
        }
        if (ProjectionConsumers(tx, request.Consumer.Partition).Length >= Limits.MaxProjectionConsumers)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, "The projection consumer quota is exhausted.");
        }

        var head = ReadOutboxHead(tx, request.Consumer.Partition);
        var checkpoint = request.StartAfter ?? head.FirstAvailable - 1;
        CheckOutboxPosition(head, checkpoint);
        var state = new ProjectionConsumerInfo(request.Consumer, definition, checkpoint, false);
        tx.PutRecord(key, state);
        return state;
    }
    private static void RequireProjectionAdministrator(PrincipalRecord principal)
    {
        if (!principal.ClusterAdministrator)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, "System projection administration is required.");
        }
    }
    private static void CheckOutboxPosition(OutboxHead head, long after)
    {
        if (after < head.FirstAvailable - 1)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, "The requested outbox history is no longer retained.");
        }

        if (after > head.Tail || after < 0)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, "The outbox position is invalid.");
        }
    }
    private static bool ProjectionAccepts(ProjectionConsumerDefinition definition, OutboxEntry entry)
        => (definition.Resources.Length == 0 || definition.Resources.Contains(entry.Receipt.Resource, StringComparer.Ordinal))
            && (definition.MutationKinds.Length == 0 || definition.MutationKinds.Contains(entry.Receipt.Kind, StringComparer.Ordinal));
    private static IEnumerable<OutboxEntry> ReadOutboxRange(IKeyValueView view, PartitionRef partition,
        long after, long tail, int limit)
    {
        foreach (var stored in StoredOutboxReader.ReadRange(view, partition, after, tail, limit))
        {
            yield return stored.Entry;
        }
    }
    /// <summary>Reads a bounded authorized projection batch.</summary>
    /// <param name="principalId">Persisted principal identifier.</param>
    /// <param name="request">Consumer and byte/count budget.</param>
    /// <returns>The ordered batch and signed continuation token.</returns>
    public ProjectionBatch ReadProjectionBatch(string principalId, ReadProjectionBatchRequest request) => Store.Read<ProjectionBatch>(view =>
    {
        var now = Clock.GetUtcNow();
        var principal = Principal(view, principalId, now);
        RequireProjectionAdministrator(principal);
        if (request.Limit < 1 || request.Limit > Limits.MaxResults || request.MaxBytes < 1 || request.MaxBytes > Limits.MaxProjectionBatchBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, "The projection batch exceeds its budget.");
        }

        var state = ProjectionConsumer(view, request.Consumer);
        if (state.Released)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, "The projection generation was released.");
        }

        var head = ReadOutboxHead(view, request.Consumer.Partition);
        CheckOutboxPosition(head, state.Checkpoint);
        var entries = new List<OutboxEntry>();
        var through = state.Checkpoint;
        var bytes = 0;
        foreach (var stored in StoredOutboxReader.ReadRange(view, request.Consumer.Partition, through, head.Tail, request.Limit))
        {
            var entry = stored.Entry;
            if (!ProjectionAccepts(state.Definition, entry))
            {
                through = entry.Sequence;
                continue;
            }

            var size = stored.StoredBytes;
            if (size > request.MaxBytes - bytes)
            {
                if (through == state.Checkpoint)
                {
                    throw Errors.Fail(ErrorCode.BudgetExceeded, "The first projection entry exceeds the batch byte budget.");
                }

                break;
            }
            entries.Add(entry);
            bytes += size;
            through = entry.Sequence;
        }
        return new(state, entries.ToImmutableArray(), through, Sign(new ProjectionBatchClaims("projection-batch", Store.Identity.Incarnation,
            request.Consumer, state.Definition.IndexGeneration, state.Checkpoint, through, now.AddMinutes(5))), through < head.Tail);
    });
    private static byte[] ProjectionReceiptKey(ProjectionBatchClaims claims)
        => KeySpace.Partition("projection-receipt", claims.Consumer.Partition, claims.Consumer.Name, claims.IndexGeneration, claims.After, claims.Through);
    private ProjectionBatchClaims ProjectionClaims(IKeyValueView view, CommitProjectionBatchRequest request, DateTimeOffset now, bool allowExpiredReceipt = false)
    {
        var claims = Verify<ProjectionBatchClaims>(request.Token);
        var state = ProjectionConsumer(view, request.Consumer);
        if (claims.Purpose != "projection-batch" || claims.Incarnation != Store.Identity.Incarnation || claims.Consumer != request.Consumer
            || claims.IndexGeneration != state.Definition.IndexGeneration || state.Released || claims.Through < claims.After)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, "The projection batch belongs to a different generation or scope.");
        }

        if (claims.ExpiresAt <= now && !(allowExpiredReceipt && view.ReadOwnedValue(ProjectionReceiptKey(claims)) is not null))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, "The projection batch token expired.");
        }

        return claims;
    }
    private ProjectionBatchResult CommitProjectionBatch(IAtomicTransaction tx, PrincipalRecord principal,
        CommitProjectionBatchRequest request, DateTimeOffset now, long position)
    {
        var claims = ProjectionClaims(tx, request, now, allowExpiredReceipt: true);
        AuthorizeBatch(tx, principal, new(request.CommandId, request.Consumer.Partition, request.Effects), allowEmpty: true);
        ReauthorizeEffects(tx, principal, request.Consumer.Partition, request.Effects);
        var key = ProjectionReceiptKey(claims);
        var fingerprint = JsonData.Fingerprint(request.Effects);
        if (tx.GetRecord<ProjectionReceipt>(key) is { } completed)
        {
            if (fingerprint != completed.Fingerprint)
            {
                throw Errors.Fail(ErrorCode.Conflict, "The projection batch was completed with different effects.");
            }

            return new(completed.Receipt, true, completed.Checkpoint);
        }
        var state = ProjectionConsumer(tx, request.Consumer);
        var head = ReadOutboxHead(tx, request.Consumer.Partition);
        CheckOutboxPosition(head, claims.After);
        if (state.Checkpoint != claims.After || claims.Through > head.Tail)
        {
            throw Errors.Fail(ErrorCode.Conflict, "The projection checkpoint changed; reread the next batch.");
        }

        if (claims.Through == claims.After)
        {
            if (request.Effects.Length != 0)
            {
                throw Errors.Fail(ErrorCode.Validation, "An empty projection batch cannot produce effects.");
            }

            return new(new(request.CommandId, Token(tx, request.Consumer.Partition, position), [], Durability), false, state.Checkpoint);
        }
        var effects = ApplyMutations(tx, principal, request.Consumer.Partition, request.Effects, now, position,
            allowOutboxProgressReserve: state.LastProgressReservationCut != head.FirstAvailable);
        var receipt = new CommitReceipt(request.CommandId, Token(tx, request.Consumer.Partition, position), effects, Durability);
        var after = ReadOutboxHead(tx, request.Consumer.Partition);
        var usedReserve = effects.Length > 0 && (after.StoredRecords > Limits.MaxOutboxRecords || after.StoredBytes > Limits.MaxOutboxBytes);
        tx.PutRecord(ConsumerKey(request.Consumer), state with
        {
            Checkpoint = claims.Through,
            LastProgressReservationCut = usedReserve ? head.FirstAvailable : state.LastProgressReservationCut
        });
        tx.PutRecord(key, new ProjectionReceipt(fingerprint, receipt, claims.Through));
        return new(receipt, false, claims.Through);
    }
    private static ProjectionConsumerInfo ReleaseProjectionConsumer(IAtomicTransaction tx, ReleaseProjectionConsumerRequest request)
    {
        var state = ProjectionConsumer(tx, request.Consumer);
        if (state.Definition.IndexGeneration != request.IndexGeneration)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, "The projection index generation is stale.");
        }

        state = state with { Released = true };
        tx.PutRecord(ConsumerKey(request.Consumer), state);
        return state;
    }
    private OutboxHead PurgeOutbox(IAtomicTransaction tx, PurgeOutboxRequest request)
    {
        ValidatePartition(request.Partition);
        if (request.Limit < 1 || request.Limit > Limits.MaxResults)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, "The outbox purge exceeds its batch limit.");
        }

        var head = ReadOutboxHead(tx, request.Partition);
        if (request.ThroughSequence < 0 || request.ThroughSequence > head.Tail)
        {
            throw Errors.Fail(ErrorCode.Validation, "The purge position is invalid.");
        }

        var pinned = ProjectionConsumers(tx, request.Partition).Where(consumer => !consumer.Released).Select(consumer => consumer.Checkpoint)
            .DefaultIfEmpty(head.Tail).Min();
        if (request.ThroughSequence > pinned)
        {
            throw Errors.Fail(ErrorCode.Conflict, "A projection consumer or rebuild pins the requested outbox history.");
        }

        var requestedTail = Math.Min(head.Tail, request.ThroughSequence);
        foreach (var stored in StoredOutboxReader.ReadRange(tx, request.Partition,
                     head.FirstAvailable - 1, requestedTail, request.Limit))
        {
            tx.Delete(stored.Key);
            head = head with
            {
                FirstAvailable = stored.Entry.Sequence + 1,
                StoredRecords = head.StoredRecords - 1,
                StoredBytes = head.StoredBytes - stored.StoredBytes
            };
        }
        tx.PutRecord(KeySpace.Partition("outbox-head", request.Partition), head);
        return head;
    }
    /// <summary>Reads administrative outbox and consumer status.</summary>
    /// <param name="principalId">Persisted administrator identifier.</param>
    /// <param name="partition">Owning atomic partition.</param>
    /// <returns>Current outbox head and immutable consumer states.</returns>
    public OutboxStatus GetOutboxStatus(string principalId, PartitionRef partition) => Store.Read<OutboxStatus>(view =>
    {
        RequireProjectionAdministrator(Principal(view, principalId, Clock.GetUtcNow()));
        ValidatePartition(partition);
        return new(ReadOutboxHead(view, partition), ProjectionConsumers(view, partition));
    });
}
