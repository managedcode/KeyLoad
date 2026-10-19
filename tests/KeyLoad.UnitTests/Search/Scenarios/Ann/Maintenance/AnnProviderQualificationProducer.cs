using System.Collections.Immutable;
using ManagedCode.Communication.Commands;
using static KeyLoad.UnitTests.Features.Search.AnnProviderQualificationProtocol;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnProviderQualificationProducer
{
    internal static async Task SeedAsync(AnnProviderQualificationOwner owner, AnnProviderQualificationState state,
        CancellationToken token)
    {
        var definition = new ResourceDefinition(Collection, ResourceKind.Collection, owner.Source.Partition.TransactionDomainId);
        var request = new ConfigureResourceRequest(owner.Source.Partition.TenantId, owner.Source.Partition.DatabaseId, definition);
        var operation = Create(owner, OperationKind.ConfigureResource, Guid.NewGuid(), request);
        var result = owner.Submit(operation, explicitTime: false, token);
        await AnnProviderQualificationHistoryAssertions.SaveAsync(owner, state, operation, result, definition);
        foreach (var row in AnnProviderQualificationCorpus.Expected(state))
        {
            await CommitAsync(owner, state, AnnProviderQualificationCorpus.Put(state, row.DocumentId, row.Values,
                MissingRevision, InitialRevision), [new(PutKind, Collection, row.DocumentId, InitialRevision),
                new(VectorKind, Collection, row.DocumentId, InitialRevision)], token);
        }
    }

    internal static async Task DeleteAsync(AnnProviderQualificationOwner owner, AnnProviderQualificationState state,
        CancellationToken token)
    {
        await CommitAsync(owner, state, [new DeleteDocument(Collection, Beta, InitialRevision)],
            [new(DeleteKind, Collection, Beta, DeletedRevision)], token);
        state.Deleted = true;
    }

    internal static async Task ReinsertAsync(AnnProviderQualificationOwner owner, AnnProviderQualificationState state,
        CancellationToken token)
    {
        await CommitAsync(owner, state, AnnProviderQualificationCorpus.Put(state, Beta, [PositiveComponent, PositiveComponent],
            DeletedRevision, ReinsertedRevision), [new(PutKind, Collection, Beta, ReinsertedRevision),
            new(VectorKind, Collection, Beta, ReinsertedRevision)], token);
        state.Reinserted = true;
    }

    internal static async Task HealthyAsync(AnnProviderQualificationOwner owner, AnnProviderQualificationState state,
        CancellationToken token)
    {
        await CommitAsync(owner, state, AnnProviderQualificationCorpus.Put(state, Alpha, [PositiveComponent, EmptyComponent],
            InitialRevision, HealthyRevision), [new(PutKind, Collection, Alpha, HealthyRevision),
            new(VectorKind, Collection, Alpha, HealthyRevision)], token);
        state.Healthy = true;
    }

    private static async Task CommitAsync(AnnProviderQualificationOwner owner, AnnProviderQualificationState state,
        ImmutableArray<Mutation> mutations, ImmutableArray<MutationReceipt> receipts, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var id = Guid.NewGuid();
        var placement = owner.Database.ReadAtomicPartitionPlacement(Root, owner.Source.Partition);
        var next = checked(owner.ReadCanonicalCut().AppliedPosition + NextJournalEntry);
        var expected = new CommitReceipt(id, new(placement.Incarnation, owner.Source.Partition.AtomicPartitionId,
            next), receipts, DurabilityProfile.ProcessDurable, 1);
        var operation = Create(owner, OperationKind.Batch, id, new CommandRequest(id, owner.Source.Partition, mutations));
        var result = owner.Submit(operation, explicitTime: false, token);
        await AnnProviderQualificationHistoryAssertions.SaveAsync(owner, state, operation, result, expected);
        await Assert.That(owner.ReadStoredOutcome(operation.CommandId).CanonicalSequence).IsEqualTo(next);
    }

    private static Command<DatabaseCommand> Create<T>(AnnProviderQualificationOwner owner, OperationKind kind, Guid id, T request)
        => owner.Database.CreateCommand(kind, id, Root, owner.Database.EvaluationClock.GetUtcNow(),
            OrleansSerialization.Serialize(request));
}
