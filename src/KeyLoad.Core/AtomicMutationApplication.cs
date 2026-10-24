using System.Collections.Immutable;
using KeyLoad.Core.DatabaseComposition;
using KeyLoad.Core.DocumentStorage;
using KeyLoad.Core.Messaging;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes AtomicMutationApplication operations against the caller's database context.</summary>
public static class AtomicMutationApplication
{
    internal const int AtomicMutationApplicationInitialSequence = 0;
    internal const int AtomicMutationApplicationAdjacentElementOffset = 1;
    internal const int AtomicMutationApplicationEmptyElementCount = 0;
    internal const string UnsupportedMutationMessage = "The mutation is unsupported.";
    internal const string DocumentEpochSpace = "document-epoch";
    internal static ImmutableArray<MutationReceipt> ApplyMutations(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, ImmutableArray<Mutation> mutations, DateTimeOffset now, long position, bool allowOutboxProgressReserve = false, CommitToken? commitToken = null, Command<DatabaseCommand>? operation = null, bool preparingComposition = false, ReadExecutionBudget? compositionBudget = null)
    {
        var token = commitToken ?? engine.Token(tx, partition, position);
        var receipts = ImmutableArray.CreateBuilder<MutationReceipt>(mutations.Length);
        TopicPurgeReadBudget? purgeBudget = null;
        var originalPayload = preparingComposition ? operation!.Value! : null;
        List<QueueRetryDecision>? previewChoices = preparingComposition ? [] : null;
        foreach (var (mutation, derived) in engine.ExpandCommandMutations(tx, principal, partition, mutations, now, compositionBudget))
        {
            var documentId = mutation switch
            {
                PutDocument put => put.Id,
                PatchDocument patch => patch.Id,
                DeleteDocument delete => delete.Id,
                _ => null
            };
            var key = documentId is null ? null : DocumentStorageExecutionDocuments.DocumentKey(partition, mutation.Resource, documentId);
            var before = key is null ? null : tx.GetRecord<DocumentRecord>(key);
            var context = new DocumentMutationContext(key!, before);
            DocumentMutationResult? image = mutation switch
            {
                PutDocument put => engine.Put(tx, principal, partition, put, now, context),
                PatchDocument patch => engine.Patch(tx, principal, partition, patch, now, context),
                DeleteDocument delete => engine.Delete(tx, principal, partition, delete, now, context),
                _ => null
            };
            if (mutation is PurgeTopic)
            {
                purgeBudget ??= new TopicPurgeReadBudget(engine.OperationLimitsOptions);
            }

            var effectOperation = preparingComposition ? engine.PrepareCompositionQueueRetry(tx, operation!, partition, mutation, originalPayload!, previewChoices!) : operation;
            var receipt = image?.Receipt ?? engine.ApplyNonDocumentMutation(tx, principal, partition, mutation, now, position, purgeBudget, effectOperation);
            if (derived)
            {
                receipt = receipt with
                {
                    CompositionReferences = engine.CompositionMutationReferences(mutation)
                };
            }

            receipts.Add(receipt);
            var after = image?.After;
            engine.AppendOutbox(tx, partition, new(AtomicMutationApplicationInitialSequence, receipts.Count - AtomicMutationApplicationAdjacentElementOffset, token, now, mutation, receipt, before, after), allowOutboxProgressReserve);
            if (before is not null && after is not null && before.Access != after.Access)
            {
                ChangeFeedsExecutionChangeFeeds.AdvanceVisibilityEpoch(tx, partition, mutation.Resource);
            }
        }

        foreach (var collection in mutations.Where(mutation => mutation is InsertTypedRow or PutDocument or PatchDocument or DeleteDocument).Select(mutation => mutation.Resource).Distinct(StringComparer.Ordinal))
        {
            tx.PutRecord(KeySpace.Partition(DocumentEpochSpace, partition, collection), checked(engine.DocumentEpoch(tx, partition, collection) + AtomicMutationApplicationAdjacentElementOffset));
        }

        ResourceEffectCompositionScopes.RequireCompleteCompositionEffects(tx, operation, preparingComposition);
        return receipts.Count == receipts.Capacity ? receipts.MoveToImmutable() : receipts.ToImmutable();
    }

    internal static IEnumerable<(Mutation Effect, bool Derived)> ExpandCommandMutations(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, ImmutableArray<Mutation> mutations, DateTimeOffset now, ReadExecutionBudget? compositionBudget = null)
    {
        var count = AtomicMutationApplicationEmptyElementCount;
        var budget = compositionBudget;
        foreach (var requested in mutations)
        {
            if (requested is InsertTypedRow insert)
            {
                engine.AcceptExpandedMutation(ref count);
                yield return (engine.ExpandTypedRowInsert(tx, principal, partition, insert), false);
                continue;
            }

            if (requested is not (QueueToGraph or GraphToQueueMutation))
            {
                engine.AcceptExpandedMutation(ref count);
                yield return (requested, false);
                continue;
            }

            budget ??= new ReadExecutionBudget(engine.OperationLimitsOptions, CompositionTimeProvider.Instance);
            foreach (var effect in engine.ExpandComposition(tx, principal, partition, requested, now, budget))
            {
                engine.AcceptExpandedMutation(ref count);
                yield return (effect, true);
            }
        }
    }

