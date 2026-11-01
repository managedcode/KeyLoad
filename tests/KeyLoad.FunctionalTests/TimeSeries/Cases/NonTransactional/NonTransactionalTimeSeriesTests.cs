using KeyLoad.FunctionalTests.RelationalStorage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal sealed class NonTransactionalTimeSeriesTests
{
    [Test]
    public async Task PlainSeriesKeepsNativeDataAndTransactionalOutcomeAcrossColdOpen()
    {
        using var fixture = new NonTransactionalTableFixture();
        var definition = new ResourceDefinition(RelationalTestData.Table, ResourceKind.TimeSeries, fixture.Partition.TransactionDomainId)
        {
            Transactional = false
        };
        (await fixture.SubmitAsync(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(fixture.Partition.TenantId, fixture.Partition.DatabaseId, definition))).Get<ResourceDefinition>();
        var timestamp = SampleAggregateTestData.Start;
        var id = Guid.NewGuid();
        var mutation = new AppendSamples(definition.Name, RelationalTestData.First, [new(RelationalTestData.First, timestamp, 1.25)]);
        var first = (await fixture.BatchAsync(id, mutation)).Get<CommitReceipt>();
        await Assert.That((await fixture.BatchAsync(id, mutation)).Get<CommitReceipt>()).IsEquivalentTo(first);
        var key = KeySpace.Partition("sample", fixture.Partition, definition.Name, RelationalTestData.First, timestamp, 1L);
        var expected = fixture.Store.Read(view => view.ReadOwnedValue(key))!;
        fixture.Store.Dispose();
        using (var plain = ZoneTreeTreeFactory.Plain(fixture.Destination(NonTransactionalTableFixture.PlainDirectory)).Open())
        using (var tree = ZoneTreeTreeFactory.Open(new(fixture.Owner.Directory), requireExisting: true))
        using (var snapshot = tree.BeginReadOnlyTransaction())
        {
            await Assert.That(plain.TryGet(key, out var row)).IsTrue();
            await Assert.That(row.Value.ToArray()).IsEquivalentTo(expected, CollectionOrdering.Matching);
            await Assert.That(snapshot.TryGet(key, out _)).IsFalse();
            await Assert.That(snapshot.TryGet(fixture.OutcomeKey(id), out _)).IsTrue();
        }
        using var cold = NonTransactionalTableFixture.Open(fixture.Owner.Directory);
        var database = fixture.Owner.CreateConfiguredDatabase(cold);
        var rows = database.ReadSamples(RelationalTestData.Root, fixture.Partition, definition.Name, RelationalTestData.First,
            timestamp, timestamp.AddTicks(1));
        await Assert.That(rows).HasSingleItem();
        await Assert.That(rows[0].Sample.Value).IsEqualTo(1.25);
        await Assert.That(rows[0].Sequence).IsEqualTo(1L);
    }
}
