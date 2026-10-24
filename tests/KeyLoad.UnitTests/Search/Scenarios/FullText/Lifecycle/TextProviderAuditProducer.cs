using System.Collections.Immutable;
using static KeyLoad.UnitTests.Search.TextProviderAuditProtocol;

namespace KeyLoad.UnitTests.Search;

internal static class TextProviderAuditProducer
{
    internal static async Task SeedAsync(TextProtectedRebuildColdOwner owner, TextProviderAuditState state)
    {
        var definition = new ResourceDefinition(Collection, ResourceKind.Collection, owner.Source.Partition.TransactionDomainId);
        var configured = owner.Send(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(owner.Source.Partition.TenantId, owner.Source.Partition.DatabaseId, definition));
        await TextProviderAuditHistoryAssertions.SaveAsync(owner, state, configured, definition);
        await CommitAsync(owner, state,
            [new PutDocument(Collection, Ukrainian, UkrainianJson, MissingRevision),
             new PutDocument(Collection, English, EnglishJson, MissingRevision),
             new PutDocument(Collection, Control, ControlJson, MissingRevision)],
            [new(PutKind, Collection, Ukrainian, InitialRevision), new(PutKind, Collection, English, InitialRevision),
             new(PutKind, Collection, Control, InitialRevision)]);
    }

    internal static async Task ChangeAsync(TextProtectedRebuildColdOwner owner, TextProviderAuditState state)
    {
        await CommitAsync(owner, state,
            [new PutDocument(Collection, Ukrainian, UpdatedJson, InitialRevision), new DeleteDocument(Collection, English, InitialRevision)],
            [new(PutKind, Collection, Ukrainian, ChangedRevision), new(DeleteKind, Collection, English, ChangedRevision)]);
        state.Updated = true;
    }

    internal static async Task HealthyAsync(TextProtectedRebuildColdOwner owner, TextProviderAuditState state)
    {
        await CommitAsync(owner, state, [new PutDocument(Collection, Healthy, HealthyJson, MissingRevision)],
            [new(PutKind, Collection, Healthy, InitialRevision)]);
        state.Healthy = true;
    }

    private static async Task CommitAsync(TextProtectedRebuildColdOwner owner, TextProviderAuditState state,
        ImmutableArray<Mutation> mutations, ImmutableArray<MutationReceipt> receipts)
    {
        var id = Guid.NewGuid();
        var placement = owner.Database.ReadAtomicPartitionPlacement(Root, owner.Source.Partition);
        var expected = new CommitReceipt(id, new(placement.Incarnation, owner.Source.Partition.AtomicPartitionId,
            checked(owner.Database.LastApplied + NextCommit)), receipts, DurabilityProfile.ProcessDurable, 1);
        var original = owner.Send(OperationKind.Batch, new CommandRequest(id, owner.Source.Partition, mutations), id);
        await TextProviderAuditHistoryAssertions.SaveAsync(owner, state, original, expected);
    }
}