    internal static void AcceptExpandedMutation(this DatabaseEngine engine, ref int count)
    {
        if (++count > engine.Limits.MaxBatchMutations)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, CommandAuthorization.MutationCountBudgetMessage);
        }
    }

    internal static MutationReceipt ApplyNonDocumentMutation(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, Mutation mutation, DateTimeOffset now, long position, TopicPurgeReadBudget? purgeBudget, Command<DatabaseCommand>? operation) => mutation switch
    {
        UpsertAnalyticsEvents analytics => engine.ApplyAnalyticsUpsert(tx, principal, partition, analytics, now),
        DeleteAnalyticsEvents analytics => engine.ApplyAnalyticsDelete(tx, principal, partition, analytics, now),
        AppendEvents events => engine.Append(tx, principal, partition, events, now),
        PublishTopic topic => engine.Publish(tx, principal, partition, topic, now),
        PurgeTopic topic => engine.PurgeTopicHistory(tx, partition, topic, purgeBudget),
        EnqueueMessage message => engine.Enqueue(tx, principal, partition, message, now),
        AdvanceQueueDeadline message => engine.ApplyQueueDeadline(tx, partition, message, now, operation),
        RedriveQueueMessage message => engine.ApplyRedriveQueueMessage(tx, partition, message, now),
        CancelQueueMessage message => engine.ApplyCancelQueueMessage(tx, partition, message),
        ParkPendingQueueMessage message => engine.ApplyParkPendingQueueMessage(tx, partition, message, now),
        UpsertEdge edge => engine.Upsert(tx, principal, partition, edge),
        DeleteEdge edge => engine.RemoveEdge(tx, principal, partition, edge),
        ApplyCrossPartitionReverseEdge edge => engine.ApplyGraphReverseDelivery(tx, principal, partition, edge),
        CompleteCrossPartitionReverseEdge edge => engine.CompleteGraphReverseDelivery(tx, principal, partition, edge),
        AppendSamples samples => engine.Append(tx, principal, partition, samples, now),
        OpenSampleChunkWindow window => global::KeyLoad.Core.TimeSeries.SampleChunkWindowOpen.Execute(engine, tx, principal, partition, window, engine.TimeSeriesOptions),
        SealSampleChunkWindow window => global::KeyLoad.Core.TimeSeries.SampleChunkWindowSeal.Execute(engine, tx, principal, partition, window, engine.TimeSeriesOptions),
        MergeSampleChunkWindow window => global::KeyLoad.Core.TimeSeries.SampleChunkWindowMerge.Execute(engine, tx, principal, partition, window, engine.TimeSeriesOptions),
        DropSampleChunkWindow window => global::KeyLoad.Core.TimeSeries.SampleChunkWindowDrop.Execute(engine, tx, principal, partition, window, engine.TimeSeriesOptions),
        ExpireSamples samples => engine.Expire(tx, principal, partition, samples, now),
        RefreshSampleRollup rollup => global::KeyLoad.Core.TimeSeries.SampleRollupCommands.Refresh(engine, tx, principal, partition, rollup, engine.TimeSeriesExecution.MaximumRollupBuckets),
        DropSampleRollup rollup => global::KeyLoad.Core.TimeSeries.SampleRollupCommands.Drop(engine, tx, principal, partition, rollup),
        StoreAggregateSnapshot snapshot => engine.SaveAggregateSnapshot(tx, principal, partition, snapshot),
        PutVector vector => engine.Upsert(tx, principal, partition, vector),
        CreateQueueTransfer transfer => engine.ApplyCreateQueueTransfer(tx, principal, partition, transfer, now),
        AcceptQueueTransfer transfer => engine.ApplyAcceptQueueTransfer(tx, principal, partition, transfer, now, position),
        CompleteQueueTransfer transfer => engine.ApplyCompleteQueueTransfer(tx, principal, partition, transfer),
        AdvanceQueueTransferAttempt transfer => engine.ApplyAdvanceQueueTransferAttempt(tx, principal, partition, transfer),
        AdvanceQueueTransferRepair transfer => engine.ApplyAdvanceQueueTransferRepair(tx, principal, partition, transfer),
        ApplyVectorProjection projection => engine.ApplyVectorProjection(tx, principal, partition, projection),
        ConfigureRecurringSchedule schedule => engine.ApplyConfigureRecurringSchedule(tx, principal, partition, schedule, now),
        EmitRecurringOccurrences schedule => engine.ApplyEmitRecurringOccurrences(tx, principal, partition, schedule, now),
        CancelRecurringSchedule schedule => engine.ApplyCancelRecurringSchedule(tx, principal, partition, schedule, now),
        CompareExchangeSaga saga => engine.ApplyCompareExchangeSaga(tx, principal, partition, saga, now),
        ExpireSaga saga => engine.ApplyExpireSaga(tx, principal, partition, saga, now),
        _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedMutationMessage)
    };
}
