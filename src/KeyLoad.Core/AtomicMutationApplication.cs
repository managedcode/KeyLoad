using System.Collections.Immutable;
using KeyLoad.Core.Features.DatabaseComposition;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string UnsupportedMutationMessage = "The mutation is unsupported.";
    private const string DocumentEpochSpace = "document-epoch";

    private ImmutableArray<MutationReceipt> ApplyMutations(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, ImmutableArray<Mutation> mutations,
        DateTimeOffset now, long position, bool allowOutboxProgressReserve = false, CommitToken? commitToken = null)
    {
        var token = commitToken ?? Token(tx, partition, position);
        var receipts = ImmutableArray.CreateBuilder<MutationReceipt>(mutations.Length);
        foreach (var (mutation, derived) in ExpandCommandMutations(tx, principal, partition, mutations, now))
        {
            var documentId = mutation switch { PutDocument put => put.Id, PatchDocument patch => patch.Id, DeleteDocument delete => delete.Id, _ => null };
            var key = documentId is null ? null : DocumentKey(partition, mutation.Resource, documentId);
            var before = key is null ? null : tx.GetRecord<DocumentRecord>(key);
            var context = new DocumentMutationContext(key!, before);
            DocumentMutationResult? image = mutation switch
            {
                PutDocument put => Put(tx, principal, partition, put, now, context),
                PatchDocument patch => Patch(tx, principal, partition, patch, now, context),
                DeleteDocument delete => Delete(tx, principal, partition, delete, now, context),
                _ => null
            };
            var receipt = image?.Receipt ?? ApplyNonDocumentMutation(tx, principal, partition, mutation, now, position);
            if (derived)
            {
                receipt = receipt with { CompositionReferences = CompositionMutationReferences(mutation) };
            }
            receipts.Add(receipt);
            var after = image?.After;
            AppendOutbox(tx, partition, new(0, receipts.Count - 1, token, now, mutation, receipt, before, after), allowOutboxProgressReserve);
            if (before is not null && after is not null && before.Access != after.Access)
            {
                AdvanceVisibilityEpoch(tx, partition, mutation.Resource);
            }
        }
        foreach (var collection in mutations.Where(mutation => mutation is PutDocument or PatchDocument or DeleteDocument)
            .Select(mutation => mutation.Resource).Distinct(StringComparer.Ordinal))
        {
            tx.PutRecord(KeySpace.Partition(DocumentEpochSpace, partition, collection), checked(DocumentEpoch(tx, partition, collection) + 1));
        }

        return receipts.Count == receipts.Capacity ? receipts.MoveToImmutable() : receipts.ToImmutable();
    }

    private IEnumerable<(Mutation Effect, bool Derived)> ExpandCommandMutations(IAtomicTransaction tx, PrincipalRecord principal,
        PartitionRef partition, ImmutableArray<Mutation> mutations, DateTimeOffset now)
    {
        var count = 0;
        ReadExecutionBudget? budget = null;
        foreach (var requested in mutations)
        {
            if (requested is not (QueueToGraph or GraphToQueueMutation))
            {
                AcceptExpandedMutation(ref count);
                yield return (requested, false);
                continue;
            }
            budget ??= new ReadExecutionBudget(Limits, CompositionTimeProvider.Instance);
            foreach (var effect in ExpandComposition(tx, principal, partition, requested, now, budget))
            {
                AcceptExpandedMutation(ref count);
                yield return (effect, true);
            }
        }
    }

    private void AcceptExpandedMutation(ref int count)
    {
        if (++count > Limits.MaxBatchMutations)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, MutationCountBudgetMessage);
        }
    }

    private MutationReceipt ApplyNonDocumentMutation(IAtomicTransaction tx, PrincipalRecord principal,
        PartitionRef partition, Mutation mutation, DateTimeOffset now, long position) => mutation switch
        {
            AppendEvents events => Append(tx, principal, partition, events, now),
            PublishTopic topic => Publish(tx, principal, partition, topic, now),
            EnqueueMessage message => Enqueue(tx, principal, partition, message, now),
            UpsertEdge edge => Upsert(tx, principal, partition, edge),
            DeleteEdge edge => RemoveEdge(tx, principal, partition, edge),
            ApplyCrossPartitionReverseEdge edge => ApplyGraphReverseDelivery(tx, principal, partition, edge),
            CompleteCrossPartitionReverseEdge edge => CompleteGraphReverseDelivery(tx, principal, partition, edge),
            AppendSamples samples => Append(tx, principal, partition, samples),
            ExpireSamples samples => Expire(tx, principal, partition, samples, now),
            StoreAggregateSnapshot snapshot => SaveAggregateSnapshot(tx, principal, partition, snapshot),
            PutVector vector => Upsert(tx, principal, partition, vector),
            CreateQueueTransfer transfer => ApplyCreateQueueTransfer(tx, principal, partition, transfer, now),
            AcceptQueueTransfer transfer => ApplyAcceptQueueTransfer(tx, principal, partition, transfer, now, position),
            CompleteQueueTransfer transfer => ApplyCompleteQueueTransfer(tx, principal, partition, transfer),
            ApplyVectorProjection projection => ApplyVectorProjection(tx, principal, partition, projection),
            ConfigureRecurringSchedule schedule => ApplyConfigureRecurringSchedule(tx, principal, partition, schedule, now),
            EmitRecurringOccurrences schedule => ApplyEmitRecurringOccurrences(tx, principal, partition, schedule, now),
            CancelRecurringSchedule schedule => ApplyCancelRecurringSchedule(tx, principal, partition, schedule, now),
            CompareExchangeSaga saga => ApplyCompareExchangeSaga(tx, principal, partition, saga, now),
            ExpireSaga saga => ApplyExpireSaga(tx, principal, partition, saga, now),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedMutationMessage)
        };
}
