using ManagedCode.Communication.Commands;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class SparseCompositePrefixFixture
{
    internal static async Task<List<(Command<DatabaseCommand> Operation, byte[] Result)>> SeedAsync(
        TestDatabase fixture, bool includeNull, bool includeMissing)
    {
        foreach (var includeStatusIndex in new[] { false, true })
        {
            fixture.Configure(includeStatusIndex ? SparseCompositePrefixData.WithStatusIndex : SparseCompositePrefixData.Only,
                ResourceKind.Collection, indexes: SparseCompositePrefixData.Indexes(includeNull, includeMissing, includeStatusIndex).ToArray(),
                fields: [new("/status", "restricted", SparseCompositePrefixData.ReadGrant, SparseCompositePrefixData.UseGrant)]);
        }
        var mutations = new[] { SparseCompositePrefixData.Only, SparseCompositePrefixData.WithStatusIndex }
            .SelectMany(collection => SparseCompositePrefixData.Rows.Select(row =>
                (Mutation)new PutDocument(collection, row.Id, SparseCompositePrefixRequests.SeedJson(row.Id, row.Json)))).ToArray();
        var command = new CommandRequest(Guid.NewGuid(), fixture.Partition, [.. mutations]);
        var originals = new List<(Command<DatabaseCommand> Operation, byte[] Result)>();
        var before = fixture.ReadCanonicalCut().AppliedPosition;
        var identity = fixture.Store.Identity;
        var receipt = QueryAdapterNullParityReplay.Apply(fixture.Database, OperationKind.Batch,
            command.CommandId, command, originals).Get<CommitReceipt>();
        await NoReferencesAsync(receipt);
        var expected = new CommitReceipt(command.CommandId, new(identity.Incarnation, fixture.Partition.AtomicPartitionId, before + 1L), [.. mutations.Cast<PutDocument>().Select(row => new MutationReceipt("putDocument", row.Resource, row.Id, 1L))], identity.Durability, (identity.Durability == DurabilityProfile.Buffered ? 0 : 1));
        await Assert.That(JsonDefaults.Serialize(receipt)).IsEquivalentTo(JsonDefaults.Serialize(expected), CollectionOrdering.Matching);
        await PolicyAsync(fixture.Database, fixture.Partition, true, 1L, originals);
        return originals;
    }
    internal static async Task NoReferencesAsync(CommitReceipt receipt)
    {
        foreach (var mutation in receipt.Mutations)
        {
            await Assert.That(mutation.CompositionReferences.IsEmpty).IsTrue();
        }
    }
    internal static async Task PolicyAsync(global::KeyLoad.Core.DatabaseEngine owner, PartitionRef partition,
        bool use, long epoch, List<(Command<DatabaseCommand> Operation, byte[] Result)> originals)
    {
        var principal = new PrincipalRecord(SparseCompositePrefixData.Reader, partition.TenantId,
            [new(partition.DatabaseId, SparseCompositePrefixData.Only, Capability.Query | Capability.DocumentsRead),
             new(partition.DatabaseId, SparseCompositePrefixData.WithStatusIndex, Capability.Query | Capability.DocumentsRead)],
            use ? [SparseCompositePrefixData.ReadGrant, SparseCompositePrefixData.UseGrant] : [SparseCompositePrefixData.ReadGrant])
        {
            PolicyEpoch = epoch
        };
        var actual = QueryAdapterNullParityReplay.Apply(owner, OperationKind.ConfigurePrincipal, Guid.NewGuid(),
            new ConfigurePrincipalRequest(principal), originals).Get<PrincipalRecord>();
        await Assert.That(JsonDefaults.Serialize(actual)).IsEquivalentTo(JsonDefaults.Serialize(principal), CollectionOrdering.Matching);
    }
}
