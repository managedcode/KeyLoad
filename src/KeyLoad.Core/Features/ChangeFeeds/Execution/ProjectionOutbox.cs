using System.Collections.Immutable;
using KeyLoad.Core.Features.ChangeFeeds;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string ConsumerIdentityRequiresNewGenerationDetail = "Use a new consumer identity for a different index generation or rebuild.";
    private const string ProjectionConsumerQuotaExhaustedDetail = "The projection consumer quota is exhausted.";
    private const string ReleasedProjectionGenerationDetail = "The projection generation was released.";
    private const string FirstProjectionEntryExceedsBudgetDetail = "The first projection entry exceeds the batch byte budget.";
    private const string EmptyProjectionBatchEffectsDetail = "An empty projection batch cannot produce effects.";
    private const string PinnedOutboxHistoryDetail = "A projection consumer or rebuild pins the requested outbox history.";

    private const string OutboxEntrySpace = "outbox";
    private const string ProjectionConsumerSpace = "projection-consumer";
    private const string OutboxHeadSpace = "outbox-head";
    private const int ReadOutboxHeadTailEmptyCount = 0;
    private const int ReadOutboxHeadFirstAvailableSingleItemCount = 1;
    private const int ReadOutboxHeadStoredRecordsEmptyCount = 0;
    private const int ReadOutboxHeadStoredBytesEmptyCount = 0;
    private const int ProjectionAcceptsEmptyResourcesLength = 0;
    private const int ProjectionAcceptsEmptyMutationKindsLength = 0;
    private const int MinimumProjectionReadCount = 1;
    private const string ProjectionBatchBudgetDetail = "The projection batch exceeds its budget.";
    private const int InitialProjectionPageBytes = 0;
    private const string ProjectionReceiptSpace = "projection-receipt";

    private const string ProjectionBatchTokenPurpose = "projection-batch";

    private static byte[] OutboxKey(PartitionRef partition, long sequence) => KeySpace.Partition(OutboxEntrySpace, partition, sequence);
    private static byte[] ConsumerKey(ProjectionConsumerRef consumer) => KeySpace.Partition(ProjectionConsumerSpace, consumer.Partition, consumer.Name);
    /// <summary>Reads persisted outbox head metadata from the current storage cut.</summary>
    /// <param name="view">Current gated storage view.</param>
    /// <param name="partition">Owning atomic partition.</param>
    /// <returns>The persisted head, or its initial value.</returns>
    public OutboxHead ReadOutboxHead(IKeyValueView view, PartitionRef partition)
        => view.GetRecord<OutboxHead>(KeySpace.Partition(OutboxHeadSpace, partition)) ?? new(ReadOutboxHeadTailEmptyCount, ReadOutboxHeadFirstAvailableSingleItemCount, ReadOutboxHeadStoredRecordsEmptyCount, ReadOutboxHeadStoredBytesEmptyCount);
    private void AppendOutbox(IAtomicTransaction tx, PartitionRef partition, OutboxEntry entry, bool allowProgressReserve)
    {
        const int TailStep = 1;
        const int NoProgressReserveRecords = 0;
        const string AppendOutboxDetailText = "The committed outbox quota is exhausted; advance consumers and reclaim retained entries.";
        const string AppendOutboxSpaceText = "outbox-head";
        const int AppendedRecordCount = 1;

        var head = ReadOutboxHead(tx, partition);
        entry = entry with { Sequence = checked(head.Tail + TailStep) };
        var payload = NativeSerialization.Serialize(entry);
        var maxRecords = checked(Limits.MaxOutboxRecords + (allowProgressReserve ? Limits.ReservedOutboxRecords : NoProgressReserveRecords));
        var maxBytes = checked(Limits.MaxOutboxBytes + (allowProgressReserve ? Limits.ReservedOutboxBytes : NoProgressReserveRecords));
        if (head.StoredRecords >= maxRecords || payload.Length > maxBytes - head.StoredBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, AppendOutboxDetailText);
        }

        tx.Put(OutboxKey(partition, entry.Sequence), payload);
        tx.PutRecord(KeySpace.Partition(AppendOutboxSpaceText, partition), head with
        { Tail = entry.Sequence, StoredRecords = head.StoredRecords + AppendedRecordCount, StoredBytes = head.StoredBytes + payload.Length });
    }
    private static ProjectionConsumerInfo ProjectionConsumer(IKeyValueView view, ProjectionConsumerRef consumer)
    {
        const string ProjectionConsumerDetailText = "The projection consumer is unavailable.";

        ValidatePartition(consumer.Partition);
        JsonData.Identifier(consumer.Name);
        return view.GetRecord<ProjectionConsumerInfo>(ConsumerKey(consumer))
            ?? throw Errors.Fail(ErrorCode.NotFound, ProjectionConsumerDetailText);
    }
    private ImmutableArray<ProjectionConsumerInfo> ProjectionConsumers(IKeyValueView view, PartitionRef partition)
    {
        const string ProjectionConsumersSpaceText = "projection-consumer";
        const string ProjectionConsumersDetailText = "The projection consumer limit is inconsistent.";

        var page = view.Scan(KeySpace.Partition(ProjectionConsumersSpaceText, partition), Limits.MaxProjectionConsumers);
        if (page.HasMore)
        {
            throw Errors.Fail(ErrorCode.Corruption, ProjectionConsumersDetailText);
        }

        return page.Records.Select(record => NativeSerialization.Deserialize<ProjectionConsumerInfo>(record.Value.Span)).ToImmutableArray();
    }
    private ProjectionConsumerInfo ConfigureProjectionConsumer(IAtomicTransaction tx, ConfigureProjectionConsumerRequest request)
    {
        const int IndexGenerationValidationBoundary = 1;
        const string InvalidProjectionFilterDetail = "The projection generation or filter is invalid.";
        const string UnsupportedProjectionMutationDetail = "The projection mutation kind is unsupported.";
        const int FirstAvailableStep = 1;

        ValidatePartition(request.Consumer.Partition);
        JsonData.Identifier(request.Consumer.Name);
        var definition = request.Definition;
        if (definition.Resources.IsDefault || definition.MutationKinds.IsDefault
            || definition.IndexGeneration < IndexGenerationValidationBoundary || definition.Resources.Length > changeFeedExecution.MaximumProjectionResources || definition.MutationKinds.Length > changeFeedExecution.MaximumProjectionMutationKinds)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidProjectionFilterDetail);
        }

        foreach (var resource in definition.Resources)
        { JsonData.Identifier(resource); Resource(tx, request.Consumer.Partition, resource); }
        foreach (var kind in definition.MutationKinds)
        {
            if (kind is not (MutationDiscriminatorNames.PutDocument or MutationDiscriminatorNames.PatchDocument
                or MutationDiscriminatorNames.DeleteDocument or MutationDiscriminatorNames.AppendEvents
                or MutationDiscriminatorNames.PublishTopic or MutationDiscriminatorNames.EnqueueMessage
                or MutationDiscriminatorNames.UpsertEdge or MutationDiscriminatorNames.DeleteEdge
                or MutationDiscriminatorNames.AppendSamples or MutationDiscriminatorNames.PutVector
                or MutationDiscriminatorNames.ApplyVectorProjection))
            {
                throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedProjectionMutationDetail);
            }
        }

        var key = ConsumerKey(request.Consumer);
        if (tx.GetRecord<ProjectionConsumerInfo>(key) is { } previous)
        {
            if (JsonData.Fingerprint(previous.Definition) != JsonData.Fingerprint(definition) || previous.Released
                || request.StartAfter is { } start && start != previous.Checkpoint)
            {
                throw Errors.Fail(ErrorCode.Conflict, ConsumerIdentityRequiresNewGenerationDetail);
            }

            return previous;
        }
        if (ProjectionConsumers(tx, request.Consumer.Partition).Length >= Limits.MaxProjectionConsumers)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ProjectionConsumerQuotaExhaustedDetail);
        }

        var head = ReadOutboxHead(tx, request.Consumer.Partition);
        var checkpoint = request.StartAfter ?? head.FirstAvailable - FirstAvailableStep;
        CheckOutboxPosition(head, checkpoint);
        var state = new ProjectionConsumerInfo(request.Consumer, definition, checkpoint, false);
        tx.PutRecord(key, state);
        return state;
    }
    private static void RequireProjectionAdministrator(PrincipalRecord principal)
    {
        const string RequireProjectionAdministratorDetailText = "System projection administration is required.";

        if (!principal.ClusterAdministrator)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, RequireProjectionAdministratorDetailText);
        }
    }
    private static void CheckOutboxPosition(OutboxHead head, long after)
    {
        const int FirstAvailableStep = 1;
        const string RetainedOutboxHistoryUnavailableDetail = "The requested outbox history is no longer retained.";
        const int AfterValidationBoundary = 0;
        const string InvalidOutboxPositionDetail = "The outbox position is invalid.";

        if (after < head.FirstAvailable - FirstAvailableStep)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, RetainedOutboxHistoryUnavailableDetail);
        }

        if (after > head.Tail || after < AfterValidationBoundary)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidOutboxPositionDetail);
        }
    }
    private static bool ProjectionAccepts(ProjectionConsumerDefinition definition, OutboxEntry entry)
        => (definition.Resources.Length == ProjectionAcceptsEmptyResourcesLength || definition.Resources.Contains(entry.Receipt.Resource, StringComparer.Ordinal))
            && (definition.MutationKinds.Length == ProjectionAcceptsEmptyMutationKindsLength || definition.MutationKinds.Contains(entry.Receipt.Kind, StringComparer.Ordinal));
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
        if (request.Limit < MinimumProjectionReadCount || request.Limit > Limits.MaxResults || request.MaxBytes < MinimumProjectionReadCount || request.MaxBytes > Limits.MaxProjectionBatchBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ProjectionBatchBudgetDetail);
        }

        var state = ProjectionConsumer(view, request.Consumer);
        if (state.Released)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ReleasedProjectionGenerationDetail);
        }

        var head = ReadOutboxHead(view, request.Consumer.Partition);
        CheckOutboxPosition(head, state.Checkpoint);
        var upper = ProjectionUpperSequence(request, state, head);
        var entries = new List<OutboxEntry>();
        var through = state.Checkpoint;
        var bytes = InitialProjectionPageBytes;
        foreach (var stored in StoredOutboxReader.ReadRange(view, request.Consumer.Partition, through, upper, request.Limit))
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
                    throw Errors.Fail(ErrorCode.BudgetExceeded, FirstProjectionEntryExceedsBudgetDetail);
                }

                break;
            }
            entries.Add(entry);
            bytes += size;
            through = entry.Sequence;
        }
        return new(state, entries.ToImmutableArray(), through, Sign(new ProjectionBatchClaims(ProjectionBatchTokenPurpose, Store.Identity.Incarnation,
            request.Consumer, state.Definition.IndexGeneration, state.Checkpoint, through, now.Add(changeFeedExecution.ProjectionBatchLifetime))), through < upper);
    });
    private const string InvalidProjectionUpperSequence = "The projection upper sequence is outside the current consumer checkpoint and retained head.";

    private static long ProjectionUpperSequence(ReadProjectionBatchRequest request,
        ProjectionConsumerInfo state, OutboxHead head)
    {
        var upper = request.ThroughSequence ?? head.Tail;
        if (upper < state.Checkpoint || upper > head.Tail)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidProjectionUpperSequence);
        }
        return upper;
    }
    private static byte[] ProjectionReceiptKey(ProjectionBatchClaims claims)
        => KeySpace.Partition(ProjectionReceiptSpace, claims.Consumer.Partition, claims.Consumer.Name, claims.IndexGeneration, claims.After, claims.Through);
    private ProjectionBatchClaims ProjectionClaims(IKeyValueView view, CommitProjectionBatchRequest request, DateTimeOffset now, bool allowExpiredReceipt = false)
    {
        const string ProjectionBatchScopeMismatchDetail = "The projection batch belongs to a different generation or scope.";
        const string ExpiredProjectionBatchTokenDetail = "The projection batch token expired.";

        var claims = Verify<ProjectionBatchClaims>(request.Token);
        var state = ProjectionConsumer(view, request.Consumer);
        if (claims.Purpose != ProjectionBatchTokenPurpose || claims.Incarnation != Store.Identity.Incarnation || claims.Consumer != request.Consumer
            || claims.IndexGeneration != state.Definition.IndexGeneration || state.Released || claims.Through < claims.After)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ProjectionBatchScopeMismatchDetail);
        }

        if (claims.ExpiresAt <= now && !(allowExpiredReceipt && view.ReadOwnedValue(ProjectionReceiptKey(claims)) is not null))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ExpiredProjectionBatchTokenDetail);
        }

        return claims;
    }
    private ProjectionBatchResult CommitProjectionBatch(IAtomicTransaction tx, PrincipalRecord principal,
        CommitProjectionBatchRequest request, DateTimeOffset now, long position)
    {
        const string ConflictingProjectionEffectsDetail = "The projection batch was completed with different effects.";
        const string ChangedProjectionCheckpointDetail = "The projection checkpoint changed; reread the next batch.";
        const int EmptyEffectsLength = 0;
        const int EffectsLengthValidationBoundary = 0;

        var claims = ProjectionClaims(tx, request, now, allowExpiredReceipt: true);
        AuthorizeBatch(tx, principal, new(request.CommandId, request.Consumer.Partition, request.Effects), allowEmpty: true);
        ReauthorizeEffects(tx, principal, request.Consumer.Partition, request.Effects);
        var key = ProjectionReceiptKey(claims);
        var fingerprint = JsonData.Fingerprint(request.Effects);
        if (tx.GetRecord<ProjectionReceipt>(key) is { } completed)
        {
            if (fingerprint != completed.Fingerprint)
            {
                throw Errors.Fail(ErrorCode.Conflict, ConflictingProjectionEffectsDetail);
            }

            return new(completed.Receipt, true, completed.Checkpoint);
        }
        var state = ProjectionConsumer(tx, request.Consumer);
        var head = ReadOutboxHead(tx, request.Consumer.Partition);
        CheckOutboxPosition(head, claims.After);
        if (state.Checkpoint != claims.After || claims.Through > head.Tail)
        {
            throw Errors.Fail(ErrorCode.Conflict, ChangedProjectionCheckpointDetail);
        }

        if (claims.Through == claims.After)
        {
            if (request.Effects.Length != EmptyEffectsLength)
            {
                throw Errors.Fail(ErrorCode.Validation, EmptyProjectionBatchEffectsDetail);
            }

            return new(new(request.CommandId, Token(tx, request.Consumer.Partition, position), [], Durability), false, state.Checkpoint);
        }
        var effects = ApplyMutations(tx, principal, request.Consumer.Partition, request.Effects, now, position,
            allowOutboxProgressReserve: state.LastProgressReservationCut != head.FirstAvailable);
        var receipt = new CommitReceipt(request.CommandId, Token(tx, request.Consumer.Partition, position), effects, Durability);
        var after = ReadOutboxHead(tx, request.Consumer.Partition);
        var usedReserve = effects.Length > EffectsLengthValidationBoundary && (after.StoredRecords > Limits.MaxOutboxRecords || after.StoredBytes > Limits.MaxOutboxBytes);
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
        const string ReleaseProjectionConsumerDetailText = "The projection index generation is stale.";

        var state = ProjectionConsumer(tx, request.Consumer);
        if (state.Definition.IndexGeneration != request.IndexGeneration)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ReleaseProjectionConsumerDetailText);
        }

        state = state with { Released = true };
        tx.PutRecord(ConsumerKey(request.Consumer), state);
        return state;
    }
    private OutboxHead PurgeOutbox(IAtomicTransaction tx, PurgeOutboxRequest request)
    {
        const int LimitValidationBoundary = 1;
        const string OutboxPurgeBatchLimitDetail = "The outbox purge exceeds its batch limit.";
        const int ThroughSequenceValidationBoundary = 0;
        const string InvalidOutboxPurgePositionDetail = "The purge position is invalid.";
        const int AfterSingleItemCount = 1;
        const int SequenceStep = 1;
        const int StoredRecordsStep = 1;
        const string PurgeOutboxSpaceText = "outbox-head";

        ValidatePartition(request.Partition);
        if (request.Limit < LimitValidationBoundary || request.Limit > Limits.MaxResults)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, OutboxPurgeBatchLimitDetail);
        }

        var head = ReadOutboxHead(tx, request.Partition);
        if (request.ThroughSequence < ThroughSequenceValidationBoundary || request.ThroughSequence > head.Tail)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidOutboxPurgePositionDetail);
        }

        var pinned = ProjectionConsumers(tx, request.Partition).Where(consumer => !consumer.Released).Select(consumer => consumer.Checkpoint)
            .DefaultIfEmpty(head.Tail).Min();
        if (request.ThroughSequence > pinned)
        {
            throw Errors.Fail(ErrorCode.Conflict, PinnedOutboxHistoryDetail);
        }

        var requestedTail = Math.Min(head.Tail, request.ThroughSequence);
        foreach (var stored in StoredOutboxReader.ReadRange(tx, request.Partition,
                     head.FirstAvailable - AfterSingleItemCount, requestedTail, request.Limit))
        {
            tx.Delete(stored.Key);
            head = head with
            {
                FirstAvailable = stored.Entry.Sequence + SequenceStep,
                StoredRecords = head.StoredRecords - StoredRecordsStep,
                StoredBytes = head.StoredBytes - stored.StoredBytes
            };
        }
        tx.PutRecord(KeySpace.Partition(PurgeOutboxSpaceText, request.Partition), head);
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
