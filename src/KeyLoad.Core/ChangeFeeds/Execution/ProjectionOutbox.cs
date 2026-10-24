using KeyLoad.Core.ChangeFeeds;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes ProjectionOutbox operations against the caller's database context.</summary>
public static class ProjectionOutbox
{
    internal const string ConsumerIdentityRequiresNewGenerationDetail = "Use a new consumer identity for a different index generation or rebuild.";
    internal const string ProjectionConsumerQuotaExhaustedDetail = "The projection consumer quota is exhausted.";
    internal const string ReleasedProjectionGenerationDetail = "The projection generation was released.";
    internal const string FirstProjectionEntryExceedsBudgetDetail = "The first projection entry exceeds the batch byte budget.";
    internal const string EmptyProjectionBatchEffectsDetail = "An empty projection batch cannot produce effects.";
    internal const string PinnedOutboxHistoryDetail = "A projection consumer or rebuild pins the requested outbox history.";
    internal const string OutboxEntrySpace = "outbox";
    internal const string ProjectionConsumerSpace = "projection-consumer";
    internal const string OutboxHeadSpace = "outbox-head";
    internal const int ReadOutboxHeadTailEmptyCount = 0;
    internal const int ReadOutboxHeadFirstAvailableSingleItemCount = 1;
    internal const int ReadOutboxHeadStoredRecordsEmptyCount = 0;
    internal const int ReadOutboxHeadStoredBytesEmptyCount = 0;
    internal const int ProjectionAcceptsEmptyResourcesLength = 0;
    internal const int ProjectionAcceptsEmptyMutationKindsLength = 0;
    internal const int MinimumProjectionReadCount = 1;
    internal const string ProjectionBatchBudgetDetail = "The projection batch exceeds its budget.";
    internal const int InitialProjectionPageBytes = 0;
    internal const string ProjectionReceiptSpace = "projection-receipt";
    internal const string ProjectionBatchTokenPurpose = "projection-batch";
    internal static byte[] OutboxKey(PartitionRef partition, long sequence) => KeySpace.Partition(OutboxEntrySpace, partition, sequence);
    internal static byte[] ConsumerKey(ProjectionConsumerRef consumer) => KeySpace.Partition(ProjectionConsumerSpace, consumer.Partition, consumer.Name);
    internal static void AppendOutbox(this DatabaseEngine engine, IAtomicTransaction tx, PartitionRef partition, OutboxEntry entry)
    {
        const int TailStep = 1;
        const string AppendOutboxSpaceText = "outbox-head";
        const int AppendedRecordCount = 1;
        var head = engine.ReadOutboxHead(tx, partition);
        entry = entry with
        {
            Sequence = checked(head.Tail + TailStep)
        };
        var payload = OrleansSerialization.Serialize(entry);
        tx.Put(OutboxKey(partition, entry.Sequence), payload);
        tx.PutRecord(KeySpace.Partition(AppendOutboxSpaceText, partition), head with { Tail = entry.Sequence, StoredRecords = head.StoredRecords + AppendedRecordCount, StoredBytes = head.StoredBytes + payload.Length });
    }

