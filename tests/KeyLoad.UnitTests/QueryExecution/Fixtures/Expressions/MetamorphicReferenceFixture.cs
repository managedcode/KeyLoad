using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class MetamorphicReferenceFixture(int seed) : IDisposable
{
    private static readonly DateTimeOffset Epoch = new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
    internal TestDatabase Harness { get; } = new(timeProvider: new ControlledReadClock(Epoch));
    internal ImmutableArray<MetamorphicReferenceRow> Rows { get; } = MetamorphicReferenceData.Rows(seed);
    internal CommandRequest Seed { get; private set; } = null!;
    internal CommitReceipt Receipt { get; private set; } = null!;
    internal byte[] Outcome { get; private set; } = null!;
    internal async Task InitializeAsync()
    {
        await Assert.That(Rows.Length).IsEqualTo(MetamorphicReferenceData.Count);
        await Assert.That(Rows.Count(row => row.Missing)).IsEqualTo(10);
        await Assert.That(Rows.Count(row => !row.Missing && row.Score is null)).IsEqualTo(10);
        await Assert.That(Rows.Any(row => row.Score == decimal.MinValue)).IsTrue();
        await Assert.That(Rows.Any(row => row.Score == decimal.MaxValue)).IsTrue();
        Harness.Configure(MetamorphicReferenceData.Collection, ResourceKind.Collection,
            indexes: [new(MetamorphicReferenceData.Index, ["/status"])],
            fields: [new("/status", "protected-status", MetamorphicReferenceData.ReadGrant, MetamorphicReferenceData.UseGrant),
                new("/title", "private-title", "title.read", "title.use")]);
        Seed = new(Guid.NewGuid(), Harness.Partition,
            [.. Rows.Select(row => (Mutation)new PutDocument(MetamorphicReferenceData.Collection, row.Id, row.Json))]);
        var before = Harness.Store.Position;
        Receipt = Harness.Submit(OperationKind.Batch, Seed, id: Seed.CommandId).Get<CommitReceipt>();
        var expected = new CommitReceipt(Seed.CommandId, new(Harness.Store.Identity.Incarnation, Harness.Partition.AtomicPartitionId, before + 1L), [.. Rows.Select(row => new MutationReceipt("putDocument", MetamorphicReferenceData.Collection, row.Id, 1L))], DurabilityProfile.ProcessDurable, 1);
        await MetamorphicReferenceAssertions.EqualAsync(Receipt, expected);
        await NativeReceiptAsync(Receipt);
        await Assert.That(Harness.Store.Position).IsEqualTo(before + 1L);
        Outcome = Harness.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(Harness.Partition, "root", Seed.CommandId)))
            ?? throw new InvalidOperationException("The original generated-corpus outcome must exist.");
        await PolicyAsync(false, 1L);
        Harness.Submit(OperationKind.ConfigureApiKey, new ConfigureApiKeyRequest(DatabaseEngine.Credential(
            MetamorphicReferenceData.Reader, MetamorphicReferenceData.Reader, MetamorphicReferenceData.Secret))).Get<bool>();
    }
    internal async Task PolicyAsync(bool use, long epoch)
    {
        var expected = new PrincipalRecord(MetamorphicReferenceData.Reader, Harness.Partition.TenantId,
            [new(Harness.Partition.DatabaseId, MetamorphicReferenceData.Collection, Capability.Query | Capability.DocumentsRead)],
            use ? [MetamorphicReferenceData.ReadGrant, MetamorphicReferenceData.UseGrant] : [MetamorphicReferenceData.ReadGrant])
        {
            PolicyEpoch = epoch
        };
        await MetamorphicReferenceAssertions.EqualAsync(Harness.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(expected)).Get<PrincipalRecord>(), expected);
        await MetamorphicReferenceAssertions.EqualAsync(Harness.Store.Read(view =>
            view.GetRecord<PrincipalRecord>(KeySpace.Principal(MetamorphicReferenceData.Reader))), expected);
    }
    internal static async Task NativeReceiptAsync(CommitReceipt receipt)
    {
        foreach (var mutation in receipt.Mutations)
        {
            await Assert.That(mutation.CompositionReferences).IsEmpty();
        }
    }
    public void Dispose() => Harness.Dispose();
}