    internal static ProjectionConsumerInfo ConfigureProjectionConsumer(this DatabaseEngine engine, IAtomicTransaction tx, ConfigureProjectionConsumerRequest request, ProjectionMaintenanceAuthority? authority)
    {
        const int FirstAvailableStep = 1;
        EngineResourceAccess.ValidatePartition(request.Consumer.Partition);
        JsonData.Identifier(request.Consumer.Name);
        var definition = request.Definition;
        engine.ValidateProjectionConsumerDefinition(tx, request.Consumer.Partition, definition);
        var key = ConsumerKey(request.Consumer);
        if (tx.GetRecord<ProjectionConsumerInfo>(key) is { } previous)
        {
            ProjectionConsumerIdentity.RequireProjectionConsumerIdentity(previous, request.Consumer);
            if (JsonData.Fingerprint(previous.Definition) != JsonData.Fingerprint(definition) || previous.Released || request.StartAfter is { } start && start != previous.Checkpoint)
            {
                throw Errors.Fail(ErrorCode.Conflict, ConsumerIdentityRequiresNewGenerationDetail);
            }

            ProjectionMaintenanceConfiguration.RequireProjectionMaintenanceAuthority(previous, authority);
            ProjectionMaintenanceConfiguration.RequireInitialMaintenanceReservation(previous, request);
            return previous;
        }

        if (engine.ProjectionConsumers(tx, request.Consumer.Partition).Length >= engine.Limits.MaxProjectionConsumers)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ProjectionConsumerQuotaExhaustedDetail);
        }

        var head = engine.ReadOutboxHead(tx, request.Consumer.Partition);
        var checkpoint = request.StartAfter ?? head.FirstAvailable - FirstAvailableStep;
        CheckOutboxPosition(head, checkpoint);
        var state = new ProjectionConsumerInfo(request.Consumer, definition, checkpoint, false)
        {
            MaintenanceAuthority = authority,
            InitialAnnMaintenance = request.InitialAnnMaintenance
        };
        if (OrleansSerialization.Measure(state) > engine.Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ProjectionMaintenanceConfiguration.InvalidMaintenanceProfile);
        }

        tx.PutRecord(key, state);
        return state;
    }

    internal static void ValidateProjectionConsumerDefinition(this DatabaseEngine engine, IAtomicTransaction tx, PartitionRef partition, ProjectionConsumerDefinition definition)
    {
        const int IndexGenerationValidationBoundary = 1;
        const string InvalidProjectionFilterDetail = "The projection generation or filter is invalid.";
        const string UnsupportedProjectionMutationDetail = "The projection mutation kind is unsupported.";
        if (definition.Resources.IsDefault || definition.MutationKinds.IsDefault || definition.IndexGeneration < IndexGenerationValidationBoundary || definition.Resources.Length > engine.ChangeFeedExecution.MaximumProjectionResources || definition.MutationKinds.Length > engine.ChangeFeedExecution.MaximumProjectionMutationKinds)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidProjectionFilterDetail);
        }

        foreach (var resource in definition.Resources)
        {
            JsonData.Identifier(resource);
            engine.Resource(tx, partition, resource);
        }

        foreach (var kind in definition.MutationKinds)
        {
            if (kind is not (AnalyticsStoreProtocol.UpsertKind or AnalyticsStoreProtocol.DeleteKind or MutationDiscriminatorNames.PutDocument or MutationDiscriminatorNames.PatchDocument or MutationDiscriminatorNames.DeleteDocument or MutationDiscriminatorNames.AppendEvents or MutationDiscriminatorNames.PublishTopic or MutationDiscriminatorNames.EnqueueMessage or MutationDiscriminatorNames.UpsertEdge or MutationDiscriminatorNames.DeleteEdge or MutationDiscriminatorNames.AppendSamples or MutationDiscriminatorNames.PutVector or MutationDiscriminatorNames.ApplyVectorProjection))
            {
                throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedProjectionMutationDetail);
            }
        }
    }

    internal static void CheckOutboxPosition(OutboxHead head, long after)
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

    internal static bool ProjectionAccepts(ProjectionConsumerDefinition definition, OutboxEntry entry) => (definition.Resources.Length == ProjectionAcceptsEmptyResourcesLength || definition.Resources.Contains(entry.Receipt.Resource, StringComparer.Ordinal)) && (definition.MutationKinds.Length == ProjectionAcceptsEmptyMutationKindsLength || definition.MutationKinds.Contains(entry.Receipt.Kind, StringComparer.Ordinal));
    internal static IEnumerable<OutboxEntry> ReadOutboxRange(IKeyValueView view, PartitionRef partition, long after, long tail, int limit)
    {
        foreach (var stored in StoredOutboxReader.ReadRange(view, partition, after, tail, limit))
        {
            yield return stored.Entry;
        }
    }

    internal const string InvalidProjectionUpperSequence = "The projection upper sequence is outside the current consumer checkpoint and retained head.";

    internal static ProjectionConsumerInfo ReleaseProjectionConsumer(IAtomicTransaction tx, ReleaseProjectionConsumerRequest request)
    {
        const string ReleaseProjectionConsumerDetailText = "The projection index generation is stale.";
        var state = ProjectionConsumerIdentity.ProjectionConsumer(tx, request.Consumer);
        if (state.Definition.IndexGeneration != request.IndexGeneration)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ReleaseProjectionConsumerDetailText);
        }

        state = state with
        {
            Released = true
        };
        tx.PutRecord(ConsumerKey(request.Consumer), state);
        return state;
    }

    internal static OutboxHead PurgeOutbox(this DatabaseEngine engine, IAtomicTransaction tx, PurgeOutboxRequest request)
    {
        const int LimitValidationBoundary = 1;
        const string OutboxPurgeBatchLimitDetail = "The outbox purge exceeds its batch limit.";
        const int ThroughSequenceValidationBoundary = 0;
        const string InvalidOutboxPurgePositionDetail = "The purge position is invalid.";
        const int AfterSingleItemCount = 1;
        const int SequenceStep = 1;
        const int StoredRecordsStep = 1;
        const string PurgeOutboxSpaceText = "outbox-head";
        EngineResourceAccess.ValidatePartition(request.Partition);
        if (request.Limit < LimitValidationBoundary || request.Limit > engine.Limits.MaxResults)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, OutboxPurgeBatchLimitDetail);
        }

        var head = engine.ReadOutboxHead(tx, request.Partition);
        if (request.ThroughSequence < ThroughSequenceValidationBoundary || request.ThroughSequence > head.Tail)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidOutboxPurgePositionDetail);
        }

        var consumers = engine.ProjectionConsumers(tx, request.Partition);
        foreach (var consumer in consumers)
        {
            ProjectionConsumerIdentity.RequireProjectionRetentionCheckpoint(consumer, head);
        }

        var pinned = consumers.Where(consumer => !consumer.Released).Select(consumer => consumer.Checkpoint).DefaultIfEmpty(head.Tail).Min();
        if (request.ThroughSequence > pinned)
        {
            throw Errors.Fail(ErrorCode.Conflict, PinnedOutboxHistoryDetail);
        }

        var requestedTail = Math.Min(head.Tail, request.ThroughSequence);
        foreach (var stored in StoredOutboxReader.ReadRange(tx, request.Partition, head.FirstAvailable - AfterSingleItemCount, requestedTail, request.Limit))
        {
            ChangeFeedsValidation.RequireOutboxReclaimAccounting(head, stored.StoredBytes);
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
    /// <param name = "principalId">Persisted administrator identifier.</param>
    /// <param name = "partition">Owning atomic partition.</param>
    /// <returns>Current outbox head and immutable consumer states.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static OutboxStatus GetOutboxStatus(this DatabaseEngine engine, string principalId, PartitionRef partition)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.Store.Read(view => engine.GetOutboxStatusRead(view, principalId, partition));
    }
}
